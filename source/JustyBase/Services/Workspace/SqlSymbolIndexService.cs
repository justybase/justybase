using System.Collections.Concurrent;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.Services.Workspace;

/// <summary>What an indexed SQL identifier is, as far as the fast lexical index can tell.</summary>
public enum SqlIndexedSymbolKind
{
    Table,
    Cte,
    Alias,
    Statement,
}

/// <summary>One identifier occurrence in one SQL file, with a position the editor can jump to.</summary>
public sealed record SqlIndexedSymbol(
    string Name,
    SqlIndexedSymbolKind Kind,
    string FilePath,
    int Line,
    int Column,
    int Offset);

/// <summary>
/// A workspace-wide index of SQL identifiers: tables, CTEs, aliases and statement starts. It is
/// deliberately lexical (the parser's tokenizer, not a full semantic model), so it stays fast,
/// never fails on incomplete SQL and can be built in the background. It is the foundation for
/// Go to Symbol, cross-file Find Usages and the Problems view.
/// </summary>
public interface ISqlSymbolIndexService
{
    /// <summary>True while a folder scan is running.</summary>
    bool IsIndexing { get; }

    /// <summary>The number of files currently in the index.</summary>
    int IndexedFileCount { get; }

    /// <summary>Starts one background scan of <paramref name="roots"/>; repeated calls are ignored while it runs.</summary>
    void StartIndexing(IEnumerable<string> roots);

    /// <summary>Rebuilds the index for <paramref name="roots"/>, reading only files that changed.</summary>
    Task RebuildAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default);

    /// <summary>Indexes an open document's text immediately (for unsaved edits).</summary>
    void IndexDocument(string filePath, string text);

    /// <summary>Drops one file from the index.</summary>
    void Invalidate(string filePath);

    /// <summary>Symbols whose name contains <paramref name="query"/>, best kinds first.</summary>
    IReadOnlyList<SqlIndexedSymbol> Search(string query, int limit = 50);

    /// <summary>Every indexed occurrence of exactly <paramref name="name"/>.</summary>
    IReadOnlyList<SqlIndexedSymbol> FindReferences(string name);
}

public sealed class SqlSymbolIndexService : ISqlSymbolIndexService
{
    /// <summary>Never index more than this many files in one scan; a guard against a wrong root.</summary>
    public const int MaxFilesPerScan = 5_000;

    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea",
        "bin", "obj", "node_modules", "artifacts", "publish", "releases",
    };

    private readonly ConcurrentDictionary<string, FileEntry> _documents = new(PathComparer);
    private readonly object _startGate = new();
    private int _indexing;

    public bool IsIndexing => Volatile.Read(ref _indexing) != 0;

    public int IndexedFileCount => _documents.Count;

    public void StartIndexing(IEnumerable<string> roots)
    {
        string[] folders = NormalizeRoots(roots);
        if (folders.Length == 0)
        {
            return;
        }

        lock (_startGate)
        {
            if (Interlocked.CompareExchange(ref _indexing, 1, 0) != 0)
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RebuildCoreAsync(folders, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or OperationCanceledException)
            {
                // A scan is best effort; the editor must never fail because a folder could not be read.
            }
            finally
            {
                Volatile.Write(ref _indexing, 0);
            }
        });
    }

    public async Task RebuildAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default)
    {
        string[] folders = NormalizeRoots(roots);
        if (folders.Length == 0)
        {
            return;
        }

        Interlocked.Exchange(ref _indexing, 1);
        try
        {
            await RebuildCoreAsync(folders, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _indexing, 0);
        }
    }

    public void IndexDocument(string filePath, string text)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string path = Normalize(filePath);
        IReadOnlyList<SqlIndexedSymbol> symbols = SqlSymbolExtractor.Extract(path, text ?? string.Empty);
        _documents[path] = new FileEntry(DateTime.MinValue, -1, symbols);
    }

    public void Invalidate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _documents.TryRemove(Normalize(filePath), out _);
    }

    public IReadOnlyList<SqlIndexedSymbol> Search(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        string value = query.Trim();
        return _documents.Values
            .SelectMany(file => file.Symbols)
            .Where(symbol => symbol.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => Rank(symbol.Kind))
            .ThenBy(symbol => symbol.Name.Length)
            .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<SqlIndexedSymbol> FindReferences(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        return _documents.Values
            .SelectMany(file => file.Symbols)
            .Where(symbol => string.Equals(symbol.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => symbol.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)
            .ToArray();
    }

    private async Task RebuildCoreAsync(string[] folders, CancellationToken cancellationToken)
    {
        foreach (string folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string file in EnumerateSqlFiles(folder, MaxFilesPerScan))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexFileAsync(file, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task IndexFileAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            FileInfo info = new(file);
            if (!info.Exists)
            {
                return;
            }

            string path = info.FullName;
            if (_documents.TryGetValue(path, out FileEntry existing)
                && existing.LastWriteUtc == info.LastWriteTimeUtc
                && existing.Length == info.Length)
            {
                return;
            }

            string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<SqlIndexedSymbol> symbols = SqlSymbolExtractor.Extract(path, text);
            _documents[path] = new FileEntry(info.LastWriteTimeUtc, info.Length, symbols);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            // Unreadable files are simply absent from the index.
        }
    }

    private static IEnumerable<string> EnumerateSqlFiles(string root, int limit)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        int seen = 0;
        while (pending.Count > 0 && seen < limit)
        {
            string folder = pending.Pop();
            string[] files;
            string[] folders;
            try
            {
                files = Directory.GetFiles(folder, "*.sql", SearchOption.TopDirectoryOnly);
                folders = Directory.GetDirectories(folder);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                if (seen++ >= limit)
                {
                    yield break;
                }

                yield return file;
            }

            foreach (string child in folders)
            {
                string name = Path.GetFileName(child);
                if (!ExcludedFolders.Contains(name))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static int Rank(SqlIndexedSymbolKind kind) => kind switch
    {
        SqlIndexedSymbolKind.Table => 0,
        SqlIndexedSymbolKind.Cte => 1,
        SqlIndexedSymbolKind.Alias => 2,
        _ => 3,
    };

    private static string[] NormalizeRoots(IEnumerable<string>? roots)
        => (roots ?? [])
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root!.Trim())
            .Where(Directory.Exists)
            .Select(Normalize)
            .Distinct(PathComparer)
            .ToArray();

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record FileEntry(DateTime LastWriteUtc, long Length, IReadOnlyList<SqlIndexedSymbol> Symbols);
}

/// <summary>Turns one SQL document into the identifiers the workspace index keeps.</summary>
public static class SqlSymbolExtractor
{
    private static readonly HashSet<NzToken> StatementStarts =
    [
        NzToken.With,
        NzToken.Select,
        NzToken.Insert,
        NzToken.Update,
        NzToken.Delete,
        NzToken.Merge,
        NzToken.Create,
        NzToken.Drop,
        NzToken.Alter,
        NzToken.Truncate,
        NzToken.Grant,
        NzToken.Revoke,
        NzToken.Explain,
        NzToken.Call,
        NzToken.Comment,
        NzToken.Reindex,
        NzToken.Copy,
    ];

    private static readonly HashSet<NzToken> TableIntroducers =
    [
        NzToken.From,
        NzToken.Join,
        NzToken.Into,
        NzToken.Update,
        NzToken.Table,
        NzToken.View,
        NzToken.Procedure,
        NzToken.Sequence,
        NzToken.Synonym,
        NzToken.Truncate,
        NzToken.Reindex,
        NzToken.Using,
    ];

    public static IReadOnlyList<SqlIndexedSymbol> Extract(string filePath, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return [];
        }

        Token<NzToken>[] tokens;
        try
        {
            tokens = NzLexer.Tokenize(sql).ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }

        if (tokens.Length == 0)
        {
            return [];
        }

        int[] lineStarts = BuildLineStarts(sql);
        var symbols = new List<SqlIndexedSymbol>();
        NzToken? previous = null;
        bool atStatementStart = true;
        bool inTableChain = false;
        bool justClassifiedTable = false;

        foreach (Token<NzToken> token in tokens)
        {
            if (token.Kind is NzToken.LineComment or NzToken.BlockComment)
            {
                continue;
            }

            if (token.Kind == NzToken.Semicolon)
            {
                atStatementStart = true;
                previous = null;
                inTableChain = false;
                justClassifiedTable = false;
                continue;
            }

            if (atStatementStart && StatementStarts.Contains(token.Kind))
            {
                Add(symbols, filePath, token, token.Kind.ToString().ToUpperInvariant(), SqlIndexedSymbolKind.Statement, lineStarts);
                atStatementStart = false;
                previous = token.Kind;
                justClassifiedTable = false;
                continue;
            }

            atStatementStart = false;

            if (token.Kind.IsIdentifierLike())
            {
                SqlIndexedSymbolKind? kind = Classify(previous, inTableChain, justClassifiedTable);
                if (kind is not null)
                {
                    Add(symbols, filePath, token, token.ToIdentifierText(), kind.Value, lineStarts);
                    inTableChain = kind == SqlIndexedSymbolKind.Table;
                    justClassifiedTable = kind == SqlIndexedSymbolKind.Table;
                }
                else
                {
                    inTableChain = false;
                    justClassifiedTable = false;
                }
            }
            else
            {
                inTableChain = inTableChain && token.Kind == NzToken.Dot;
                justClassifiedTable = false;
            }

            previous = token.Kind;
        }

        return symbols;
    }

    private static SqlIndexedSymbolKind? Classify(NzToken? previous, bool inTableChain, bool justClassifiedTable)
    {
        if (previous is null)
        {
            return null;
        }

        if (previous == NzToken.As)
        {
            return SqlIndexedSymbolKind.Alias;
        }

        if (previous is NzToken.With or NzToken.Recursive)
        {
            return SqlIndexedSymbolKind.Cte;
        }

        if (inTableChain && previous == NzToken.Dot)
        {
            return SqlIndexedSymbolKind.Table;
        }

        if (TableIntroducers.Contains(previous.Value))
        {
            return SqlIndexedSymbolKind.Table;
        }

        // "FROM customers c" and "FROM sales s": an identifier right after the table name is
        // its alias even without AS. Qualified chains are excluded above.
        if (justClassifiedTable && previous.Value.IsIdentifierLike())
        {
            return SqlIndexedSymbolKind.Alias;
        }

        return null;
    }

    private static void Add(
        List<SqlIndexedSymbol> symbols,
        string filePath,
        Token<NzToken> token,
        string name,
        SqlIndexedSymbolKind kind,
        int[] lineStarts)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        int offset = token.Span.Position.Absolute;
        (int line, int column) = Locate(offset, lineStarts);
        symbols.Add(new SqlIndexedSymbol(name, kind, filePath, line, column, offset));
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    private static (int Line, int Column) Locate(int offset, int[] lineStarts)
    {
        int index = Array.BinarySearch(lineStarts, offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, lineStarts.Length - 1);
        return (index + 1, offset - lineStarts[index] + 1);
    }
}

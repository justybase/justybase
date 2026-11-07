using System.Text;
using System.Text.RegularExpressions;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.Services.Schema;

/// <summary>How sure we are that a reference really points at the target. Ordered best first.</summary>
public enum SourceConfidence
{
    Parsed = 0,
    Likely = 1,
    Text = 2,
}

/// <summary>The object whose usages are asked for.</summary>
public sealed record SourceTarget(string Database, string Schema, string Name);

/// <summary>A definition that carries source text and can be searched locally.</summary>
public sealed record SourceDocument(
    string Database,
    string Schema,
    string Name,
    string? Signature,
    string ObjectType,
    string Text);

public sealed record SourceObjectRef(
    string Database,
    string Schema,
    string Name,
    string? Signature,
    string ObjectType);

public sealed record SourceUsageHit(
    SourceObjectRef Object,
    SourceConfidence Confidence,
    int References,
    int Line,
    string Snippet);

public sealed record SourceImpactNode(SourceUsageHit Hit, int Depth, int? Parent);

public sealed record SourceTextHit(SourceObjectRef Object, int Line, string Snippet, int Matches);

/// <summary>Definition search options mirroring the Rust source index.</summary>
public sealed class SourceTextQuery
{
    public string Term { get; init; } = "";
    public bool CaseSensitive { get; init; }
    public bool WholeWord { get; init; }
    public bool Regex { get; init; }
    public bool IgnoreComments { get; init; }
    /// <summary>Uppercase object types; empty means all.</summary>
    public IReadOnlyList<string> ObjectTypes { get; init; } = [];
    /// <summary>Case-insensitive schema allow-list; empty means all.</summary>
    public IReadOnlyList<string> Schemas { get; init; } = [];
    /// <summary>Maximum hits; zero or negative means unlimited.</summary>
    public int Limit { get; init; }
}

/// <summary>
/// Local analysis of object definitions: "used by", impact analysis and definition
/// search. The text of every procedure, view and external table of a connection is
/// read once and kept in memory. References are found with the SQL lexer, so a
/// mention inside a string or a comment is told apart from a real table reference:
/// <list type="bullet">
/// <item><see cref="SourceConfidence.Parsed"/>: a qualified name that resolves to the
/// target, or an unqualified one in a table position of the same schema;</item>
/// <item><see cref="SourceConfidence.Likely"/>: the name appears as an identifier but
/// could be another object;</item>
/// <item><see cref="SourceConfidence.Text"/>: the name appears only inside a string
/// literal or a comment, which includes dynamic SQL.</item>
/// </list>
/// Ported from <c>crates/app-core/src/source_index.rs</c>.
/// </summary>
public sealed class SourceIndex
{
    private const int MaxReferencesPerObject = 25;
    private const int SnippetChars = 160;
    private const int SnippetBefore = 60;

    private static readonly string[] RelationContext =
    [
        "FROM", "JOIN", "INTO", "UPDATE", "TABLE", "VIEW", "CALL", "EXEC",
        "EXECUTE", "USING", "REFERENCES", "TRUNCATE", "ONLY",
    ];

    private static readonly Regex FallbackWord = new(
        @"[_\p{L}][_\p{L}\p{N}$#]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, int[]> _byWord;
    private readonly IReadOnlyList<SourceDocument> _entries;
    private readonly SqlDialect _dialect;

    private SourceIndex(
        IReadOnlyList<SourceDocument> entries,
        Dictionary<string, int[]> byWord,
        IReadOnlyList<string> skippedDatabases,
        SqlDialect dialect)
    {
        _entries = entries;
        _byWord = byWord;
        SkippedDatabases = skippedDatabases;
        _dialect = dialect;
        BuiltAt = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset BuiltAt { get; }
    public int Count => _entries.Count;
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Catalogs whose definitions could not be read; their objects are missing from every answer.</summary>
    public IReadOnlyList<string> SkippedDatabases { get; }

    public IReadOnlyList<SourceDocument> Entries => _entries;

    public static SourceIndex Build(
        IReadOnlyList<SourceDocument> entries,
        SqlDialect dialect,
        IReadOnlyList<string>? skippedDatabases = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries
            .OrderBy(entry => entry.Database, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var byWord = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var position = 0; position < ordered.Length; position++)
        {
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectWords(ordered[position].Text, dialect, words);
            foreach (var word in words)
            {
                if (!byWord.TryGetValue(word, out var positions))
                {
                    positions = [];
                    byWord[word] = positions;
                }

                positions.Add(position);
            }
        }

        var frozen = byWord.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

        return new SourceIndex(
            ordered,
            frozen,
            skippedDatabases ?? [],
            dialect);
    }

    /// <summary>Objects whose definition references <paramref name="target"/>, best confidence first.</summary>
    public IReadOnlyList<SourceUsageHit> Usages(SourceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_byWord.TryGetValue(target.Name, out var candidates))
        {
            return [];
        }

        var hits = new List<SourceUsageHit>();
        foreach (var position in candidates)
        {
            var entry = _entries[position];
            if (Same(entry.Database, target.Database)
                && Same(entry.Schema, target.Schema)
                && Same(entry.Name, target.Name))
            {
                continue;
            }

            var references = ReferencesIn(entry, target);
            if (references.Count == 0)
            {
                continue;
            }

            var best = references
                .OrderBy(reference => reference.Confidence)
                .ThenBy(reference => reference.Line)
                .First();

            hits.Add(new SourceUsageHit(
                ObjectRef(entry),
                best.Confidence,
                references.Count,
                best.Line,
                best.Snippet));
        }

        hits.Sort(static (left, right) =>
        {
            var confidence = left.Confidence.CompareTo(right.Confidence);
            if (confidence != 0)
            {
                return confidence;
            }

            var type = string.Compare(left.Object.ObjectType, right.Object.ObjectType, StringComparison.OrdinalIgnoreCase);
            if (type != 0)
            {
                return type;
            }

            var database = string.Compare(left.Object.Database, right.Object.Database, StringComparison.OrdinalIgnoreCase);
            if (database != 0)
            {
                return database;
            }

            var schema = string.Compare(left.Object.Schema, right.Object.Schema, StringComparison.OrdinalIgnoreCase);
            return schema != 0
                ? schema
                : string.Compare(left.Object.Name, right.Object.Name, StringComparison.OrdinalIgnoreCase);
        });

        return hits;
    }

    /// <summary>
    /// What depends on <paramref name="root"/>, transitively, breadth first. Each object
    /// appears once (at its shallowest level), so cycles end. The flag reports that
    /// <paramref name="maxNodes"/> cut the result short.
    /// </summary>
    public (IReadOnlyList<SourceImpactNode> Nodes, bool Truncated) Impact(
        SourceTarget root,
        int maxDepth,
        int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(root);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Key(root.Database, root.Schema, root.Name),
        };
        var nodes = new List<SourceImpactNode>();
        var queue = new Queue<(SourceTarget Target, int? Parent, int Depth)>();
        queue.Enqueue((root, null, 0));
        var truncated = false;

        while (queue.Count > 0)
        {
            var (target, parent, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var hit in Usages(target))
            {
                var key = Key(hit.Object.Database, hit.Object.Schema, hit.Object.Name);
                if (!visited.Add(key))
                {
                    continue;
                }

                if (nodes.Count >= maxNodes)
                {
                    truncated = true;
                    return (nodes, truncated);
                }

                var next = new SourceTarget(hit.Object.Database, hit.Object.Schema, hit.Object.Name);
                nodes.Add(new SourceImpactNode(hit, depth + 1, parent));
                queue.Enqueue((next, nodes.Count - 1, depth + 1));
            }
        }

        return (nodes, truncated);
    }

    /// <summary>Definition search with the options the live (pushed-down) search cannot offer.</summary>
    public IReadOnlyList<SourceTextHit> Search(SourceTextQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Term.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        var pattern = query.Regex ? term : Regex.Escape(term);
        Regex matcher;
        try
        {
            var options = RegexOptions.CultureInvariant;
            if (!query.CaseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            matcher = new Regex(pattern, options, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regular expression: {ex.Message}", ex);
        }

        var wholeWord = query.WholeWord && !query.Regex;
        var hits = new List<SourceTextHit>();

        foreach (var entry in _entries)
        {
            if (query.ObjectTypes.Count > 0
                && !query.ObjectTypes.Any(type => Same(type, entry.ObjectType)))
            {
                continue;
            }

            if (query.Schemas.Count > 0
                && !query.Schemas.Any(schema => Same(schema, entry.Schema)))
            {
                continue;
            }

            var text = query.IgnoreComments ? MaskComments(entry.Text) : entry.Text;
            var starts = LineStarts(entry.Text);
            var first = -1;
            var matches = 0;

            foreach (Match found in matcher.Matches(text))
            {
                if (found.Length == 0)
                {
                    continue;
                }

                if (wholeWord && !HasWordBoundaries(text, found.Index, found.Length))
                {
                    continue;
                }

                matches++;
                if (first < 0)
                {
                    first = found.Index;
                }
            }

            if (first < 0)
            {
                continue;
            }

            var line = LineOf(starts, first);
            hits.Add(new SourceTextHit(
                ObjectRef(entry),
                line,
                LineSnippet(entry.Text, starts, line, first),
                matches));

            if (query.Limit > 0 && hits.Count >= query.Limit)
            {
                break;
            }
        }

        return hits;
    }

    private static void CollectWords(string text, SqlDialect dialect, HashSet<string> words)
    {
        var tokens = Tokenize(text, dialect);
        if (tokens.Length == 0)
        {
            foreach (Match match in FallbackWord.Matches(text))
            {
                words.Add(match.Value);
            }

            return;
        }

        foreach (var token in tokens)
        {
            if (token.Kind.IsIdentifierLike())
            {
                words.Add(token.ToIdentifierText());
                continue;
            }

            if (token.Kind == NzToken.StringLiteral)
            {
                foreach (Match match in FallbackWord.Matches(token.ToStringValue()))
                {
                    words.Add(match.Value);
                }
            }
        }

        foreach (var (start, length) in CommentSpans(text))
        {
            foreach (Match match in FallbackWord.Matches(text.Substring(start, length)))
            {
                words.Add(match.Value);
            }
        }
    }

    private IReadOnlyList<SourceReference> ReferencesIn(SourceDocument entry, SourceTarget target)
    {
        var tokens = Tokenize(entry.Text, _dialect);
        var starts = LineStarts(entry.Text);
        var references = new List<SourceReference>();

        void Push(int offset, SourceConfidence confidence)
        {
            if (references.Count >= MaxReferencesPerObject)
            {
                return;
            }

            var line = LineOf(starts, offset);
            references.Add(new SourceReference(
                line,
                LineSnippet(entry.Text, starts, line, offset),
                confidence));
        }

        if (tokens.Length == 0)
        {
            var offset = 0;
            while (references.Count < MaxReferencesPerObject)
            {
                var found = entry.Text.IndexOf(target.Name, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                if (HasWordBoundaries(entry.Text, found, target.Name.Length))
                {
                    Push(found, SourceConfidence.Likely);
                }

                offset = found + Math.Max(1, target.Name.Length);
            }

            return references;
        }

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Kind.IsIdentifierLike())
            {
                if (!Same(token.ToIdentifierText(), target.Name))
                {
                    continue;
                }

                var (database, schema, prefix) = Qualifiers(tokens, index);
                if (database is not null && !Same(database, target.Database))
                {
                    continue;
                }

                if (schema is not null && !Same(schema, target.Schema))
                {
                    continue;
                }

                // An unqualified name resolves in the definition's own database.
                if (database is null && !Same(entry.Database, target.Database))
                {
                    continue;
                }

                var beforeIndex = index - prefix - 1;
                var relationContext = beforeIndex >= 0
                    && beforeIndex < tokens.Length
                    && RelationContext.Any(keyword => TokenWordEquals(tokens[beforeIndex], keyword));
                var call = index + 1 < tokens.Length && tokens[index + 1].Kind == NzToken.LParen;
                // `name.column` / `alias.column`: the name is used as a qualifier.
                var usedAsQualifier = prefix == 0 && IsDot(tokens, index + 1);
                var qualified = database is not null || schema is not null;
                var confident = !usedAsQualifier
                    && (qualified || ((relationContext || call) && Same(entry.Schema, target.Schema)));

                Push(
                    token.Span.Position.Absolute,
                    confident ? SourceConfidence.Parsed : SourceConfidence.Likely);
                continue;
            }

            if (token.Kind == NzToken.StringLiteral)
            {
                PushTextMatches(token.ToStringValue(), token.Span.Position.Absolute, target.Name, Push);
            }
        }

        foreach (var (start, length) in CommentSpans(entry.Text))
        {
            PushTextMatches(entry.Text.Substring(start, length), start, target.Name, Push);
        }

        return references;
    }

    private static void PushTextMatches(
        string text,
        int absoluteOffset,
        string name,
        Action<int, SourceConfidence> push)
    {
        if (name.Length == 0)
        {
            return;
        }

        var from = 0;
        while (from <= text.Length - name.Length)
        {
            var found = text.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return;
            }

            if (HasWordBoundaries(text, found, name.Length))
            {
                push(
                    absoluteOffset + found,
                    SourceConfidence.Text);
                return;
            }

            from = found + name.Length;
        }
    }

    private static (string? Database, string? Schema, int Prefix) Qualifiers(
        IReadOnlyList<Token<NzToken>> tokens,
        int index)
    {
        if (index == 0 || !IsDot(tokens, index - 1))
        {
            return (null, null, 0);
        }

        if (index >= 2 && IsDot(tokens, index - 2))
        {
            // db..name
            var database = index >= 3 ? IdentifierAt(tokens, index - 3) : null;
            return (database, null, database is null ? 2 : 3);
        }

        var schema = index >= 2 ? IdentifierAt(tokens, index - 2) : null;
        if (schema is null)
        {
            return (null, null, 1);
        }

        if (index >= 4 && IsDot(tokens, index - 3))
        {
            var database = IdentifierAt(tokens, index - 4);
            if (database is not null)
            {
                return (database, schema, 4);
            }
        }

        return (null, schema, 2);
    }

    private static bool IsDot(IReadOnlyList<Token<NzToken>> tokens, int index)
        => index >= 0 && index < tokens.Count && tokens[index].Kind == NzToken.Dot;

    private static string? IdentifierAt(IReadOnlyList<Token<NzToken>> tokens, int index)
        => index >= 0 && index < tokens.Count && tokens[index].Kind.IsIdentifierLike()
            ? tokens[index].ToIdentifierText()
            : null;

    private static bool TokenWordEquals(Token<NzToken> token, string word)
        => token.ToStringValue().Equals(word, StringComparison.OrdinalIgnoreCase);

    private static Token<NzToken>[] Tokenize(string text, SqlDialect dialect)
    {
        try
        {
            return DialectRuntime.Tokenize(text, dialect).ToArray();
        }
        catch (Exception ex) when (ex is Superpower.ParseException or InvalidOperationException or ArgumentException)
        {
            // A definition may carry syntax the tokenizer rejects (or be a fragment);
            // fall back to a word scan instead of hiding the object from the index.
            return [];
        }
    }

    private static string MaskComments(string text)
    {
        var buffer = new StringBuilder(text);
        foreach (var (start, length) in CommentSpans(text))
        {
            for (var index = start; index < start + length; index++)
            {
                if (buffer[index] is not ('\r' or '\n'))
                {
                    buffer[index] = ' ';
                }
            }
        }

        return buffer.ToString();
    }

    private static List<(int Start, int Length)> CommentSpans(string text)
    {
        var spans = new List<(int Start, int Length)>();
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '\'')
            {
                index = SkipQuoted(text, index, '\'');
                continue;
            }

            if (current == '"')
            {
                index = SkipQuoted(text, index, '"');
                continue;
            }

            if (current == '-' && index + 1 < text.Length && text[index + 1] == '-')
            {
                var start = index;
                index += 2;
                while (index < text.Length && text[index] is not ('\r' or '\n'))
                {
                    index++;
                }

                spans.Add((start, index - start));
                continue;
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var start = index;
                index += 2;
                while (index < text.Length)
                {
                    if (text[index] == '*' && index + 1 < text.Length && text[index + 1] == '/')
                    {
                        index += 2;
                        break;
                    }

                    index++;
                }

                spans.Add((start, index - start));
                continue;
            }

            index++;
        }

        return spans;
    }

    private static int SkipQuoted(string text, int index, char quote)
    {
        index++;
        while (index < text.Length)
        {
            if (text[index] != quote)
            {
                index++;
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return index;
    }

    private static bool HasWordBoundaries(string text, int start, int length)
    {
        var before = start == 0 || !IsWordChar(text[start - 1]);
        var after = start + length >= text.Length || !IsWordChar(text[start + length]);
        return before && after;
    }

    private static bool IsWordChar(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '$' or '#';

    private static bool Same(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Key(string database, string schema, string name)
        => string.Concat(database, "\u001f", schema, "\u001f", name);

    private static SourceObjectRef ObjectRef(SourceDocument entry)
        => new(entry.Database, entry.Schema, entry.Name, entry.Signature, entry.ObjectType);

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }

    private static int LineOf(int[] starts, int offset)
    {
        var low = 0;
        var high = starts.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (starts[middle] <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// The line around <paramref name="offset"/>, trimmed; a long line (a one-line view)
    /// is cut to a window around the match.
    /// </summary>
    private static string LineSnippet(string text, int[] starts, int line, int offset)
    {
        var start = line >= 1 && line <= starts.Length ? starts[line - 1] : 0;
        var end = line < starts.Length ? starts[line] : text.Length;
        var raw = text[start..end];
        var lead = raw.Length - raw.TrimStart().Length;
        var trimmed = raw.Trim();
        var matchStart = Math.Clamp(offset, start + lead, end);
        var column = Math.Max(0, matchStart - (start + lead));
        var skip = Math.Max(0, column - SnippetBefore);

        var builder = new StringBuilder(trimmed.Length + 2);
        if (skip > 0)
        {
            builder.Append('…');
        }

        var count = 0;
        foreach (var character in trimmed.Skip(skip))
        {
            if (count == SnippetChars)
            {
                builder.Append('…');
                break;
            }

            builder.Append(character);
            count++;
        }

        return builder.ToString();
    }

    private sealed record SourceReference(int Line, string Snippet, SourceConfidence Confidence);
}

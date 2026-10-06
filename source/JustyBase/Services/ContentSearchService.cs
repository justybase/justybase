using System.Text;
using System.Text.RegularExpressions;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

public sealed record ContentSearchOptions(
    string Pattern,
    bool MatchCase = false,
    bool WholeWord = false,
    bool UseRegex = false,
    bool SearchSqlComments = true,
    string Include = "",
    string Exclude = "");

public sealed record ContentSearchHit(int Line, int Column, int Offset, int Length,
    string Before, string Match, string After);

public sealed record ContentSearchFile(string Path, string RelativePath, string Text,
    byte[] OriginalBytes, Encoding Encoding, bool HasBom, IReadOnlyList<ContentSearchHit> Hits);

public sealed record ContentReplacementChange(int Line, string Before, string After);
public sealed record ContentReplacement(ContentSearchFile File, string NewText, int Count,
    IReadOnlyList<ContentReplacementChange> Changes);
public sealed record ContentSearchRun(IReadOnlyList<ContentSearchFile> Files, IReadOnlyList<string> Skipped);

public interface IContentSearchService
{
    Task<ContentSearchRun> SearchAsync(IEnumerable<string> roots,
        ContentSearchOptions options, CancellationToken cancellationToken);
    ContentReplacement? Preview(ContentSearchFile file, ContentSearchOptions options, string replacement,
        IReadOnlyCollection<int>? selectedOffsets = null);
    bool IsUnchanged(ContentSearchFile file);
    void Apply(ContentReplacement replacement);
}

public sealed class ContentSearchService : IContentSearchService
{
    public const int MaxFileSize = 10 * 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public async Task<ContentSearchRun> SearchAsync(IEnumerable<string> roots,
        ContentSearchOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.Pattern)) return new([], []);
        var matcher = CreateMatcher(options);
        var results = new List<ContentSearchFile>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                skipped.Add($"{root}: directory not found");
                continue;
            }
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = pending.Pop();
                string[] directories;
                string[] files;
                try
                {
                    directories = Directory.GetDirectories(folder);
                    files = Directory.GetFiles(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add($"{folder}: {ex.Message}");
                    continue;
                }
                foreach (var directory in directories)
                    if (!Path.GetFileName(directory).StartsWith('.')) pending.Push(directory);
                foreach (var path in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ISomeEditorOptions.REGISTERED_EXTENSIONS.ContainsKey(Path.GetExtension(path))
                        || !IsIncluded(path, root, options) || !seen.Add(path)) continue;
                    try
                    {
                        var info = new FileInfo(path);
                        if (info.Length > MaxFileSize)
                        {
                            skipped.Add($"{path}: over 10 MB");
                            continue;
                        }
                        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                        if (bytes.AsSpan().IndexOf((byte)0) >= 0 &&
                            !bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) &&
                            !bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }) &&
                            !bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
                        {
                            skipped.Add($"{path}: binary content");
                            continue;
                        }
                        var (encoding, bomLength) = DetectEncoding(bytes);
                        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
                        var searchable = options.SearchSqlComments || !path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                            ? text : MaskSqlComments(text);
                        var hits = FindHits(text, searchable, matcher);
                        if (hits.Count > 0)
                            results.Add(new ContentSearchFile(path, Path.GetRelativePath(root, path), text,
                                bytes, encoding, bomLength > 0, hits));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                    {
                        skipped.Add($"{path}: {ex.Message}");
                    }
                }
            }
        }
        return new(results.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), skipped);
    }

    public ContentReplacement? Preview(ContentSearchFile file, ContentSearchOptions options,
        string replacement, IReadOnlyCollection<int>? selectedOffsets = null)
    {
        var matcher = CreateMatcher(options);
        var allowed = selectedOffsets is null ? null : new HashSet<int>(selectedOffsets);
        var result = new StringBuilder(file.Text.Length);
        var changes = new List<ContentReplacementChange>();
        var cursor = 0;
        var count = 0;
        foreach (var hit in file.Hits)
        {
            if (hit.Offset < cursor || (allowed is not null && !allowed.Contains(hit.Offset))) continue;
            var lineStart = file.Text.LastIndexOf('\n', Math.Max(0, hit.Offset - 1)) + 1;
            var lineEnd = file.Text.IndexOf('\n', hit.Offset);
            if (lineEnd < 0) lineEnd = file.Text.Length;
            if (lineEnd > lineStart && file.Text[lineEnd - 1] == '\r') lineEnd--;
            var line = file.Text.Substring(lineStart, lineEnd - lineStart);
            var match = matcher.Match(line, hit.Column - 1);
            if (!match.Success || match.Index != hit.Column - 1 || match.Length != hit.Length) continue;
            var inserted = options.UseRegex ? match.Result(replacement) : replacement;
            var beforeContextStart = Math.Max(0, match.Index - 60);
            var beforeContextEnd = Math.Min(line.Length, match.Index + match.Length + 90);
            var afterLine = string.Concat(line.AsSpan(0, match.Index), inserted,
                line.AsSpan(match.Index + match.Length));
            var afterContextEnd = Math.Min(afterLine.Length, match.Index + inserted.Length + 90);
            var beforeSnippet = (beforeContextStart > 0 ? "…" : "") +
                line[beforeContextStart..beforeContextEnd] + (beforeContextEnd < line.Length ? "…" : "");
            var afterSnippet = (beforeContextStart > 0 ? "…" : "") +
                afterLine[beforeContextStart..afterContextEnd] + (afterContextEnd < afterLine.Length ? "…" : "");
            changes.Add(new ContentReplacementChange(hit.Line, beforeSnippet, afterSnippet));
            result.Append(file.Text, cursor, hit.Offset - cursor);
            result.Append(inserted);
            cursor = hit.Offset + hit.Length;
            count++;
        }
        if (count == 0) return null;
        result.Append(file.Text, cursor, file.Text.Length - cursor);
        return new ContentReplacement(file, result.ToString(), count, changes);
    }

    public bool IsUnchanged(ContentSearchFile file)
    {
        try { return File.ReadAllBytes(file.Path).AsSpan().SequenceEqual(file.OriginalBytes); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Apply(ContentReplacement replacement)
    {
        var file = replacement.File;
        if (!IsUnchanged(file)) throw new IOException("File changed since the search. Search again before replacing.");
        var preamble = file.HasBom ? file.Encoding.GetPreamble() : [];
        var content = file.Encoding.GetBytes(replacement.NewText);
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        var tempPath = Path.Combine(Path.GetDirectoryName(file.Path)!, $".{Path.GetFileName(file.Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, file.Path, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static Regex CreateMatcher(ContentSearchOptions options)
    {
        var pattern = options.UseRegex ? options.Pattern : Regex.Escape(options.Pattern);
        if (options.WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        return new Regex(pattern, (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) |
            RegexOptions.CultureInvariant, RegexTimeout);
    }

    private static List<ContentSearchHit> FindHits(string text, string searchable, Regex matcher)
    {
        var hits = new List<ContentSearchHit>();
        var offset = 0;
        var lineNumber = 1;
        while (offset < text.Length)
        {
            var end = text.IndexOf('\n', offset);
            if (end < 0) end = text.Length;
            var length = end - offset;
            if (length > 0 && text[end - 1] == '\r') length--;
            var line = searchable.Substring(offset, length);
            foreach (Match match in matcher.Matches(line))
            {
                if (match.Length == 0) continue;
                if (line.AsSpan(match.Index, match.Length).IndexOf('\u0001') >= 0) continue;
                var original = text.AsSpan(offset, length);
                var start = Math.Max(0, match.Index - 45);
                var afterEnd = Math.Min(length, match.Index + match.Length + 65);
                hits.Add(new ContentSearchHit(lineNumber, match.Index + 1, offset + match.Index,
                    match.Length, (start > 0 ? "…" : "") + original[start..match.Index].ToString(),
                    original.Slice(match.Index, match.Length).ToString(),
                    original[(match.Index + match.Length)..afterEnd].ToString() + (afterEnd < length ? "…" : "")));
            }
            offset = end + 1;
            lineNumber++;
        }
        return hits;
    }

    private static bool IsIncluded(string path, string root, ContentSearchOptions options)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var includes = SplitGlobs(options.Include);
        var excludes = SplitGlobs(options.Exclude);
        return (includes.Length == 0 || includes.Any(g => GlobMatches(g, relative))) &&
            !excludes.Any(g => GlobMatches(g, relative));
    }

    private static string[] SplitGlobs(string value) => value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool GlobMatches(string glob, string path)
    {
        glob = glob.Replace('\\', '/');
        var pattern = Regex.Escape(glob).Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        if (!glob.Contains('/')) pattern = $"(?:^|.*/){pattern}";
        return Regex.IsMatch(path, $"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }

    private static (Encoding Encoding, int BomLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(true, true), 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) return (new UTF32Encoding(false, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return (new UTF32Encoding(true, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, true, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, true, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }

    private static string MaskSqlComments(string text)
    {
        var chars = text.ToCharArray();
        var quote = '\0';
        var line = false;
        var block = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';
            if (line)
            {
                if (chars[i] == '\n') line = false;
                else if (chars[i] != '\r') chars[i] = '\u0001';
            }
            else if (block)
            {
                if (chars[i] == '*' && next == '/') { chars[i] = chars[++i] = '\u0001'; block = false; }
                else if (chars[i] is not ('\r' or '\n')) chars[i] = '\u0001';
            }
            else if (quote != '\0')
            {
                if (chars[i] == quote && next == quote) i++;
                else if (chars[i] == quote) quote = '\0';
            }
            else if (chars[i] is '\'' or '"') quote = chars[i];
            else if (chars[i] == '-' && next == '-') { chars[i] = chars[++i] = '\u0001'; line = true; }
            else if (chars[i] == '/' && next == '*') { chars[i] = chars[++i] = '\u0001'; block = true; }
        }
        return new string(chars);
    }
}

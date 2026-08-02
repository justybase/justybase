using JustyBase.PluginCommons;

namespace JustyBase.Editor.CompletionProviders;

/// <summary>
/// Provides completion data for import/export options like encoding, delimiters, compression, etc.
/// </summary>
public static class ImportExportCompletionHelper
{
    private static ICompletionDataEx[]? _encodingListCache;
    private static readonly object _encodingLock = new();

    private static readonly ICompletionDataEx[] _encodingList =
    [
        new CompletionDataSql("ASCII", "An encoding for the ASCII (7-bit) character set.", false, Glyph.None, null),
        new CompletionDataSql("UTF8", "An encoding for the UTF-8 format.", false, Glyph.None, null),
        new CompletionDataSql("UTF8_BM", "An encoding for the UTF-8 format - without BOM", false, Glyph.None, null),
        new CompletionDataSql("UTF16", "An encoding object for the UTF-16 format", false, Glyph.None, null),
        new CompletionDataSql("UTF32", "An encoding object for the UTF-32 format using the little endian byte order.", false, Glyph.None, null),
        new CompletionDataSql("Unicode", "An encoding for the UTF-16 format using the little endian byte order.", false, Glyph.None, null),
        new CompletionDataSql("BigEndianUnicode", "An encoding object for the UTF-16 format that uses the big endian byte order.", false, Glyph.None, null),
        new CompletionDataSql("Latin1", "An encoding for the Latin1 character set (ISO-8859-1).", false, Glyph.None, null),
        new CompletionDataSql("Default", "Default encoding for this .NET implementation", false, Glyph.None, null)
    ];

    private static readonly ICompletionDataEx[] _rowDelimiterList =
    [
        new CompletionDataSql("windows", @"\r\n as row delimiter", false, Glyph.None, null),
        new CompletionDataSql("unix", @"\n as row delimiter", false, Glyph.None, null)
    ];

    private static readonly ICompletionDataEx[] _headerList =
    [
        new CompletionDataSql("true", "use header", false, Glyph.None, null),
        new CompletionDataSql("false", "do not use header", false, Glyph.None, null)
    ];

    private static readonly ICompletionDataEx[] _columnDelimiter =
    [
        new CompletionDataSql("';'", "';' as column delimiter", false, Glyph.None, null),
        new CompletionDataSql("|", "'|' as column delimiter", false, Glyph.None, null),
        new CompletionDataSql(",", "',' as column delimiter", false, Glyph.None, null),
        new CompletionDataSql("#", "'#' as column delimiter", false, Glyph.None, null)
    ];

    private static readonly ICompletionDataEx[] _compressionList =
    [
        new CompletionDataSql("none", "no compression", false, Glyph.None, null),
        new CompletionDataSql("zip", "zip compression", false, Glyph.None, null),
        new CompletionDataSql("gzip", "gzip compression", false, Glyph.None, null),
        new CompletionDataSql("brotli", "brotli compression", false, Glyph.None, null),
        new CompletionDataSql("zstd", "zstd compression", false, Glyph.None, null),
        new CompletionDataSql("lz4", "lz4 compression", false, Glyph.None, null)
    ];

    private static readonly ICompletionDataEx[] _upFrontRowsCountList =
    [
        new CompletionDataSql("true", "determine rows count before export ON", false, Glyph.None, null),
        new CompletionDataSql("false","determine rows count before export OFF", false, Glyph.None, null)
    ];

    /// <summary>
    /// Gets completion data for #encoding directive
    /// </summary>
    public static ICompletionDataEx[] GetEncodingCompletions()
    {
        if (_encodingListCache is not null)
        {
            return _encodingListCache;
        }

        lock (_encodingLock)
        {
            if (_encodingListCache is not null)
            {
                return _encodingListCache;
            }

            var enc = System.Text.Encoding.GetEncodings();
            var result = new ICompletionDataEx[enc.Length + _encodingList.Length];

            for (int i = 0; i < _encodingList.Length; i++)
            {
                result[i] = _encodingList[i];
            }

            for (int i = _encodingList.Length; i < enc.Length + _encodingList.Length; i++)
            {
                var currentEnc = enc[i - _encodingList.Length];
                result[i] = new CompletionDataSql(currentEnc.Name, currentEnc.DisplayName, false, Glyph.None, null);
            }

            _encodingListCache = result;
            return result;
        }
    }

    /// <summary>
    /// Gets completion data for #LineDelimiter directive
    /// </summary>
    public static ICompletionDataEx[] GetRowDelimiterCompletions() => _rowDelimiterList;

    /// <summary>
    /// Gets completion data for #header directive
    /// </summary>
    public static ICompletionDataEx[] GetHeaderCompletions() => _headerList;

    /// <summary>
    /// Gets completion data for #delimiter directive
    /// </summary>
    public static ICompletionDataEx[] GetColumnDelimiterCompletions() => _columnDelimiter;

    /// <summary>
    /// Gets completion data for #compression directive
    /// </summary>
    public static ICompletionDataEx[] GetCompressionCompletions() => _compressionList;

    /// <summary>
    /// Gets completion data for #upFrontRowsCount directive
    /// </summary>
    public static ICompletionDataEx[] GetUpFrontRowsCountCompletions() => _upFrontRowsCountList;

    /// <summary>
    /// Checks if the given word is an import/export directive
    /// </summary>
    public static bool IsImportExportDirective(string word)
    {
        return word.Equals("#encoding ", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("#LineDelimiter ", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("#header ", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("#delimiter ", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("#compression ", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("#upFrontRowsCount ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets completion result for import/export directive if applicable
    /// </summary>
    public static CompletionResult? GetCompletionForDirective(string word)
    {
        if (word.Equals("#encoding ", StringComparison.OrdinalIgnoreCase))
        {
            return new CompletionResult(GetEncodingCompletions(), null, true);
        }
        else if (word.Equals("#LineDelimiter ", StringComparison.OrdinalIgnoreCase))
        {
            return new CompletionResult(GetRowDelimiterCompletions(), null, true);
        }
        else if (word.Equals("#header ", StringComparison.OrdinalIgnoreCase))
        {
            return new CompletionResult(GetHeaderCompletions(), null, true);
        }
        else if (word.Equals("#delimiter ", StringComparison.OrdinalIgnoreCase))
        {
            return new CompletionResult(GetColumnDelimiterCompletions(), null, true);
        }
        else if (word.Equals("#compression ", StringComparison.OrdinalIgnoreCase))
        {
            return new CompletionResult(GetCompressionCompletions(), null, true);
        }
        else if (word.Equals("#upFrontRowsCount ", StringComparison.OrdinalIgnoreCase))
        {
            return new CompletionResult(GetUpFrontRowsCountCompletions(), null, true);
        }

        return null;
    }
}

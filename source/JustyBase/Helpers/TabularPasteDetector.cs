using System.Text;
using System.Text.RegularExpressions;

namespace JustyBase.Helpers;

/// <summary>Result of the clipboard heuristic used before the editor accepts a paste.</summary>
public readonly record struct TabularPasteInfo(int Lines, int Columns);

/// <summary>User decision for clipboard text that looks like a spreadsheet block.</summary>
public enum TabularPasteChoice
{
    /// <summary>Do not paste anything (the default when the prompt is dismissed).</summary>
    Cancel = 0,

    /// <summary>Open the import wizard seeded with the pasted rows.</summary>
    ImportData = 1,

    /// <summary>Insert the clipboard text into the SQL editor.</summary>
    PasteAsText = 2
}

/// <summary>
/// Detects spreadsheet-style clipboard data (several non-empty lines with a consistent
/// number of tab-separated fields) and converts it to a CSV file the import wizard can read.
/// </summary>
public static partial class TabularPasteDetector
{
    [GeneratedRegex(
        @"^\s*(SELECT|WITH|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|MERGE|GRANT|REVOKE|CALL|EXECUTE|DECLARE|BEGIN|END|COMMENT)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlLineStart();

    /// <summary>
    /// Detects spreadsheet-style clipboard data. SQL scripts usually mix indentation and
    /// punctuation, so they fail the consistent-tab check.
    /// </summary>
    public static TabularPasteInfo? Detect(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] lines = NormalizeLines(text)
            .Select(static line => line.TrimEnd())
            .Where(static line => line.Length > 0)
            .ToArray();

        if (lines.Length < 3)
        {
            return null;
        }

        if (lines.Any(static line => SqlLineStart().IsMatch(line)))
        {
            return null;
        }

        int columns = 0;
        foreach (string line in lines)
        {
            if (!line.Contains('\t'))
            {
                return null;
            }

            string[] fields = line.Split('\t');
            // Trailing tabs are common in Excel; they still describe the same table.
            int end = fields.Length;
            while (end > 0 && fields[end - 1].Length == 0)
            {
                end--;
            }

            if (end < 2)
            {
                return null;
            }

            if (columns == 0)
            {
                columns = end;
                continue;
            }

            // Allow the row to have fewer filled fields, but not a different table width.
            if (end > columns)
            {
                return null;
            }
        }

        if (columns < 2)
        {
            return null;
        }

        return new TabularPasteInfo(Columns: columns, Lines: lines.Length);
    }

    public static string Describe(TabularPasteInfo info)
    {
        string lineLabel = info.Lines == 1 ? "line" : "lines";
        string columnLabel = info.Columns == 1 ? "column" : "columns";
        return $"{info.Lines} tab-separated {lineLabel} with {info.Columns} {columnLabel}";
    }

    /// <summary>
    /// Converts a tab-separated clipboard block into comma-separated CSV text so the
    /// file-based import wizard reads the same rows the user copied from the spreadsheet.
    /// </summary>
    public static string ToCsv(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var rows = new List<string[]>();
        int width = 0;
        foreach (string rawLine in NormalizeLines(text))
        {
            if (rawLine.Length == 0)
            {
                continue;
            }

            string[] fields = rawLine.Split('\t');
            int end = fields.Length;
            while (end > 0 && fields[end - 1].Length == 0)
            {
                end--;
            }

            if (end == 0)
            {
                continue;
            }

            var row = new string[end];
            for (int i = 0; i < end; i++)
            {
                row[i] = UnquoteTsvField(fields[i]);
            }

            rows.Add(row);
            width = Math.Max(width, end);
        }

        if (rows.Count == 0 || width == 0)
        {
            return string.Empty;
        }

        var csv = new StringBuilder(text.Length + 16);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rowIndex > 0)
            {
                csv.Append('\n');
            }

            string[] row = rows[rowIndex];
            for (int column = 0; column < width; column++)
            {
                if (column > 0)
                {
                    csv.Append(',');
                }

                AppendCsvField(csv, column < row.Length ? row[column] : string.Empty);
            }
        }

        return csv.ToString();
    }

    private static string[] NormalizeLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string UnquoteTsvField(string field)
    {
        if (field.Length >= 2 && field[0] == '"' && field[^1] == '"')
        {
            return field[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return field;
    }

    private static void AppendCsvField(StringBuilder csv, string field)
    {
        if (field.Length == 0)
        {
            return;
        }

        bool needsQuotes = field.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        if (!needsQuotes)
        {
            csv.Append(field);
            return;
        }

        csv.Append('"');
        foreach (char c in field)
        {
            if (c == '"')
            {
                csv.Append('"');
            }

            csv.Append(c);
        }

        csv.Append('"');
    }
}

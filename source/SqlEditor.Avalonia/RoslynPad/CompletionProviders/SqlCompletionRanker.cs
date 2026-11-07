namespace JustyBase.Editor.CompletionProviders;

/// <summary>
/// VS Code-like fuzzy ranking for SQL completion (port of justybase-vscode completionRanker).
/// Pure logic, no Avalonia dependencies. Order: prefix → compact → acronym → subsequence.
/// </summary>
public static class SqlCompletionRanker
{
    public const int NoMatchRank = int.MaxValue;

    public static int GetMatchRank(string? prefix, string? label)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(label))
        {
            return NoMatchRank;
        }

        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var compactLabel = RemoveUnderscores(label);
        var compactPrefix = RemoveUnderscores(prefix);
        if (compactLabel.StartsWith(compactPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (IsAcronymMatch(prefix, label))
        {
            return 2;
        }

        if (label.Contains(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (IsSubsequence(prefix, label))
        {
            return 4;
        }

        return NoMatchRank;
    }

    public static int GetKindOrder(Glyph glyph) => glyph switch
    {
        Glyph.Column => 0,
        Glyph.Table => 1,
        Glyph.View => 1,
        Glyph.WithDb => 1,
        Glyph.TempTable => 1,
        Glyph.SubQuery => 1,
        Glyph.Function => 2,
        Glyph.Procedure => 2,
        Glyph.Schema => 3,
        Glyph.Database => 3,
        Glyph.Synonym => 3,
        Glyph.ExternalTable => 1,
        Glyph.Snippet => 5,
        Glyph.None => 4,
        Glyph.CompletionWarning => 6,
        Glyph.BetweenSelectAndFrom => 6,
        _ => 6,
    };

    public static string BuildSortText(Glyph glyph, string label)
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:D2}_{1}",
            GetKindOrder(glyph),
            (label ?? string.Empty).ToUpperInvariant());

    /// <summary>
    /// Deduplicates by label + glyph (case-insensitive) and orders best match first.
    /// </summary>
    /// <remarks>
    /// AvaloniaEdit's <c>CompletionList</c> does not sort by
    /// <see cref="ICompletionData.Priority"/> (no collection-view sorting in the
    /// 12.0 assembly), so the returned order is the order the user sees.
    /// <see cref="ICompletionDataEx.SortText"/> is produced for consumers that do
    /// sort, and for stable diffs in tests.
    /// </remarks>
    public static IList<ICompletionDataEx> SortAndDeduplicate(
        IEnumerable<ICompletionDataEx> items,
        string? prefix)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ICompletionDataEx>();
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var glyphPart = item is CompletionDataSql sql
                ? ((int)sql.Glyph).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "?";
            var key = item.Text + "\u0001" + glyphPart;
            if (seen.Add(key))
            {
                deduped.Add(item);
            }
        }

        var normalizedPrefix = (prefix ?? string.Empty).Trim();
        var dotIndex = normalizedPrefix.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            normalizedPrefix = normalizedPrefix[(dotIndex + 1)..];
        }

        return deduped
            .OrderBy(item => GetMatchRank(normalizedPrefix, item.Text))
            .ThenBy(item => item.SortText, StringComparer.Ordinal)
            .ThenBy(item => item.Text.Length)
            .ToList();
    }

    private static string RemoveUnderscores(string value)
    {
        if (value.IndexOf('_') < 0)
        {
            return value;
        }

        return string.Concat(value.Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsAcronymMatch(string prefix, string label)
    {
        var acronym = GetAcronym(label);
        if (acronym.Length < prefix.Length)
        {
            return false;
        }

        return acronym.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAcronym(string label)
    {
        var parts = label.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            var sb = new System.Text.StringBuilder(parts.Length);
            foreach (var part in parts)
            {
                if (part.Length > 0)
                {
                    sb.Append(char.ToUpperInvariant(part[0]));
                }
            }

            return sb.ToString();
        }

        var acronymBuilder = new System.Text.StringBuilder(label.Length);
        acronymBuilder.Append(char.ToUpperInvariant(label[0]));
        for (var i = 1; i < label.Length; i++)
        {
            if (char.IsUpper(label[i]) && !char.IsUpper(label[i - 1]))
            {
                acronymBuilder.Append(char.ToUpperInvariant(label[i]));
            }
        }

        return acronymBuilder.ToString();
    }

    private static bool IsSubsequence(string prefix, string label)
    {
        var pi = 0;
        for (var li = 0; li < label.Length && pi < prefix.Length; li++)
        {
            if (char.ToUpperInvariant(label[li]) == char.ToUpperInvariant(prefix[pi]))
            {
                pi++;
            }
        }

        return pi == prefix.Length;
    }
}

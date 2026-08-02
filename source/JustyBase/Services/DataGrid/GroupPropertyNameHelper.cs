using JustyBase.Models;
using System.Text.RegularExpressions;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Helper for DataGrid grouping property names ("Fields[index]").
/// Keeps parsing and formatting logic in one AOT-friendly place.
/// </summary>
public static partial class GroupPropertyNameHelper
{
    public static string CreatePropertyName(int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        return $"{nameof(TableRow.Fields)}[{columnIndex}]";
    }

    public static bool TryExtractColumnIndex(string? propertyName, out int columnIndex)
    {
        columnIndex = -1;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var match = GroupPropertyNameRegex().Match(propertyName);
        return match.Success && int.TryParse(match.Groups["num"].Value, out columnIndex);
    }

    [GeneratedRegex(@"^Fields\[(?<num>\d+)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupPropertyNameRegex();
}

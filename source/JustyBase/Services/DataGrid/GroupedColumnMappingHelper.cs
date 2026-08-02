namespace JustyBase.Services.DataGrid;

/// <summary>
/// Maps grouped DataGrid property names to column names and resolves reorder indices.
/// </summary>
public static class GroupedColumnMappingHelper
{
    public static bool TryFindMoveIndexes(
        IReadOnlyList<string> groupedPropertyNames,
        IReadOnlyList<string> headers,
        string sourceColumnName,
        string targetColumnName,
        out int sourceIndex,
        out int targetIndex)
    {
        sourceIndex = -1;
        targetIndex = -1;

        for (int i = 0; i < groupedPropertyNames.Count; i++)
        {
            if (!GroupPropertyNameHelper.TryExtractColumnIndex(groupedPropertyNames[i], out int columnIndex))
            {
                continue;
            }

            if ((uint)columnIndex >= (uint)headers.Count)
            {
                continue;
            }

            string columnName = headers[columnIndex];
            if (string.Equals(columnName, sourceColumnName, StringComparison.Ordinal))
            {
                sourceIndex = i;
            }

            if (string.Equals(columnName, targetColumnName, StringComparison.Ordinal))
            {
                targetIndex = i;
            }
        }

        return sourceIndex >= 0 && targetIndex >= 0;
    }

    public static List<string> ToGroupedColumnNames(
        IReadOnlyList<string> groupedPropertyNames,
        IReadOnlyList<string> headers)
    {
        var groupedColumns = new List<string>(groupedPropertyNames.Count);
        foreach (var propertyName in groupedPropertyNames)
        {
            if (!GroupPropertyNameHelper.TryExtractColumnIndex(propertyName, out int columnIndex))
            {
                continue;
            }

            if ((uint)columnIndex >= (uint)headers.Count)
            {
                continue;
            }

            groupedColumns.Add(headers[columnIndex]);
        }

        return groupedColumns;
    }
}

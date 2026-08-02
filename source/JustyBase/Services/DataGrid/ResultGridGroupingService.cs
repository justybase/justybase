namespace JustyBase.Services.DataGrid;

public sealed class ResultGridGroupingService : IResultGridGroupingService
{
    public GroupingTogglePlan BuildTogglePlan(
        string columnName,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> groupedPropertyNames)
    {
        if (string.IsNullOrWhiteSpace(columnName) || headers.Count == 0)
        {
            return GroupingTogglePlan.None;
        }

        int headerIndex = FindHeaderIndex(headers, columnName);
        if (headerIndex < 0)
        {
            return GroupingTogglePlan.None;
        }

        string propertyName = GroupPropertyNameHelper.CreatePropertyName(headerIndex);
        for (int i = 0; i < groupedPropertyNames.Count; i++)
        {
            if (string.Equals(groupedPropertyNames[i], propertyName, StringComparison.Ordinal))
            {
                return new GroupingTogglePlan(GroupingToggleAction.Remove, propertyName, i);
            }
        }

        return new GroupingTogglePlan(GroupingToggleAction.Add, propertyName, -1);
    }

    public bool TryFindMoveIndexes(
        IReadOnlyList<string> groupedPropertyNames,
        IReadOnlyList<string> headers,
        string sourceColumnName,
        string targetColumnName,
        out int sourceIndex,
        out int targetIndex)
    {
        return GroupedColumnMappingHelper.TryFindMoveIndexes(
            groupedPropertyNames,
            headers,
            sourceColumnName,
            targetColumnName,
            out sourceIndex,
            out targetIndex);
    }

    public List<string> ToGroupedColumnNames(
        IReadOnlyList<string> groupedPropertyNames,
        IReadOnlyList<string> headers)
    {
        return GroupedColumnMappingHelper.ToGroupedColumnNames(groupedPropertyNames, headers);
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, string columnName)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], columnName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

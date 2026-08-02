namespace JustyBase.Services.DataGrid;

public enum GroupingToggleAction
{
    None = 0,
    Add = 1,
    Remove = 2,
}

public readonly record struct GroupingTogglePlan(GroupingToggleAction Action, string PropertyName, int ExistingIndex)
{
    public static GroupingTogglePlan None => new(GroupingToggleAction.None, string.Empty, -1);
}

public interface IResultGridGroupingService
{
    GroupingTogglePlan BuildTogglePlan(
        string columnName,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> groupedPropertyNames);

    bool TryFindMoveIndexes(
        IReadOnlyList<string> groupedPropertyNames,
        IReadOnlyList<string> headers,
        string sourceColumnName,
        string targetColumnName,
        out int sourceIndex,
        out int targetIndex);

    List<string> ToGroupedColumnNames(
        IReadOnlyList<string> groupedPropertyNames,
        IReadOnlyList<string> headers);
}

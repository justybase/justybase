namespace JustyBase.ViewModels.Tools;

public interface ISqlResultsViewBridge
{
    void CollapseAllGroups();

    void ExpandAllGroups();

    void RemoveGroupByColumnName(string columnName);

    void RecalculateSummaryValues();

    /// <summary>
    /// Refreshes DataGrid columns after data is loaded. This is needed because DataGrid_Initialized
    /// may be called before data is available, resulting in no columns being created.
    /// </summary>
    void RefreshColumns();

    /// <summary>
    /// Detach DataGrid from the collection view so large result mutations do not layout/bind.
    /// </summary>
    void SuspendGridBinding();

    /// <summary>
    /// Re-attach DataGrid to the current collection view after bulk load completes.
    /// </summary>
    void ResumeGridBinding();
}

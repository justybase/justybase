namespace JustyBase.Services.DataGrid;

public enum ResultGridGroupOperation
{
    Collapse,
    Expand
}

public interface IResultGridGroupExpandCollapseService
{
    bool TryCommitPendingEdit(Action commitEditAction, Action<Exception> onError);

    bool TryExecuteGroupOperation(
        ResultGridGroupOperation operation,
        Action collapseAllGroupsAction,
        Action expandAllGroupsAction,
        Action<Exception> onError);
}

namespace JustyBase.Services.DataGrid;

public sealed class ResultGridGroupExpandCollapseService : IResultGridGroupExpandCollapseService
{
    public bool TryCommitPendingEdit(Action commitEditAction, Action<Exception> onError)
    {
        return TryExecute(commitEditAction, onError);
    }

    public bool TryExecuteGroupOperation(
        ResultGridGroupOperation operation,
        Action collapseAllGroupsAction,
        Action expandAllGroupsAction,
        Action<Exception> onError)
    {
        Action operationAction = operation switch
        {
            ResultGridGroupOperation.Collapse => collapseAllGroupsAction,
            ResultGridGroupOperation.Expand => expandAllGroupsAction,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

        return TryExecute(operationAction, onError);
    }

    private static bool TryExecute(Action action, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onError);

        try
        {
            action();
            return true;
        }
        catch (ObjectDisposedException ex)
        {
            onError(ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            onError(ex);
            return false;
        }
    }
}

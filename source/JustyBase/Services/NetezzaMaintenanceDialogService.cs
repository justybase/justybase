using JustyBase.Common.Contracts;
using JustyBase.ViewModels;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services;

public sealed class NetezzaMaintenanceDialogService : INetezzaMaintenanceDialogService
{
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IMessageForUserTools _messageForUserTools;

    public NetezzaMaintenanceDialogService(
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IMessageForUserTools messageForUserTools)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _messageForUserTools = messageForUserTools;
    }

    public Task<string?> ShowAsync(NetezzaMaintenanceDialogKind kind, string qualifiedTable)
    {
        var tcs = new TaskCompletionSource<string?>();
        _messageForUserTools.DispatcherActionInstance(async () =>
        {
            try
            {
                var vm = new NetezzaMaintenanceDialogViewModel(kind, qualifiedTable);
                var dialog = new NetezzaMaintenanceDialog(vm);
                var confirmed = await dialog.ShowDialog<bool>(_avaloniaSpecificHelpers.GetMainWindow());
                tcs.TrySetResult(confirmed ? vm.ResultSql : null);
            }
            catch (Exception)
            {
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }
}

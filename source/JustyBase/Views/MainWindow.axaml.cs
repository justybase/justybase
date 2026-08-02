using Avalonia.Input;
using Avalonia.Interactivity;
using JustyBase.Common.Contracts;
using JustyBase.Helpers.Interactions;
using JustyBase.ViewModels;

namespace JustyBase.Views;

public partial class MainWindow : Window
{
    private readonly INotificationManagerProvider _notificationManagerProvider;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly AboutViewModel _aboutViewModel;

    public MainWindow(INotificationManagerProvider notificationManagerProvider, IMessageForUserTools messageForUserTools, AboutViewModel aboutViewModel)
    {
        _notificationManagerProvider = notificationManagerProvider;
        _messageForUserTools = messageForUserTools;
        _aboutViewModel = aboutViewModel;
        InitializeComponent();
        // Tunnel so Ctrl+B still works when the SQL editor has focus and would otherwise eat the key.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;
        Program.SetUpDispatcherExceptionHandling();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.B || e.KeyModifiers != KeyModifiers.Control)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel vm || !vm.ConcentrateModeCommand.CanExecute(null))
        {
            return;
        }

        vm.ConcentrateModeCommand.Execute(null);
        e.Handled = true;
    }

    private void MainWindow_Loaded(object? sender, EventArgs e)
    {
        _notificationManagerProvider.SetWindow(this);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        (this.DataContext as MainWindowViewModel)?.WindowClosingCommand?.Execute(this);
    }
}

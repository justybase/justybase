using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using JustyBase.Common.Contracts;
using JustyBase.Helpers.Interactions;
using JustyBase.Services.Actions;
using JustyBase.ViewModels;

namespace JustyBase.Views;

public partial class MainWindow : Window
{
    private readonly INotificationManagerProvider _notificationManagerProvider;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly AboutViewModel _aboutViewModel;
    private readonly List<KeyBinding> _dynamicBindings = [];
    private MainWindowViewModel? _boundViewModel;

    public MainWindow(INotificationManagerProvider notificationManagerProvider, IMessageForUserTools messageForUserTools, AboutViewModel aboutViewModel)
    {
        StartupTrace.Write("MainWindow constructor start");
        _notificationManagerProvider = notificationManagerProvider;
        _messageForUserTools = messageForUserTools;
        _aboutViewModel = aboutViewModel;
        InitializeComponent();
        // Tunnel so Ctrl+B still works when the SQL editor has focus and would otherwise eat the key.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += MainWindow_DataContextChanged;
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Program.SetUpDispatcherExceptionHandling();
        StartupTrace.Write("MainWindow constructor completed");
    }

    private void MainWindow_DataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel?.Keymap is { } oldKeymap)
        {
            oldKeymap.Changed -= Keymap_Changed;
        }

        _boundViewModel = DataContext as MainWindowViewModel;
        if (_boundViewModel?.Keymap is { } keymap)
        {
            keymap.Changed += Keymap_Changed;
        }

        RebuildActionKeyBindings();
    }

    private void Keymap_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildActionKeyBindings();
        }
        else
        {
            Dispatcher.UIThread.Post(RebuildActionKeyBindings);
        }
    }

    private void RebuildActionKeyBindings()
    {
        foreach (KeyBinding binding in _dynamicBindings)
        {
            KeyBindings.Remove(binding);
        }

        _dynamicBindings.Clear();

        if (_boundViewModel?.Actions is not { } actions || _boundViewModel.Keymap is not { } keymap)
        {
            return;
        }

        foreach (IdeAction action in actions.All)
        {
            string? gesture = keymap.GetGesture(action.Id);
            if (string.IsNullOrWhiteSpace(gesture))
            {
                continue;
            }

            KeyGesture parsed;
            try
            {
                parsed = KeyGesture.Parse(gesture);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                // A hand-edited keymap can contain a stale gesture; the editor will show it as
                // unbound rather than preventing the application from receiving keys.
                continue;
            }

            var binding = new KeyBinding
            {
                Gesture = parsed,
                Command = _boundViewModel.RunActionCommand,
                CommandParameter = action.Id,
            };
            KeyBindings.Add(binding);
            _dynamicBindings.Add(binding);
        }
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
        StartupTrace.Write("MainWindow Loaded");
        _notificationManagerProvider.SetWindow(this);
        (DataContext as MainWindowViewModel)?.StartAutomaticUpdateCheck();
        StartupTrace.Write("MainWindow Loaded handler completed");
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StartupTrace.Write($"MainWindow Closing cancel={e.Cancel}");
        (this.DataContext as MainWindowViewModel)?.WindowClosingCommand?.Execute(this);
        StartupTrace.Write("MainWindow Closing handler completed");
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_boundViewModel?.Keymap is { } keymap)
        {
            keymap.Changed -= Keymap_Changed;
        }

        StartupTrace.Write("MainWindow Closed");
    }
}

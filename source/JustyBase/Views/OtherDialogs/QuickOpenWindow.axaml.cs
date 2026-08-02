using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JustyBase.ViewModels;

namespace JustyBase.Views.OtherDialogs;

public partial class QuickOpenWindow : Window
{
    private bool _activationReady;
    private bool _closing;

    /// <summary>
    /// Set before Close on Accept so Deactivated does not treat Enter as a cancel.
    /// </summary>
    public bool IsAccepting { get; set; }

    public QuickOpenWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Deactivated += OnDeactivated;

        // Tunnel + handledEventsToo: TextBox/ListBox otherwise swallow Enter / pointer events.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Results.AddHandler(DoubleTappedEvent, OnResultsDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        Results.AddHandler(PointerPressedEvent, OnResultsPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public QuickOpenWindow(QuickOpenViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private QuickOpenViewModel? Vm => DataContext as QuickOpenViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        // Avoid closing immediately when the dialog steals activation from the owner.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _activationReady = true);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_activationReady || _closing || IsAccepting)
            return;
        Vm?.Cancel();
        if (Vm is null)
            Close(null);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null)
            return;

        switch (e.Key)
        {
            case Key.Down:
                Vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                Vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                Vm.Cancel();
                e.Handled = true;
                break;
        }
    }

    private void OnResultsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null)
            return;

        var entry = FindEntry(e.Source);
        if (entry is null || !entry.IsSelectable)
            return;

        Vm.SelectEntryFromClick(entry);
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is null)
            return;

        var entry = FindEntry(e.Source);
        if (entry is null || !entry.IsSelectable)
            return;

        Vm.SelectEntryFromClick(entry);
        Accept();
        e.Handled = true;
    }

    private void Accept()
    {
        if (Vm is null)
            return;

        IsAccepting = true;
        Vm.AcceptSelection();
    }

    private static QuickOpenEntryViewModel? FindEntry(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: QuickOpenEntryViewModel entry })
                return entry;
            current = current.GetVisualParent();
        }
        return null;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }
}

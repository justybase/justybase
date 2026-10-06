using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class SqlOutlineView : UserControl
{
    public SqlOutlineView()
    {
        InitializeComponent();
    }

    private SqlOutlineViewModel? ViewModel => DataContext as SqlOutlineViewModel;

    private static SqlOutlineItem? ItemFromSource(object? source)
    {
        Visual? current = source as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: SqlOutlineItem item })
                return item;
            current = current.GetVisualParent();
        }

        return null;
    }

    private void OutlineTree_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsFromToggleButton(e.Source))
            return;

        var item = ItemFromSource(e.Source);
        if (item is null || ViewModel is not { } vm)
            return;

        // VS Code double-click: jump and focus the editor.
        vm.NavigateToItem(item, focusEditor: true);
        e.Handled = true;
    }

    private void OutlineTree_Tapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // The chevron only expands; it must not navigate.
        if (IsFromToggleButton(e.Source))
            return;

        // Single click on the already-selected item does not change selection,
        // but VS Code still navigates there.
        var item = ItemFromSource(e.Source);
        if (item is null || ViewModel is not { } vm)
            return;

        if (ReferenceEquals(item, vm.SelectedItem))
            vm.NavigateToItem(item, focusEditor: false);
    }

    private static bool IsFromToggleButton(object? source)
    {
        Visual? current = source as Visual;
        while (current is not null)
        {
            if (current is ToggleButton)
                return true;

            current = current.GetVisualParent();
        }

        return false;
    }
}

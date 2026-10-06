using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustyBase.Models.Tools;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class FileExplorerView : UserControl
{
    public FileExplorerView()
    {
        InitializeComponent();
        ExplorerTreeArea.AddHandler(KeyDownEvent, ExplorerTree_KeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    private FileExplorerViewModel? ViewModel => DataContext as FileExplorerViewModel;

    private static FileTreeNodeModel? RowNode(object? sender) =>
        (sender as Control)?.DataContext as FileTreeNodeModel;

    private void ExplorerRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var node = RowNode(sender);
        if (node is null || ViewModel is not { } vm)
        {
            return;
        }

        // Any press selects the row (left = select, right = select for the
        // context menu which opens on release). Selecting on chevron press
        // matches VS Code too.
        vm.SelectedTreeItem = node;
    }

    private void ExplorerRow_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // The chevron ToggleButton already toggles on double click; skip it.
        if (IsFromToggleButton(e.Source))
        {
            return;
        }

        var node = RowNode(sender);
        if (node is null || ViewModel is not { } vm)
        {
            return;
        }

        vm.SelectedTreeItem = node;
        if (node.IsDirectory)
        {
            node.IsExpanded = !node.IsExpanded;
        }
        else
        {
            vm.OpenTxtPreviewFile(node.Path);
        }

        e.Handled = true;
    }

    private static bool IsFromToggleButton(object? source)
    {
        Visual? current = source as Visual;
        while (current is not null)
        {
            if (current is ToggleButton)
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private void ExplorerTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                vm.StepTreeSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                vm.StepTreeSelection(1);
                e.Handled = true;
                break;
            case Key.Left:
                vm.TreeLeft();
                e.Handled = true;
                break;
            case Key.Right:
                vm.TreeRight();
                e.Handled = true;
                break;
            case Key.Enter:
                if (vm.SelectedTreeItem is { } selected)
                {
                    if (selected.IsDirectory)
                    {
                        selected.IsExpanded = !selected.IsExpanded;
                    }
                    else
                    {
                        vm.OpenTxtPreviewFile(selected.Path);
                    }

                    e.Handled = true;
                }

                break;
            case Key.F2:
                _ = vm.RenameNodeCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }
}

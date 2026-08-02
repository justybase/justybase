using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class GitView : UserControl
{
    public GitView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is GitViewModel vm)
            _ = vm.InitializeAsync();
    }

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (Resources.TryGetValue("GitSettingsFlyout", out object? resource) && resource is Flyout flyout)
        {
            if (flyout.Content is Control content)
                content.DataContext = DataContext;
            flyout.ShowAt(MoreButton);
        }
    }

    private void OnCommitPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not GitViewModel vm)
            return;
        if (sender is Control { DataContext: GitCommitItem commit })
            _ = vm.EnsureCommitTooltipAsync(commit);
    }

    private async void OnCopyCommitHashClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hash } || string.IsNullOrWhiteSpace(hash))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, hash));
        await clipboard.SetDataAsync(data);
    }
}

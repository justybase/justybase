using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class SqlDiagnosticsView : UserControl
{
    public SqlDiagnosticsView()
    {
        InitializeComponent();
    }

    private async void OnCopyDiagnostic(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is DiagnosticItem item)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                var clipboard = window.Clipboard;
                if (clipboard is not null)
                    await clipboard.SetTextAsync(item.CopyText);
            }
        }
    }
}

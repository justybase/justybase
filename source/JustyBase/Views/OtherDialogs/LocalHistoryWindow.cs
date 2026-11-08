using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JustyBase.Common.Contracts;
using JustyBase.Services.History;

namespace JustyBase.Views.OtherDialogs;

/// <summary>
/// Local History for one file: every kept version on the left and the selected version's text on
/// the right, with a Restore button. Kept in code rather than XAML because it is a small, fixed
/// dialog with no bindings that would benefit from compiled templates.
/// </summary>
public sealed class LocalHistoryWindow : Window
{
    private readonly ILocalHistoryService _history;
    private readonly IMessageForUserTools _messages;
    private readonly string _filePath;
    private readonly ListBox _versions = new();
    private readonly TextBox _preview = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas,Menlo,monospace"),
    };

    private readonly Button _restoreButton = new() { Content = "Restore", IsEnabled = false };
    private IReadOnlyList<LocalHistoryEntry> _entries = [];

    public LocalHistoryWindow(
        ILocalHistoryService history,
        IMessageForUserTools messages,
        string filePath)
    {
        _history = history;
        _messages = messages;
        _filePath = filePath;

        Title = $"Local History — {Path.GetFileName(filePath)}";
        Width = 980;
        Height = 620;
        MinWidth = 620;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _versions.SelectionChanged += (_, _) => _ = LoadPreviewAsync();
        _restoreButton.Click += async (_, _) => await RestoreSelectedAsync();

        var closeButton = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _restoreButton, closeButton },
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,8,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetColumn(_versions, 0);
        Grid.SetColumn(_preview, 2);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_preview, 0);
        Grid.SetColumnSpan(_preview, 1);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        Grid.SetRow(splitter, 0);

        body.Children.Add(_versions);
        body.Children.Add(splitter);
        body.Children.Add(_preview);
        body.Children.Add(buttons);
        Content = body;

        Opened += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _entries = await _history.GetEntriesAsync(_filePath);
            _versions.ItemsSource = _entries.Select(entry => new Row(entry)).ToArray();
            _versions.SelectedIndex = _entries.Count > 0 ? 0 : -1;
            if (_entries.Count == 0)
            {
                _preview.Text = "No versions were recorded yet. Save the file or reopen it to start the history.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _preview.Text = $"Could not read local history:\n{ex.Message}";
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (_versions.SelectedItem is not Row row)
        {
            _restoreButton.IsEnabled = false;
            return;
        }

        _restoreButton.IsEnabled = true;
        try
        {
            _preview.Text = await _history.ReadTextAsync(row.Entry) ?? "(the stored version is gone)";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _preview.Text = $"Could not read the stored version:\n{ex.Message}";
        }
    }

    private async Task RestoreSelectedAsync()
    {
        if (_versions.SelectedItem is not Row row)
        {
            return;
        }

        bool confirmed = await _messages.ShowConfirmationDialogAsync(
            $"Restore {Path.GetFileName(_filePath)} to the version from " +
            $"{row.Entry.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}?\n\n" +
            "The current content is kept in Local History first.",
            "Restore version");

        if (!confirmed)
        {
            return;
        }

        try
        {
            string? restored = await _history.RestoreAsync(row.Entry);
            if (restored is null)
            {
                _messages.ShowSimpleMessageBoxInstance(
                    "That version is no longer stored.",
                    "Local History");
                await ReloadAsync();
                return;
            }

            _preview.Text = restored;
            _messages.ShowSimpleMessageBoxInstance(
                "The file was restored. Reload it in the editor if the change is not visible yet.",
                "Local History");
            await ReloadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messages.ShowSimpleMessageBoxInstance(
                $"Restore failed:\n{ex.Message}",
                "Local History");
        }
    }

    private sealed record Row(LocalHistoryEntry Entry)
    {
        public override string ToString()
            => $"{Entry.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {Entry.Reason}  {FormatSize(Entry.Length)}";

        private static string FormatSize(long bytes)
            => bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
                _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
            };
    }
}

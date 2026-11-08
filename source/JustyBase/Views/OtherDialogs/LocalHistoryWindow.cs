using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JustyBase.Common.Contracts;
using JustyBase.Services.Files;
using JustyBase.Services.History;

namespace JustyBase.Views.OtherDialogs;

/// <summary>
/// Local History for one file: every kept version on the left, the selected version's text on the
/// right, its label, and actions to compare with the current file or restore it. Kept in code rather
/// than XAML because it is a small, fixed dialog.
/// </summary>
public sealed class LocalHistoryWindow : Window
{
    private readonly ILocalHistoryService _history;
    private readonly IMessageForUserTools _messages;
    private readonly Action<string>? _onRestored;
    private readonly Func<string?>? _getCurrentText;
    private readonly string _filePath;
    private readonly ListBox _versions = new();
    private readonly TextBox _preview = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas,Menlo,monospace"),
    };

    private readonly TextBox _labelBox = new()
    {
        PlaceholderText = "Label this version (optional)",
        Width = 320,
        Margin = new Thickness(0, 0, 8, 0),
    };

    private readonly Button _saveLabelButton = new() { Content = "Save label", IsEnabled = false };
    private readonly Button _compareButton = new() { Content = "Compare with current", IsEnabled = false };
    private readonly Button _restoreButton = new() { Content = "Restore", IsEnabled = false };
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<LocalHistoryEntry> _entries = [];

    /// <summary>Completes after the first version list was read; lets headless tests render real content.</summary>
    internal Task LoadedTask => _loaded.Task;

    public LocalHistoryWindow(
        ILocalHistoryService history,
        IMessageForUserTools messages,
        string filePath,
        Action<string>? onRestored = null,
        Func<string?>? getCurrentText = null)
    {
        _history = history;
        _messages = messages;
        _filePath = filePath;
        _onRestored = onRestored;
        _getCurrentText = getCurrentText;

        Title = $"Local History - {Path.GetFileName(filePath)}";
        Width = 1060;
        Height = 640;
        MinWidth = 680;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _versions.SelectionChanged += (_, _) => _ = LoadPreviewAsync();
        _restoreButton.Click += async (_, _) => await RestoreSelectedAsync();
        _compareButton.Click += async (_, _) => await CompareSelectedAsync();
        _saveLabelButton.Click += async (_, _) => await SaveLabelAsync();

        var labelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { _labelBox, _saveLabelButton },
        };

        var closeButton = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _compareButton, _restoreButton, closeButton },
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("360,8,*"),
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(10),
        };
        Grid.SetColumn(_versions, 0);
        Grid.SetColumn(_preview, 2);
        Grid.SetRow(_preview, 0);
        Grid.SetRow(labelRow, 1);
        Grid.SetColumnSpan(labelRow, 3);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 3);
        buttons.Margin = new Thickness(0, 8, 0, 0);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        Grid.SetRow(splitter, 0);

        body.Children.Add(_versions);
        body.Children.Add(splitter);
        body.Children.Add(_preview);
        body.Children.Add(labelRow);
        body.Children.Add(buttons);
        Content = body;

        Opened += async (_, _) =>
        {
            try
            {
                await ReloadAsync();
            }
            finally
            {
                _loaded.TrySetResult();
            }
        };
    }

    private async Task ReloadAsync(long? selectId = null)
    {
        try
        {
            _entries = await _history.GetEntriesAsync(_filePath);
            _versions.ItemsSource = _entries.Select(entry => new Row(entry)).ToArray();

            int index = selectId is long id
                ? _entries.ToList().FindIndex(entry => entry.Id == id)
                : -1;
            _versions.SelectedIndex = index >= 0
                ? index
                : _entries.Count > 0 ? 0 : -1;
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
            _compareButton.IsEnabled = false;
            _saveLabelButton.IsEnabled = false;
            return;
        }

        _restoreButton.IsEnabled = true;
        _compareButton.IsEnabled = true;
        _saveLabelButton.IsEnabled = true;
        _labelBox.Text = row.Entry.Label ?? string.Empty;

        try
        {
            _preview.Text = await _history.ReadTextAsync(row.Entry) ?? "(the stored version is gone)";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _preview.Text = $"Could not read the stored version:\n{ex.Message}";
        }
    }

    private async Task CompareSelectedAsync()
    {
        if (_versions.SelectedItem is not Row row)
        {
            return;
        }

        try
        {
            string? stored = await _history.ReadTextAsync(row.Entry);
            if (stored is null)
            {
                _messages.ShowSimpleMessageBoxInstance(
                    "That version is no longer stored.",
                    "Local History");
                return;
            }

            string? current = _getCurrentText?.Invoke();
            if (current is null)
            {
                if (!File.Exists(_filePath))
                {
                    _messages.ShowSimpleMessageBoxInstance(
                        "The file no longer exists, so there is nothing to compare it with.",
                        "Local History");
                    return;
                }

                current = (await SafeFileWriter.ReadAsync(_filePath)).Text;
            }

            await _messages.ShowGitDiffDialogAsync(
                $"Local History - {Path.GetFileName(_filePath)} - {row.Entry.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
                stored,
                current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messages.ShowSimpleMessageBoxInstance(
                $"Comparison failed:\n{ex.Message}",
                "Local History");
        }
    }

    private async Task SaveLabelAsync()
    {
        if (_versions.SelectedItem is not Row row)
        {
            return;
        }

        try
        {
            LocalHistoryEntry? updated = await _history.SetLabelAsync(row.Entry.Id, _labelBox.Text);
            if (updated is null)
            {
                _messages.ShowSimpleMessageBoxInstance(
                    "That version is no longer stored.",
                    "Local History");
                return;
            }

            await ReloadAsync(updated.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messages.ShowSimpleMessageBoxInstance(
                $"The label could not be saved:\n{ex.Message}",
                "Local History");
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
            "The current content, including unsaved editor changes, is kept in Local History first.",
            "Restore version");

        if (!confirmed)
        {
            return;
        }

        try
        {
            string? currentText = _getCurrentText?.Invoke();
            string? restored = await _history.RestoreAsync(row.Entry, currentText);
            if (restored is null)
            {
                _messages.ShowSimpleMessageBoxInstance(
                    "That version is no longer stored.",
                    "Local History");
                await ReloadAsync();
                return;
            }

            _preview.Text = restored;
            _onRestored?.Invoke(restored);
            _messages.ShowSimpleMessageBoxInstance(
                "The file was restored and the editor now shows that version.",
                "Local History");
            await ReloadAsync(row.Entry.Id);
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
        {
            string label = string.IsNullOrWhiteSpace(Entry.Label) ? string.Empty : $"[{Entry.Label}]  ";
            return $"{label}{Entry.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {Entry.Reason}  {FormatSize(Entry.Length)}";
        }

        private static string FormatSize(long bytes)
            => bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
                _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
            };
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JustyBase.Services.Actions;

namespace JustyBase.Views.OtherDialogs;

/// <summary>
/// A small keymap editor: every registered action, its current gesture, and a text box for a new
/// gesture. Saving is refused when the gesture is already taken, so the map can never contain a
/// shortcut that runs two different commands.
/// </summary>
public sealed class KeymapWindow : Window
{
    private readonly KeymapService _keymap;
    private readonly ListBox _actions = new();
    private readonly TextBox _gesture = new()
    {
        PlaceholderText = "e.g. Ctrl+Shift+O (empty removes the binding)",
        Margin = new Thickness(0, 0, 0, 6),
    };

    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        MinHeight = 34,
    };

    private readonly Button _apply = new() { Content = "Apply", IsEnabled = false };
    private readonly Button _reset = new() { Content = "Reset action", IsEnabled = false };
    private readonly Button _resetAll = new() { Content = "Reset all" };

    public KeymapWindow(KeymapService keymap)
    {
        _keymap = keymap;

        Title = "Keyboard Shortcuts";
        Width = 760;
        Height = 620;
        MinWidth = 540;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _actions.SelectionChanged += (_, _) => OnSelectionChanged();
        _actions.DoubleTapped += (_, _) => _gesture.Focus();
        _apply.Click += async (_, _) => await ApplyAsync();
        _reset.Click += async (_, _) => await ResetSelectedAsync();
        _resetAll.Click += async (_, _) => await ResetAllAsync();

        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _resetAll, _reset, _apply, close },
        };

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto"),
            Margin = new Thickness(10),
        };

        var editor = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
        };
        editor.Children.Add(new TextBlock { Text = "Shortcut for the selected action", Opacity = 0.75 });
        editor.Children.Add(_gesture);

        Grid.SetRow(_actions, 0);
        Grid.SetRow(editor, 1);
        Grid.SetRow(_status, 2);
        Grid.SetRow(buttons, 3);
        body.Children.Add(_actions);
        body.Children.Add(editor);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;

        Opened += (_, _) => Reload();
    }

    private void Reload(string? successMessage = null, string? selectActionId = null)
    {
        string? keepActionId = selectActionId
            ?? (_actions.SelectedItem as Row)?.Entry.Action.Id;
        KeymapEntry[] entries = _keymap.GetAll().ToArray();
        Row[] rows = entries.Select(entry => new Row(entry)).ToArray();
        _actions.ItemsSource = rows;

        int index = keepActionId is null
            ? -1
            : Array.FindIndex(rows, row => string.Equals(
                row.Entry.Action.Id, keepActionId, StringComparison.Ordinal));
        _actions.SelectedIndex = index >= 0 ? index : rows.Length > 0 ? 0 : -1;
        OnSelectionChanged();

        KeymapConflict[] conflicts = _keymap.FindConflicts().ToArray();
        if (conflicts.Length > 0)
        {
            SetStatus(
                "Conflicts: " + string.Join("; ", conflicts.Select(conflict =>
                    $"{conflict.Gesture} - {string.Join(", ", conflict.Actions.Select(action => action.Title))}")),
                isError: true);
        }
        else if (successMessage is not null)
        {
            SetStatus(successMessage, isError: false);
        }
    }

    private void OnSelectionChanged()
    {
        if (_actions.SelectedItem is not Row row)
        {
            _apply.IsEnabled = false;
            _reset.IsEnabled = false;
            return;
        }

        _gesture.Text = row.Entry.Gesture ?? string.Empty;
        _apply.IsEnabled = true;
        _reset.IsEnabled = row.Entry.IsCustom;
        SetStatus(
            row.Entry.IsCustom
                ? $"Custom binding. Default: {row.Entry.Action.Shortcut ?? "(none)"}."
                : $"Default binding from the action declaration.",
            isError: false);
    }

    private async Task ApplyAsync()
    {
        if (_actions.SelectedItem is not Row row)
        {
            return;
        }

        KeymapUpdateResult result;
        try
        {
            result = await _keymap.SetGestureAsync(row.Entry.Action.Id, _gesture.Text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus($"The shortcut could not be saved: {exception.Message}", isError: true);
            return;
        }

        if (!result.Success)
        {
            SetStatus(result.Error ?? "The shortcut could not be saved.", isError: true);
            return;
        }

        Reload(
            string.IsNullOrWhiteSpace(result.Gesture)
                ? "The binding was removed."
                : $"Saved {result.Gesture} for {row.Entry.Action.Title}.",
            row.Entry.Action.Id);
    }

    private async Task ResetSelectedAsync()
    {
        if (_actions.SelectedItem is not Row row)
        {
            return;
        }

        try
        {
            await _keymap.ResetAsync(row.Entry.Action.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus($"The shortcut could not be reset: {exception.Message}", isError: true);
            return;
        }

        Reload($"Restored the default for {row.Entry.Action.Title}.", row.Entry.Action.Id);
    }

    private async Task ResetAllAsync()
    {
        bool confirmed = await ShowConfirmationAsync(
            "Reset every shortcut to its default?",
            "Reset all shortcuts");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _keymap.ResetAllAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus($"The shortcuts could not be reset: {exception.Message}", isError: true);
            return;
        }

        Reload("Every shortcut is back to its default.");
    }

    private Task<bool> ShowConfirmationAsync(string message, string title)
    {
        var dialog = new ConfirmationWindow(message, title, confirmText: "Reset all", cancelText: "Cancel")
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return dialog.ShowDialog<bool>(this);
    }

    private void SetStatus(string message, bool isError)
    {
        _status.Text = message;
        _status.Foreground = isError ? Brushes.IndianRed : Brushes.Gray;
    }

    private sealed record Row(KeymapEntry Entry)
    {
        public override string ToString()
        {
            string gesture = string.IsNullOrWhiteSpace(Entry.Gesture) ? "unbound" : Entry.Gesture;
            string custom = Entry.IsCustom ? "  (custom)" : string.Empty;
            return $"{Entry.Action.Title}    ·    {Entry.Action.Group}    ·    {gesture}{custom}";
        }
    }
}

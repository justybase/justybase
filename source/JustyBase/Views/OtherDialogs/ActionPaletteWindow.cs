using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JustyBase.Services.Actions;

namespace JustyBase.Views.OtherDialogs;

/// <summary>
/// Find Action: every action by name, narrowed as words are typed, with its group and shortcut.
/// Enter runs the selected one; Escape closes without running anything.
/// </summary>
public sealed class ActionPaletteWindow : Window
{
    private readonly ActionRegistry _registry;
    private readonly Action<IdeAction?> _accept;
    private readonly TextBox _query = new()
    {
        PlaceholderText = "Type an action name…",
        Margin = new Thickness(0, 0, 0, 8),
    };

    private readonly ListBox _results = new();

    public ActionPaletteWindow(ActionRegistry registry, Action<IdeAction?> accept)
    {
        _registry = registry;
        _accept = accept;

        Title = "Find Action";
        Width = 640;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _query.TextChanged += (_, _) => Rebuild();
        _query.KeyDown += OnQueryKeyDown;
        _results.DoubleTapped += (_, _) => Accept();
        _results.KeyDown += OnResultsKeyDown;

        var body = new DockPanel();
        DockPanel.SetDock(_query, Avalonia.Controls.Dock.Top);
        body.Children.Add(_query);
        body.Children.Add(_results);
        Content = body;

        Opened += (_, _) =>
        {
            Rebuild();
            _query.Focus();
        };
    }

    private void Rebuild()
    {
        IReadOnlyList<IdeAction> actions = _registry.Search(_query.Text);
        _results.ItemsSource = actions.Select(action => new Row(action)).ToArray();
        _results.SelectedIndex = actions.Count > 0 ? 0 : -1;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _results.ItemCount > 0:
                _results.SelectedIndex = Math.Min(_results.SelectedIndex + 1, _results.ItemCount - 1);
                e.Handled = true;
                break;
            case Key.Up when _results.ItemCount > 0:
                _results.SelectedIndex = Math.Max(_results.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                CloseWith(null);
                e.Handled = true;
                break;
        }
    }

    private void OnResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseWith(null);
            e.Handled = true;
        }
    }

    private void Accept()
    {
        if (_results.SelectedItem is Row row)
        {
            CloseWith(row.Action);
        }
    }

    private void CloseWith(IdeAction? action)
    {
        _accept(action);
        Close();
    }

    private sealed record Row(IdeAction Action)
    {
        public override string ToString()
        {
            string shortcut = string.IsNullOrWhiteSpace(Action.Shortcut) ? string.Empty : $"    {Action.Shortcut}";
            return $"{Action.Title}    ·    {Action.Group}{shortcut}";
        }
    }
}

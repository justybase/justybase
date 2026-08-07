using Avalonia.Markup.Xaml;
using JustyBase.ViewModels.Tools;
using JustyBase.Ai.Models;
using JustyBase.Common.Models;

namespace JustyBase.Views.Tools;

public partial class AiChatView : UserControl
{
    private ListBox? _slashCommandListBox;
    private ListBox? _mentionListBox;
    private int _slashCommandSelectedIndex = -1;
    private int _mentionSelectedIndex = -1;
    private MenuFlyout? _chatActionsMenuFlyout;
    private MenuItem? _conversationsSectionHeader;
    private readonly List<MenuItem> _flyoutSessionItems = [];

    public AiChatView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _slashCommandListBox = this.FindControl<ListBox>("SlashCommandListBox");
        _mentionListBox = this.FindControl<ListBox>("MentionListBox");
        _chatActionsMenuFlyout = this.FindControl<Button>("ChatActionsButton")?.Flyout as MenuFlyout;
        _conversationsSectionHeader = this.FindControl<MenuItem>("ConversationsSectionHeader");
    }

    private void OnChatActionsFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is not AiChatViewModel vm
            || _chatActionsMenuFlyout is not { } flyout
            || _conversationsSectionHeader is not { } header)
        {
            return;
        }

        foreach (var item in _flyoutSessionItems)
        {
            flyout.Items.Remove(item);
        }
        _flyoutSessionItems.Clear();

        var index = flyout.Items.IndexOf(header);
        if (index < 0)
        {
            return;
        }

        foreach (var session in vm.SavedSessions)
        {
            var item = new MenuItem
            {
                Header = session.Title,
                Command = vm.OpenSavedSessionCommand,
                CommandParameter = session,
                IsEnabled = vm.CanSwitchSession,
            };
            flyout.Items.Insert(++index, item);
            _flyoutSessionItems.Add(item);
        }
    }

    private void InputTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AiChatViewModel vm)
            return;

        if (vm.ShowMentionMenu && _mentionListBox is { Items.Count: > 0 })
        {
            switch (e.Key)
            {
                case Key.Down:
                    _mentionSelectedIndex = Math.Min(_mentionSelectedIndex + 1, _mentionListBox.Items.Count - 1);
                    _mentionListBox.SelectedIndex = _mentionSelectedIndex;
                    _mentionListBox.ScrollIntoView(_mentionListBox.SelectedItem!);
                    e.Handled = true;
                    return;
                case Key.Up:
                    _mentionSelectedIndex = Math.Max(_mentionSelectedIndex - 1, 0);
                    _mentionListBox.SelectedIndex = _mentionSelectedIndex;
                    _mentionListBox.ScrollIntoView(_mentionListBox.SelectedItem!);
                    e.Handled = true;
                    return;
                case Key.Enter or Key.Tab:
                    if (_mentionSelectedIndex >= 0 && _mentionListBox.SelectedItem is MentionItem mention)
                    {
                        vm.InsertMentionItemCommand.Execute(mention);
                        _mentionSelectedIndex = -1;
                        e.Handled = true;
                        return;
                    }
                    break;
                case Key.Escape:
                    vm.ShowMentionMenu = false;
                    _mentionSelectedIndex = -1;
                    e.Handled = true;
                    return;
            }
        }

        if (vm.ShowSlashCommandMenu && _slashCommandListBox is { Items.Count: > 0 })
        {
            switch (e.Key)
            {
                case Key.Down:
                    _slashCommandSelectedIndex = Math.Min(_slashCommandSelectedIndex + 1, _slashCommandListBox.Items.Count - 1);
                    _slashCommandListBox.SelectedIndex = _slashCommandSelectedIndex;
                    _slashCommandListBox.ScrollIntoView(_slashCommandListBox.SelectedItem!);
                    e.Handled = true;
                    return;
                case Key.Up:
                    _slashCommandSelectedIndex = Math.Max(_slashCommandSelectedIndex - 1, 0);
                    _slashCommandListBox.SelectedIndex = _slashCommandSelectedIndex;
                    _slashCommandListBox.ScrollIntoView(_slashCommandListBox.SelectedItem!);
                    e.Handled = true;
                    return;
                case Key.Enter or Key.Tab:
                    if (_slashCommandSelectedIndex >= 0 && _slashCommandListBox.SelectedItem is SlashCommand cmd)
                    {
                        vm.ExecuteSlashCommandCommand.Execute(cmd);
                        _slashCommandSelectedIndex = -1;
                        e.Handled = true;
                        return;
                    }
                    break;
                case Key.Escape:
                    vm.ShowSlashCommandMenu = false;
                    _slashCommandSelectedIndex = -1;
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Enter)
        {
            // Bare Enter sends the message; Ctrl+Enter and Shift+Enter insert a newline.
            if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Shift)
            {
                return;
            }
            vm.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SlashCommandListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is SlashCommand cmd && DataContext is AiChatViewModel vm)
        {
            vm.ExecuteSlashCommandCommand.Execute(cmd);
            _slashCommandSelectedIndex = -1;
        }
    }

    private void MentionListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is MentionItem mention && DataContext is AiChatViewModel vm)
        {
            vm.InsertMentionItemCommand.Execute(mention);
            _mentionSelectedIndex = -1;
        }
    }
}

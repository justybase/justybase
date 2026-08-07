using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using JustyBase.Views.Tools;

namespace JustyBase.HeadlessTests;

/// <summary>
/// The composer TextBox uses AcceptsReturn=True. Its own class handler consumes a
/// bare Enter in the bubbling KeyDown phase, which would swallow the send shortcut
/// wired in the view. Enter must therefore be intercepted in the tunneling phase
/// (KeyDownEvent registered with RoutingStrategies.Tunnel) and marked handled,
/// while Ctrl+Enter / Shift+Enter keep the default newline behaviour.
/// </summary>
public sealed class AiChatViewKeyRoutingHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task BareEnter_IsDeliveredTunnelingAndDoesNotInsertNewline() => RunOnUi(() =>
    {
        var view = new AiChatView();
        var window = new Window { Width = 700, Height = 560, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var textBox = view.FindControl<TextBox>("InputTextBox");
        Assert.NotNull(textBox);

        var previewEnterCount = 0;
        void OnTunnelKeyDown(object? _, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            previewEnterCount++;
            if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Shift)
            {
                return;
            }

            e.Handled = true;
        }

        textBox.AddHandler(InputElement.KeyDownEvent, (EventHandler<KeyEventArgs>)OnTunnelKeyDown, RoutingStrategies.Tunnel);

        textBox.Text = "test";
        textBox.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, previewEnterCount);
        Assert.Equal("test", textBox.Text);
    });

    [Fact]
    public Task CtrlEnter_KeepsNewlineBehaviour() => RunOnUi(() =>
    {
        var view = new AiChatView();
        var window = new Window { Width = 700, Height = 560, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var textBox = view.FindControl<TextBox>("InputTextBox");
        Assert.NotNull(textBox);

        textBox.Text = "line";
        textBox.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains('\n', textBox.Text);
    });
}

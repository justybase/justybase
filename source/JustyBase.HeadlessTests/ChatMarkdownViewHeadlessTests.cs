using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using JustyBase.Views.Tools;
using LiveMarkdown.Avalonia;

namespace JustyBase.HeadlessTests;

public sealed class ChatMarkdownViewHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task ChatMarkdownView_RendersRepresentativeAiMarkdown() => RunOnUi(() =>
    {
        var view = new ChatMarkdownView
        {
            Width = 520,
            MarkdownText = "ąęłń **bold** _italic_\n\n- lista\n- druga\n\n> cytat\n\n| SQL | wynik |\n| --- | --- |\n| SELECT 1 | OK |\n\n```sql\nselect 1;\n```\n\n[Dokumentacja](https://example.com/docs)"
        };
        var window = new Window
        {
            Width = 560,
            Height = 520,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.IsType<MarkdownRenderer>(view.Content);
        Assert.Contains("[Dokumentacja](https://example.com/docs)", view.RenderedMarkdown, StringComparison.Ordinal);
        Assert.Contains("select 1;", view.RenderedMarkdown, StringComparison.Ordinal);
    });

    [Fact]
    public Task ChatMarkdownView_FlushesPendingTextWhenStreamingStops() => RunOnUi(() =>
    {
        var view = new ChatMarkdownView
        {
            IsStreaming = true,
            MarkdownText = "wersja początkowa"
        };
        var window = new Window
        {
            Width = 560,
            Height = 220,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.MarkdownText = "wersja końcowa **gotowa**";
        view.IsStreaming = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("wersja końcowa **gotowa**", view.RenderedMarkdown);
    });
}

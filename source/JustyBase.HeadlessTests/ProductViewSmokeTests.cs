using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Views.Documents;
using JustyBase.Views.Tools;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Headless smokes that construct real JustyBase product views (not generic Avalonia controls).
/// </summary>
public sealed class ProductViewSmokeTests : HeadlessSessionTestBase
{
    [Fact]
    public Task SqlDiagnosticsView_CanBeCreatedAndShown() => RunOnUi(() =>
    {
        var view = new SqlDiagnosticsView
        {
            Width = 400,
            Height = 240
        };
        var window = new Window
        {
            Width = 480,
            Height = 320,
            Content = view,
            Title = "SqlDiagnosticsView smoke"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Same(view, window.Content);
    });

    [Fact]
    public Task LogToolView_CanBeCreatedAndShown() => RunOnUi(() =>
    {
        var view = new LogToolView
        {
            Width = 400,
            Height = 240
        };
        var window = new Window
        {
            Width = 480,
            Height = 320,
            Content = view,
            Title = "LogToolView smoke"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.IsType<LogToolView>(window.Content);
    });

    [Fact]
    public Task SqlDocumentView_CanBeConstructedWithMocks() => RunOnUi(() =>
    {
        var view = new SqlDocumentView(
            Mock.Of<IMessageForUserTools>(),
            Mock.Of<ISimpleLogger>());

        Assert.NotNull(view);
        Assert.Null(view.DataContext);

        var window = new Window
        {
            Width = 640,
            Height = 480,
            Content = view,
            Title = "SqlDocumentView smoke"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(view, window.Content);
    });
}

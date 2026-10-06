using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Views.Documents;
using JustyBase.Views.Tools;
using JustyBase.Views.OtherDialogs;
using JustyBase.Services;
using JustyBase.ViewModels.Tools;
using System.Text;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Headless smokes that construct real JustyBase product views (not generic Avalonia controls).
/// </summary>
public sealed class ProductViewSmokeTests : HeadlessSessionTestBase
{
    [Theory]
    [InlineData("files")]
    [InlineData("fileSearch")]
    [InlineData("outline")]
    [InlineData("variables")]
    public Task CompactToolViews_CanBeCreatedAndShown(string kind) => RunOnUi(() =>
    {
        Control view = kind switch
        {
            "files" => new FileExplorerView(),
            "fileSearch" => new FileSearchView(),
            "outline" => new SqlOutlineView(),
            _ => new VariablesView()
        };
        if (view is SqlOutlineView outlineView)
        {
            var outlineViewModel = new SqlOutlineViewModel(Mock.Of<IFactory>());
            outlineViewModel.UpdateOutline("WITH source_rows AS (SELECT 1) SELECT * FROM source_rows;");
            outlineView.DataContext = outlineViewModel;
        }
        var window = new Window { Width = 400, Height = 440, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
    });

    [Fact]
    public Task ReplacementPreview_CanBeCreatedAndShown() => RunOnUi(() =>
    {
        var file = new ContentSearchFile("sample.sql", "sample.sql", "select id", [], Encoding.UTF8,
            false, [new ContentSearchHit(1, 8, 7, 2, "select ", "id", "")]);
        var window = new ReplacePreviewWindow(
            [new ContentReplacement(file, "select customer_id", 1, [new ContentReplacementChange(1, "select id", "select customer_id")])],
            []);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
    });

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

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Tools;
using JustyBase.Views.Tools;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Mounts the real SqlResultsView + SqlResultsViewModel (same wiring as the app)
/// and verifies the Ctrl+F search produces results through the grid search adapter.
/// </summary>
public sealed class SqlResultsFindFlowHeadlessTests : HeadlessSessionTestBase
{
    internal static SqlResultsView CreateView(out SqlResultsViewModel vm)
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());

        vm = new SqlResultsViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IClipboardService>(),
            appData.Object,
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger,
            Mock.Of<IResultGridActionRoutingService>(),
            Mock.Of<IActiveDocumentManager>());

        var table = vm.CurrentResultsTable;
        table.Headers.Add("A");
        table.Headers.Add("B");
        table.TypeCodes = [TypeCode.String, TypeCode.Int32];
        for (int i = 0; i < 100; i++)
        {
            table.Rows.Add(new TableRow { Fields = [$"Name{i}", i] });
        }
        table.FilteredRows.AddRange(table.Rows);
        vm.GridCollectionView = new Avalonia.Collections.DataGridCollectionView(table.FilteredRows);

        var services = new SqlResultsViewServices(
            Mock.Of<ISummaryRowService>(),
            Mock.Of<IResultGridSearchService>(),
            new ResultGridSummaryRefreshService(),
            new ResultGridSummaryScrollService(),
            Mock.Of<IResultGridSelectionService>(),
            Mock.Of<IResultGridDoubleTapService>(),
            Mock.Of<IDataGridClipboardService>(),
            Mock.Of<IResultGridGroupingService>(),
            Mock.Of<IResultGridGroupingDragService>(),
            Mock.Of<IResultGridGroupExpandCollapseService>(),
            Mock.Of<IResultGridStatsService>(),
            new ResultGridKeyboardService(),
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger);

        var view = new SqlResultsView(services)
        {
            DataContext = vm
        };
        return view;
    }

    [Fact]
    public Task FullView_FindText_DebounceTimerFires() => RunWithAsyncUi(async () =>
    {
        var view = CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.IsFindVisible = true;
        vm.FindText = "Name42";

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (vm.FindModel.Results.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(vm.FindModel.Results.Count > 0, $"results: {vm.FindModel.Results.Count}");
        window.Close();
    });

    [Fact]
    public Task FullView_FindText_WithGrouping_FindsResults() => RunWithAsyncUi(async () =>
    {
        var view = CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.GridCollectionView.GroupDescriptions.Add(new DataGridPathGroupDescription("Fields[1]"));
        Dispatcher.UIThread.RunJobs();

        vm.IsFindVisible = true;
        vm.FindText = "Name42";
        vm.RefreshFind();

        Assert.True(vm.FindModel.Results.Count > 0, $"results: {vm.FindModel.Results.Count}");
        window.Close();
    });

    [Fact]
    public Task FullView_FindText_AfterDetachReattach_FindsResults() => RunWithAsyncUi(async () =>
    {
        var view = CreateView(out var vm);
        var window = new Window { Width = 700, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        window.Content = view;
        Dispatcher.UIThread.RunJobs();

        vm.IsFindVisible = true;
        vm.FindText = "Name42";
        vm.RefreshFind();

        Assert.True(vm.FindModel.Results.Count > 0, $"results: {vm.FindModel.Results.Count}");
        window.Close();
    });

    private Task RunWithAsyncUi(Func<Task> action)
    {
        Assert.NotNull(Session);
        return Session!.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
    }
}

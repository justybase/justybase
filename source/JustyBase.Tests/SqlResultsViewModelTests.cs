using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlResultsViewModelTests
{
    [Fact]
    public void Constructor_SetsExpectedDefaults()
    {
        var vm = CreateViewModel();

        Assert.Equal("10", vm.DpWidth);
        Assert.False(vm.VisibleExpand);
        Assert.False(vm.GridVisible);
        Assert.False(vm.ShowSummaryRow);
        Assert.Empty(vm.ColumnSummaries);
        Assert.Empty(vm.GroupedColumns);
        Assert.Empty(vm.RowDetailCollection);
        Assert.NotNull(vm.CurrentResultsTable);
        Assert.NotNull(vm.GridCollectionView);
        Assert.Equal(500, vm.SpillPageSize);
        Assert.False(vm.IsSpillMode);
        Assert.True(vm.IsResultVisible);
        Assert.True(vm.ContainsGeneralSearch);
    }

    [Fact]
    public void SetColumnSummary_TogglesShowSummaryRowAndStoresType()
    {
        var bridge = new Mock<ISqlResultsViewBridge>();
        var vm = CreateViewModel();
        vm.ViewBridge = bridge.Object;

        vm.SetColumnSummary(2, ColumnSummaryType.Sum);

        Assert.True(vm.ShowSummaryRow);
        Assert.Equal(ColumnSummaryType.Sum, vm.GetColumnSummaryType(2));
        bridge.Verify(x => x.RecalculateSummaryValues(), Times.Once);

        vm.SetColumnSummary(2, ColumnSummaryType.None);

        Assert.False(vm.ShowSummaryRow);
        Assert.Equal(ColumnSummaryType.None, vm.GetColumnSummaryType(2));
        bridge.Verify(x => x.RecalculateSummaryValues(), Times.Exactly(2));
    }

    [Fact]
    public void DoCleanup_ClearsCurrentResultsTable()
    {
        var vm = CreateViewModel();
        vm.CurrentResultsTable.Headers.Add("A");
        vm.CurrentResultsTable.FilteredRows.Add(new TableRow { Fields = [1] });
        Assert.NotEmpty(vm.CurrentResultsTable.FilteredRows);

        vm.DoCleanup();

        Assert.Empty(vm.CurrentResultsTable.FilteredRows);
        Assert.Empty(vm.CurrentResultsTable.Rows);
    }

    [Fact]
    public void DpWidth_AboveThreshold_SetsVisibleExpand()
    {
        var vm = CreateViewModel();

        vm.DpWidth = "200";
        Assert.True(vm.VisibleExpand);

        vm.DpWidth = "10";
        Assert.False(vm.VisibleExpand);
    }

    [Fact]
    public void ExpandCollapseRowViewCommand_TogglesWidthAndVisibleExpand()
    {
        var vm = CreateViewModel();
        Assert.False(vm.VisibleExpand);
        Assert.Equal("10", vm.DpWidth);

        Assert.True(vm.ExpandCollapseRowViewCommand.CanExecute(null));
        vm.ExpandCollapseRowViewCommand.Execute(null);

        Assert.True(vm.VisibleExpand);
        Assert.Equal("200", vm.DpWidth);

        vm.ExpandCollapseRowViewCommand.Execute(null);

        Assert.False(vm.VisibleExpand);
        Assert.Equal("10", vm.DpWidth);
    }

    private static SqlResultsViewModel CreateViewModel()
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());
        return new SqlResultsViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IClipboardService>(),
            appData.Object,
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger,
            Mock.Of<IResultGridActionRoutingService>(),
            Mock.Of<IActiveDocumentManager>());
    }
}

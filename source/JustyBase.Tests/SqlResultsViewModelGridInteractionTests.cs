using System.ComponentModel;
using Avalonia.Controls;
using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Tools;
using Moq;

namespace JustyBase.Tests;

/// <summary>
/// Covers the grid-interaction logic that was moved from <c>SqlResultsView.axaml.cs</c>
/// into <c>SqlResultsViewModel.GridInteraction.cs</c>. The view is now only a thin
/// adapter, so this behaviour must be protected by tests here.
/// </summary>
public sealed class SqlResultsViewModelGridInteractionTests
{
    // ------------------------------------------------------------------
    // Selection statistics
    // ------------------------------------------------------------------

    [Fact]
    public void SetSingleCellSelectionSummary_ClearsCellsAndSetsText()
    {
        var vm = CreateViewModel();
        vm.SelectedColumnCells = [42];
        vm.StatsText = "stale";

        vm.SetSingleCellSelectionSummary();

        Assert.Empty(vm.SelectedColumnCells);
        Assert.Equal("Selected 1 cell", vm.StatsText);
    }

    [Fact]
    public void UpdateSelectionStats_WithNullTable_SetsZeroState()
    {
        var vm = CreateViewModel();
        vm.CurrentResultsTable = null!;
        var statsService = new Mock<IResultGridStatsService>();

        vm.UpdateSelectionStats([], statsService.Object);

        Assert.Empty(vm.SelectedColumnCells);
        Assert.StartsWith("Selected 0 cells", vm.StatsText);
        statsService.Verify(
            x => x.CalculateStats(It.IsAny<IReadOnlyList<DataGridCellInfo>>(), It.IsAny<TableOfSqlResults>()),
            Times.Never);
    }

    [Fact]
    public void UpdateSelectionStats_WithTable_DelegatesToStatsService()
    {
        var vm = CreateViewModel();
        var statsService = new Mock<IResultGridStatsService>();
        var expected = new CellStatsResult
        {
            SelectedCount = 3,
            Sum = 6m,
            NotNullCount = 3,
            DistinctCount = 2,
            Min = 1m,
            Max = 3m,
            SelectedValues = [1, 2, 3],
        };
        statsService
            .Setup(x => x.CalculateStats(It.IsAny<IReadOnlyList<DataGridCellInfo>>(), vm.CurrentResultsTable))
            .Returns(expected);

        vm.UpdateSelectionStats([], statsService.Object);

        Assert.Equal(new object[] { 1, 2, 3 }, vm.SelectedColumnCells);
        Assert.Equal(expected.ToDisplayString(), vm.StatsText);
    }

    // ------------------------------------------------------------------
    // Filter / search
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyFilterSearch_WithEmptyTable_DoesNotTouchBridge()
    {
        var vm = CreateViewModel();
        var bridge = new Mock<ISqlResultsViewBridge>();
        vm.ViewBridge = bridge.Object;
        var searchService = new Mock<IResultGridSearchService>();

        vm.ApplyFilterSearch(searchService.Object);

        bridge.Verify(x => x.SuspendGridBinding(), Times.Never);
        searchService.Verify(
            x => x.ApplySearch(It.IsAny<TableOfSqlResults>(), It.IsAny<string>(), It.IsAny<Dictionary<int, AditionalOneFilter>>(), It.IsAny<bool>()),
            Times.Never);
        Assert.False(vm.SearchInProgress);
    }

    [Fact]
    public void ApplyFilterSearch_WithRows_SearchesAndRebuildsView()
    {
        var vm = CreateViewModel();
        SeedTable(vm.CurrentResultsTable, ["A"]);
        var bridge = new Mock<ISqlResultsViewBridge>();
        vm.ViewBridge = bridge.Object;
        var searchService = new Mock<IResultGridSearchService>();
        vm.SearchText = "A";

        vm.ApplyFilterSearch(searchService.Object);

        searchService.Verify(
            x => x.ApplySearch(vm.CurrentResultsTable, "A", null, vm.ContainsGeneralSearch),
            Times.Once);
        bridge.Verify(x => x.SuspendGridBinding(), Times.Once);
        bridge.Verify(x => x.ResumeGridBinding(), Times.Once);
        bridge.Verify(x => x.RecalculateSummaryValues(), Times.Once);
        bridge.Verify(x => x.InvalidateSummaryLayout(), Times.Once);
        Assert.False(vm.SearchInProgress);
        Assert.Contains("rows", vm.RowsLoadingMessage);
    }

    // ------------------------------------------------------------------
    // Sorting
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyColumnSort_SortsFilteredRowsAndRebuildsView()
    {
        var vm = CreateViewModel();
        SeedTable(vm.CurrentResultsTable, ["N"], TypeCode.Int32);
        vm.CurrentResultsTable.FilteredRows[0].Fields[0] = 3;
        vm.CurrentResultsTable.FilteredRows[1].Fields[0] = 1;
        vm.CurrentResultsTable.FilteredRows[2].Fields[0] = 2;
        var bridge = new Mock<ISqlResultsViewBridge>();
        vm.ViewBridge = bridge.Object;
        var comparer = new CustomResultComparer(TypeCode.Int32, 0);

        vm.ApplyColumnSort(comparer, ListSortDirection.Ascending);

        Assert.Single(vm.CurrentResultsTable.ColumnsToSort);
        Assert.Equal(0, vm.CurrentResultsTable.ColumnsToSort[0].ColNumber);
        Assert.Equal(ListSortDirection.Ascending, vm.CurrentResultsTable.ColumnsToSort[0].SortDirection);
        Assert.Equal([1, 2, 3], vm.CurrentResultsTable.FilteredRows.Select(r => (int)r.Fields[0]));
        bridge.Verify(x => x.SuspendGridBinding(), Times.Once);
        bridge.Verify(x => x.ResumeGridBinding(), Times.Once);
    }

    [Fact]
    public void ApplyColumnSort_WithDescending_ReversesOrder()
    {
        var vm = CreateViewModel();
        SeedTable(vm.CurrentResultsTable, ["N"], TypeCode.Int32);
        vm.CurrentResultsTable.FilteredRows[0].Fields[0] = 3;
        vm.CurrentResultsTable.FilteredRows[1].Fields[0] = 1;
        vm.CurrentResultsTable.FilteredRows[2].Fields[0] = 2;
        var bridge = new Mock<ISqlResultsViewBridge>();
        vm.ViewBridge = bridge.Object;
        var comparer = new CustomResultComparer(TypeCode.Int32, 0);

        vm.ApplyColumnSort(comparer, ListSortDirection.Descending);

        Assert.Equal([3, 2, 1], vm.CurrentResultsTable.FilteredRows.Select(r => (int)r.Fields[0]));
    }

    // ------------------------------------------------------------------
    // Grouping
    // ------------------------------------------------------------------

    [Fact]
    public void ToggleGroupByColumn_AddsThenRemovesGroupedColumn()
    {
        var vm = CreateViewModel();
        SeedTable(vm.CurrentResultsTable, ["Id", "City"]);
        vm.GridCollectionView = new Avalonia.Collections.DataGridCollectionView(vm.CurrentResultsTable.FilteredRows);
        var groupingService = new ResultGridGroupingService();

        vm.ToggleGroupByColumn("City", groupingService);

        Assert.Contains("City", vm.GroupedColumns);
        Assert.Single(vm.GridCollectionView.GroupDescriptions);

        vm.ToggleGroupByColumn("City", groupingService);

        Assert.Empty(vm.GroupedColumns);
        Assert.Empty(vm.GridCollectionView.GroupDescriptions);
    }

    [Fact]
    public void ToggleGroupByColumn_WithUnknownColumn_DoesNothing()
    {
        var vm = CreateViewModel();
        SeedTable(vm.CurrentResultsTable, ["Id", "City"]);
        var groupingService = new ResultGridGroupingService();

        vm.ToggleGroupByColumn("Missing", groupingService);

        Assert.Empty(vm.GroupedColumns);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void SeedTable(TableOfSqlResults table, string[] headers, TypeCode typeCode = TypeCode.String)
    {
        table.Headers = [.. headers];
        table.TypeCodes = [.. headers.Select(_ => typeCode)];
        table.Rows = [];
        for (int i = 0; i < 3; i++)
        {
            table.Rows.Add(new TableRow { Fields = headers.Select(_ => (object)i).ToArray() });
        }
        table.FilteredRows = new BulkObservableCollection<TableRow>(table.Rows);
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
            Mock.Of<IActiveDocumentManager>(),
            new DataGridClipboardService());
    }
}

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Guards the Ctrl+F search adapter integration: result-grid columns must expose
/// a search member path (Fields[i]) or the adapter silently skips them and the
/// search returns no results.
/// </summary>
public sealed class SearchModelIntegrationHeadlessTests : HeadlessSessionTestBase
{
    private static TableOfSqlResults CreateTable()
    {
        var table = new TableOfSqlResults();
        table.Headers.Add("A");
        table.Headers.Add("B");
        table.TypeCodes = [TypeCode.String, TypeCode.Int32];
        for (int i = 0; i < 100; i++)
        {
            table.Rows.Add(new TableRow { Fields = [$"Name{i}", i] });
        }
        table.FilteredRows.AddRange(table.Rows);
        return table;
    }

    private static DataGrid CreateGrid(TableOfSqlResults table)
    {
        var grid = new DataGrid
        {
            ItemsSource = new DataGridCollectionView(table.FilteredRows),
            AutoGenerateColumns = false,
            IsReadOnly = true,
            RowHeight = 22
        };

        var converters = new List<Avalonia.Data.Converters.IValueConverter>();
        for (int i = 0; i < table.Headers.Count; i++)
        {
            grid.Columns.Add(ResultGridColumnFactory.CreateColumn(table, i, null!, new Dictionary<string, int>(), converters));
        }

        grid.SearchModel = new SearchModel
        {
            HighlightMode = SearchHighlightMode.TextAndCell,
            HighlightCurrent = true,
            WrapNavigation = true
        };
        return grid;
    }

    private static void RunSearch(DataGrid grid, string query)
    {
        grid.SearchModel.SetOrUpdate(new SearchDescriptor(
            query: query,
            matchMode: SearchMatchMode.Contains,
            termMode: SearchTermCombineMode.Any,
            scope: SearchScope.AllColumns,
            comparison: StringComparison.OrdinalIgnoreCase,
            wholeWord: false,
            normalizeWhitespace: true,
            ignoreDiacritics: true));
    }

    [Fact]
    public Task Search_WithFactoryColumns_FindsTextMatch() => RunOnUi(() =>
    {
        var grid = CreateGrid(CreateTable());
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        RunSearch(grid, "Name42");

        Assert.True(grid.SearchModel.Results.Count > 0, $"results: {grid.SearchModel.Results.Count}");
        Assert.Equal(42, grid.SearchModel.Results[0].RowIndex);
        window.Close();
    });

    [Fact]
    public Task Search_WithFactoryColumns_FindsNumericMatch() => RunOnUi(() =>
    {
        var grid = CreateGrid(CreateTable());
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        RunSearch(grid, "7");

        Assert.True(grid.SearchModel.Results.Count >= 19, $"results: {grid.SearchModel.Results.Count}");
        window.Close();
    });
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Minimal reproduction of result-grid selection after replacing an ItemsSource.
/// Intentionally uses only ProDataGrid, a SelectedIndex binding, and plain rows.
/// </summary>
public sealed class ProDataGridSelectionReproHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task DataGrid_SelectsFirstRowAfterItemsSourceRebindWithoutSelectedIndexBinding() => RunWithAsyncUi(async () =>
    {
        var grid = CreateGrid(CreateView(1));
        var window = new Window { Width = 320, Height = 180, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(grid.SelectedIndex == -1, $"SelectedIndex after initial show: {grid.SelectedIndex}.");

        grid.IsVisible = false;
        grid.SelectedIndex = -1;
        grid.ItemsSource = CreateView(10);
        grid.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, grid.SelectedIndex);
        Assert.Equal(10, Assert.IsType<ReproRow>(grid.SelectedItem).Id);
        Assert.Single(grid.SelectedItems);
        Assert.Empty(grid.SelectedCells);
        window.Close();
        await Task.CompletedTask;
    });

    [Fact]
    public Task DataGrid_SelectionCanBeClearedAfterItemsSourceRebind() => RunWithAsyncUi(async () =>
    {
        var selection = new SelectionState();
        var grid = CreateGrid(CreateView(1));
        grid.Bind(DataGrid.SelectedIndexProperty, new Binding(nameof(SelectionState.SelectedIndex))
        {
            Source = selection,
            Mode = BindingMode.TwoWay
        });
        var window = new Window { Width = 320, Height = 180, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        grid.IsVisible = false;
        grid.ItemsSource = CreateView(10);
        selection.SelectedIndex = -1;
        grid.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(-1, grid.SelectedIndex);
        Assert.Null(grid.SelectedItem);
        Assert.Empty(grid.SelectedItems);
        Assert.Empty(grid.SelectedCells);
        window.Close();
        await Task.CompletedTask;
    });

    [Fact]
    public Task DataGrid_SelectsFirstRowAfterItemsSourceRebindWithSelectedIndexBinding() => RunWithAsyncUi(async () =>
    {
        var selection = new SelectionState();
        var grid = CreateGrid(CreateView(1));
        grid.Bind(DataGrid.SelectedIndexProperty, new Binding(nameof(SelectionState.SelectedIndex))
        {
            Source = selection,
            Mode = BindingMode.TwoWay
        });

        var window = new Window { Width = 320, Height = 180, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AssertNoSelection(grid, selection, "initial show");

        grid.IsVisible = false;
        selection.SelectedIndex = -1;
        grid.ItemsSource = CreateView(10);
        grid.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, grid.SelectedIndex);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Equal(10, Assert.IsType<ReproRow>(grid.SelectedItem).Id);
        Assert.Single(grid.SelectedItems);
        Assert.Empty(grid.SelectedCells);
        window.Close();
        await Task.CompletedTask;
    });

    private static DataGridCollectionView CreateView(int startingId) => new(
        new ObservableCollection<ReproRow>(
        [
            new ReproRow(startingId),
            new ReproRow(startingId + 1),
            new ReproRow(startingId + 2)
        ]));

    private static DataGrid CreateGrid(DataGridCollectionView source)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            ItemsSource = source
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Id",
            Binding = new Binding(nameof(ReproRow.Id))
        });
        return grid;
    }

    private static void AssertNoSelection(DataGrid grid, SelectionState selection, string phase)
    {
        Assert.True(grid.SelectedIndex == -1,
            $"SelectedIndex after {phase}: grid={grid.SelectedIndex}, viewModel={selection.SelectedIndex}.");
        Assert.Null(grid.SelectedItem);
        Assert.Empty(grid.SelectedItems);
        Assert.Empty(grid.SelectedCells);
    }

    private Task<bool> RunWithAsyncUi(Func<Task> action)
    {
        Assert.NotNull(Session);
        return Session!.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
    }

    private sealed class SelectionState : INotifyPropertyChanged
    {
        private int _selectedIndex = -1;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value)
                {
                    return;
                }

                _selectedIndex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
            }
        }
    }

    private sealed record ReproRow(int Id);
}

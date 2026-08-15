// =============================================================================
// TECHNICAL DEBT NOTICE - SqlResultsView.axaml.cs (~1600 lines)
// =============================================================================
// This file handles multiple responsibilities that should be refactored:
//
// 1. HEADER TEMPLATE (lines ~1360-1390) - ~30 lines
//    - GetHeaderTemplate now uses ColumnHeaderFactory
//    - Factory uses Xaml.Behaviors.Avalonia for drag-drop
//    - ColumnHeaderDragBehavior, ColumnDropBehavior, ColumnPinBehavior
//
// 2. GROUPING FUNCTIONALITY (lines ~540-700, ~1400-1500)
//    - Drag-drop for column grouping
//    - Group header management
//
// 3. SUMMARY ROW (lines ~180-350)
//    - Summary display and scroll sync
//    - Uses ISummaryRowService for calculations
//
// 4. SEARCH/FILTER (lines ~1200-1270)
//    - Search box functionality
//    - Filter logic
//
// REFACTORING PROGRESS:
// ✅ ISummaryRowService extracted and used
// ✅ Removed dead rectangular selection code
// ✅ Removed commented code
// ✅ Header Template extracted to ColumnHeaderFactory
// ✅ Xaml.Behaviors.Avalonia integrated for drag-drop
// ✅ File reduced from ~2434 -> 1971 -> 1603 lines
//
// PRIORITY: Low - Functionality works well, refactoring is for maintainability
// =============================================================================

using Avalonia.Collections;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Behaviors;
using JustyBase.Converters;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Tools;
using System.ComponentModel;

namespace JustyBase.Views.Tools;

public sealed partial class SqlResultsView : UserControl, ISqlResultsViewBridge
{
    private readonly SummaryRowPresenter _summaryRowPresenter;
    private readonly IResultGridSearchService _searchService;
    private readonly IResultGridSummaryRefreshService _summaryRefreshService;
    private readonly IResultGridSummaryScrollService _summaryScrollService;
    private readonly IResultGridSelectionService _selectionService;
    private readonly IResultGridDoubleTapService _doubleTapService;
    private readonly IDataGridClipboardService _clipboardService;
    private readonly IResultGridGroupingService _groupingService;
    private readonly IResultGridGroupingDragService _groupingDragService;
    private readonly IResultGridGroupExpandCollapseService _groupExpandCollapseService;
    private readonly IResultGridStatsService _statsService;
    private readonly IResultGridKeyboardService _keyboardService;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;
    private readonly Dictionary<int, double> _lastColumnWidths = [];
    private SqlResultsViewModel? _boundViewModel;

    public SqlResultsView(ISqlResultsViewServices services)
    {
        _summaryRowPresenter = new SummaryRowPresenter(services.SummaryRowService);
        _searchService = services.SearchService;
        _summaryRefreshService = services.SummaryRefreshService;
        _summaryScrollService = services.SummaryScrollService;
        _selectionService = services.SelectionService;
        _doubleTapService = services.DoubleTapService;
        _clipboardService = services.ClipboardService;
        _groupingService = services.GroupingService;
        _groupingDragService = services.GroupingDragService;
        _groupExpandCollapseService = services.GroupExpandCollapseService;
        _statsService = services.StatsService;
        _keyboardService = services.KeyboardService;
        _messageForUserTools = services.MessageForUserTools;
        _simpleLogger = services.SimpleLogger;
        InitializeComponent();
        ResultDataGrid.Initialized += DataGrid_Initialized;
        ResultDataGrid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        Initialized += SqlResultsView_Initialized;
        DataContextChanged += SqlResultsView_DataContextChanged;
        DetachedFromVisualTree += SqlResultsView_DetachedFromVisualTree;
        rowDetailsDataGrid.Initialized += RowDetailsDataGrid_Initialized;

        ResultDataGrid.KeyDown += DataGrid_KeyDown; // Handle grid keyboard shortcuts
        ResultDataGrid.SelectionChanged += DataGrid_SelectionChanged;
        ResultDataGrid.DoubleTapped += DataGrid_DoubleTapped;
        this.CopySelectionWithHeadersOptions.Command = new RelayCommand(CopyWithHeadersAsPlainText);
        ConfigureColumnAutocomplete();
        ResultDataGrid.LoadingRow += DataGrid_LoadingRow;
        ResultDataGrid.Sorting += ResultDataGrid_Sorting;
        ResultDataGrid.LoadingRowGroup += DataGrid_LoadingRowGroup;
        rowDetailsDataGrid.DoubleTapped += DataGrid_DoubleTapped;
        TriggerSearchTimerCommand = new RelayCommand(TriggerSearchTimer);

        ConfigureGroupingPanelDropBehavior();
    }

    private void ConfigureColumnAutocomplete()
    {
        if (columnAutoComplet is null)
        {
            return;
        }

        columnAutoComplet.GotFocus += (_, _) => EnsureColumnAutocompleteDropDown();
        columnAutoComplet.ItemFilter = new AutoCompleteFilterPredicate<object>((x, y) => FilterColumnAutocomplete(x, y));
    }

    private static bool FilterColumnAutocomplete(string typedValue, object item)
    {
        if (string.IsNullOrWhiteSpace(typedValue))
        {
            return true;
        }

        return item.ToString().Contains(typedValue.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureColumnAutocompleteDropDown()
    {
        if (columnAutoComplet is null || !string.IsNullOrWhiteSpace(columnAutoComplet.Text))
        {
            return;
        }

        columnAutoComplet.Text = " ";
        columnAutoComplet.IsDropDownOpen = true;
    }

    private void ConfigureGroupingPanelDropBehavior()
    {
        var groupingPanel = this.FindControl<Border>("GroupingPanelBorder");
        if (groupingPanel == null)
        {
            return;
        }

        var dropBehavior = new Behaviors.GroupingPanelDropBehavior
        {
            ColumnNameDataFormat = _columnNameDataFormat,
            GroupByColumnAction = OnGroupByColumnRequested
        };
        Avalonia.Xaml.Interactivity.Interaction.GetBehaviors(groupingPanel).Add(dropBehavior);
    }

    private void OnGroupByColumnRequested(string columnName)
    {
        if (DataContext is SqlResultsViewModel vm && !vm.GroupedColumns.Contains(columnName))
        {
            GroupByOneColumn(columnName);
        }
    }

    public Action RecalculateSummaryValuesAction => RefreshSummaryRowWidths;

    /// <summary>
    /// Invalidates cached summary alignment measurements after the data or column layout changed.
    /// </summary>
    public void InvalidateSummaryLayout()
    {
        _summaryScrollService.InvalidateRowHeaderWidthCache();
    }

    /// <summary>
    /// Refreshes the summary row widths to match current column widths
    /// </summary>
    private void RefreshSummaryRowWidths()
    {
        if (DataContext is SqlResultsViewModel vm &&
            _summaryRefreshService.ShouldRefreshSummaryRow(vm.ShowSummaryRow, ResultDataGrid.Columns.Count))
        {
            RecalculateSummaryValues();
        }
    }

    private ScrollViewer? _summaryScrollViewer;

    private void SetupSummaryRowSync()
    {
        _summaryScrollViewer = this.FindControl<ScrollViewer>("SummaryScrollViewer");

        // Initialize the behavior with the scroll service
        var behavior = Avalonia.Xaml.Interactivity.Interaction.GetBehaviors(ResultDataGrid)
            .OfType<SummaryRowSyncBehavior>()
            .FirstOrDefault();
            
        behavior?.Initialize(_summaryScrollService);
        
        RefreshSummaryRowWidths();
    }



    /// <summary>
    /// Recalculates summary values for all columns that have summaries enabled
    /// </summary>
    private void RecalculateSummaryValues()
    {
        if (DataContext is not SqlResultsViewModel vm || CurrentResultsTable?.FilteredRows == null)
            return;

        var summaryPanel = this.FindControl<StackPanel>("SummaryRowPanel");
        if (summaryPanel == null || ResultDataGrid.Columns.Count == 0)
            return;

        double spacerWidth = _summaryScrollService.GetFirstColumnSpacerWidth(ResultDataGrid, _summaryScrollViewer);

        // Summaries reflect the currently visible rows (filtered collection view).
        var visibleRows = GridCollectionView.Cast<object>().OfType<TableRow>().ToList();

        _summaryRowPresenter.BuildSummaryRow(
            summaryPanel,
            ResultDataGrid.Columns,
            CurrentResultsTable,
            visibleRows,
            vm.ColumnSummaries,
            spacerWidth);

        // Update SummaryRowValues for binding (still used for visibility)
        vm.SummaryRowValues = new ObservableCollection<string>();

        // Also update group header summaries if grouped
        if (_summaryRefreshService.ShouldRefreshGroupHeaderSummaries(vm.ColumnSummaries.Count, GetGroupCount()))
        {
            _summaryRowPresenter.UpdateGroupHeaderSummaries(
                ResultDataGrid,
                CurrentResultsTable,
                GridCollectionView,
                vm.ColumnSummaries);
        }

        // Behavior handles scrolling
        // SyncSummaryRowScroll(GetCurrentGridHorizontalOffset());
    }

    private int GetGroupCount()
    {
        return GridCollectionView.Groups?.Count ?? 0;
    }




    public void MoveGroup(string sourceColName, string targetColName)
    {
        var groupedPropertyNames = GridCollectionView.GroupDescriptions
            .Select(static gd => gd.PropertyName)
            .ToList();
        if (_groupingService.TryFindMoveIndexes(
            groupedPropertyNames,
            CurrentResultsTable.Headers,
            sourceColName,
            targetColName,
            out int sourceIndex,
            out int targetIndex))
        {
            var item = GridCollectionView.GroupDescriptions[sourceIndex];
            GridCollectionView.GroupDescriptions.RemoveAt(sourceIndex);
            GridCollectionView.GroupDescriptions.Insert(targetIndex, item);

            RefreshGroupedColumnsState();
             
            // Refresh summary row layout after group reordering
            Dispatcher.UIThread.Post(() =>
            {
                RefreshSummaryRowWidths();
            }, DispatcherPriority.Input);
        }
    }

    private void SqlResultsView_Initialized(object? sender, EventArgs e)
    {
        BindViewBridge();
        // Setup summary row scroll sync behavior
        // do not remove this, the behavior is responsible for syncing scroll of summary row and grid, and it needs the view bridge to access scroll viewers
        SetupSummaryRowSync();
        _flyoutOnControls = new Dictionary<string, (Control, string)>()
        {
            { "CopyAsCsvClipboard|button", (copyClipboardBt, "Copied")},
            { "CopyAsExcelFileClipboard|button", (copyXlsToClipboard, "Copied")},
            { "OpenAsExcelFileClipboard|button",(openAsXlsx, "Opening started") },
            { "SaveAsExcelFile|button",(openAsXlsx, "Saved") },
            { "CopyAsHtml|button",(copyAsHtml, "Copied") },
            { "ERROR", (this, "Error") }
        };

        ShowFlyoutCommand = new RelayCommand<string>((x) =>
        {
            if (_flyoutOnControls.TryGetValue(x, out var flyout))
            {
                ShowCopiedFlyout(flyout.Item1, flyout.Item2);
            }
        });


        ChangeColumVisiblityCommand = new RelayCommand<string>((colname) =>
        {
            foreach (var item in ResultDataGrid.Columns)
            {
                if (item.Header.ToString() == colname)
                {
                    _messageForUserTools.DispatcherActionInstance(() => item.IsVisible = !item.IsVisible);
                    return;
                }
            }
        });
    }

    private void SqlResultsView_DataContextChanged(object? sender, EventArgs e)
    {
        BindViewBridge();
    }

    private void SqlResultsView_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnbindViewBridge();
    }

    private void BindViewBridge()
    {
        var currentViewModel = DataContext as SqlResultsViewModel;
        if (!ReferenceEquals(_boundViewModel, currentViewModel))
        {
            if (_boundViewModel is not null)
            {
                _boundViewModel.FilteringModel.FilteringChanged -= OnFilteringChanged;
                if (ReferenceEquals(_boundViewModel.ViewBridge, this))
                {
                    _boundViewModel.ViewBridge = null;
                }
            }

            _boundViewModel = currentViewModel;
        }

        if (currentViewModel is not null)
        {
            currentViewModel.ViewBridge = this;
            currentViewModel.FilteringModel.FilteringChanged += OnFilteringChanged;
        }
    }

    private void UnbindViewBridge()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.FilteringModel.FilteringChanged -= OnFilteringChanged;
            if (ReferenceEquals(_boundViewModel.ViewBridge, this))
            {
                _boundViewModel.ViewBridge = null;
            }
        }

        _boundViewModel = null;
    }

    private void OnFilteringChanged(object? sender, FilteringChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not SqlResultsViewModel vm)
            {
                return;
            }

            rowsLoadingMessage.Text = $"{GridCollectionView.Count:N0} rows";
            RefreshSummaryRowWidths();
            vm.RefreshFind();
        }, DispatcherPriority.Background);
    }

    private Dictionary<string, (Control, string)> _flyoutOnControls;

    private void DataGridDoubleClicked(object data, bool rawMode)
    {
        GridDoubleClickCommand?.Execute(new GridDoubleClickArg(data, rawMode));
    }


    private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedCellsCount = ResultDataGrid.SelectedCells?.Count ?? 0;
        var selectionPlan = _selectionService.BuildSelectionChangePlan(
            selectedCellsCount,
            ResultDataGrid.SelectedItems.Count,
            _prevSelectedCount);
        GridSelectionChangedCommand?.Execute(selectionPlan.StatusMessage);

        if (selectionPlan.IsSingleCellSelection)
        {
            SelectedColumnCells = [];
            StatsText = "Selected 1 cell";
            return;
        }

        _prevSelectedCount = selectionPlan.UpdatedPreviousSelectedCount;
        if (selectionPlan.ShouldRefreshRowDetails)
        {
            RefreshRowView();
        }

        TriggerStatsUpdate();
    }
    private int _prevSelectedCount = -1;

    private Flyout _copiedNoticeFlyout;

    private void ShowCopiedFlyout(Control host, string message = "Copied!", bool fail = false)
    {
        if (_copiedNoticeFlyout == null)
        {
            _copiedNoticeFlyout = new Flyout
            {
                Content = new TextBlock
                {
                    Text = message,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                }
            };
        }
        else
        {
            var tb = _copiedNoticeFlyout.Content as TextBlock;
            tb.Text = message;
        }

        if (fail)
        {
            _copiedNoticeFlyout.FlyoutPresenterClasses.Add("Fail");
        }
        else
        {
            _copiedNoticeFlyout.FlyoutPresenterClasses.Remove("Fail");
        }

        _copiedNoticeFlyout.ShowAt(host);


        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(2000);
                _copiedNoticeFlyout.Hide();
            }
            catch (ObjectDisposedException ex)
            {
                _simpleLogger.TrackError(ex, isCrash: false);
            }
            catch (InvalidOperationException ex)
            {
                _simpleLogger.TrackError(ex, isCrash: false);
            }
        }, priority: DispatcherPriority.Background);
    }

    private static readonly Thickness RowHeaderMargin = new(5, 0, 5, 0);

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Header is TextBlock tb)
        {
            tb.Text = (e.Row.Index + 1).ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        }
        else
        {
            e.Row.Header = new TextBlock()
            {
                Text = (e.Row.Index + 1).ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
                Margin = RowHeaderMargin,
                FontSize = 12
            };
        }

        // Stripe by data index — :nth-child breaks under row virtualization/recycling.
        e.Row.Classes.Set("odd-row", e.Row.Index % 2 == 1);
    }

    private void TriggerStatsUpdate()
    {
        if (CurrentResultsTable is null)
        {
            return;
        }

        _statsService.ScheduleStatsUpdate(UpdateSelectedCellsStats);
    }

    private void UpdateSelectedCellsStats()
    {
        var currentResultsTable = CurrentResultsTable;
        if (currentResultsTable is null)
        {
            SelectedColumnCells = [];
            StatsText = "Selected 0 cells | Sum 0.000 | Count 0 | Distinct 0 | Min - | Max -";
            return;
        }

        var selectedCells = ResultDataGrid.SelectedCells?.OfType<DataGridCellInfo>().ToList() ?? [];
        var result = _statsService.CalculateStats(selectedCells, currentResultsTable);

        SelectedColumnCells = result.SelectedValues;
        StatsText = result.ToDisplayString();
    }

    private void RowDetailsDataGrid_Initialized(object? sender, EventArgs e)
    {
        if (CurrentResultsTable is null)
            return;
        RefreshRowView();
    }
    private void RefreshRowView()
    {
        while (rowDetailsDataGrid.Columns.Count > 3)
        {
            rowDetailsDataGrid.Columns.RemoveAt(2);
        }

        int selectedCount = _selectionService.GetRowDetailValueColumnCount(ResultDataGrid.SelectedItems.Count);
        if (selectedCount > 0)
        {
            for (int i = 0; i < selectedCount; i++)
            {
                int savedI = i;
                var valCol = new DataGridTextColumn()
                {
                    Header = $"Value {savedI + 1}",
                    MaxWidth = 600,
                    Width = DataGridLength.Auto,
                    Binding = new Binding($"{nameof(RowDetail.FieldsValues)}[{savedI}]")
                    {
                        Mode = BindingMode.OneWay
                    },
                    IsReadOnly = true,
                    CanUserSort = true,
                    CanUserResize = true
                };
                rowDetailsDataGrid.Columns.Insert(rowDetailsDataGrid.Columns.Count - 1, valCol);
            }
        }
    }

    private void DataGrid_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var action = _keyboardService.ParseKeyDown(e.Key, e.KeyModifiers);
        if (action == ResultGridKeyboardAction.Copy)
        {
            CopyWithHeadersAsPlainText();
            e.Handled = true;
        }
        else if (action == ResultGridKeyboardAction.CopyAll)
        {
            e.Handled = true;
            _ = Dispatcher.UIThread.InvokeAsync(CopyAllRowsToClipboardAsync, DispatcherPriority.Background);
        }
        else if (action == ResultGridKeyboardAction.Find)
        {
            ShowFindBar();
            e.Handled = true;
        }
        else if (action is ResultGridKeyboardAction.FindNext or ResultGridKeyboardAction.FindPrevious)
        {
            if (DataContext is not SqlResultsViewModel findVm)
            {
                return;
            }
            if (!findVm.IsFindVisible)
            {
                findVm.IsFindVisible = true;
            }
            if (action == ResultGridKeyboardAction.FindNext)
            {
                findVm.FindNextCommand.Execute(null);
            }
            else
            {
                findVm.FindPreviousCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void ShowFindBar()
    {
        if (DataContext is not SqlResultsViewModel findVm)
        {
            return;
        }
        findVm.IsFindVisible = true;
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            findTextBox?.Focus();
            findTextBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async Task CopyAllRowsToClipboardAsync()
    {
        try
        {
            var columnHeaders = ResultDataGrid.Columns.Select(c => c.Header?.ToString() ?? "").ToList();
            string text = await _clipboardService.BuildAllRowsTextAsync(CurrentResultsTable, columnHeaders);
            if (!string.IsNullOrEmpty(text) && DataContext is SqlResultsViewModel vm)
            {
                await vm.Clipboard?.SetTextAsync(text);
            }
        }
        catch
        {
        }
    }

    private void CopyWithHeadersAsPlainText()
    {
        // Prefer ProDataGrid native copy for cell selection ranges.
        if (ResultDataGrid.SelectedCells?.Count > 0)
        {
            var prevCopyMode = ResultDataGrid.ClipboardCopyMode;
            try
            {
                ResultDataGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                if (ResultDataGrid.CopySelectionToClipboard(DataGridClipboardExportFormat.Text))
                {
                    return;
                }
            }
            finally
            {
                ResultDataGrid.ClipboardCopyMode = prevCopyMode;
            }
        }

        string text;
        var selectedItems = ResultDataGrid.SelectedItems;
        var columnHeaders = ResultDataGrid.Columns.Select(c => c.Header?.ToString() ?? "").ToList();

        if (selectedItems.Count > 1)
        {
            text = _clipboardService.BuildMultiRowText(columnHeaders, selectedItems);
        }
        else if (ResultDataGrid.SelectedItem is TableRow tableRow)
        {
            var header = ResultDataGrid.CurrentColumn.Header?.ToString();
            text = header is not null
                ? _clipboardService.BuildSingleCellText(tableRow, header, CurrentResultsTable)
                : string.Empty;
        }
        else
        {
            text = string.Empty;
        }
        Copy1Command.Execute(text);
    }




    private DataGridCollectionView GridCollectionView => this.ResultDataGrid.ItemsSource as DataGridCollectionView;

    private void TriggerSearchTimer()
    {
        if (DataContext is SqlResultsViewModel vm && !vm.SearchInProgress)
        {
            _searchService.ScheduleSearch(MakeSearch);
        }
    }

    private bool? ContainsGeneralSearch => generalSearchToggle.GetValue(ToggleSwitch.IsCheckedProperty);//generalSearchToggle.IsChecked;
    private string SearchText => searchBox.GetValue(TextBox.TextProperty);// searchBox.Text;
    private void MakeSearch()
    {
        if (DataContext is not SqlResultsViewModel vm
            || vm.SearchInProgress
            || CurrentResultsTable is null
            || CurrentResultsTable.Rows is null
            || CurrentResultsTable.Rows.Count <= 0
            || CurrentResultsTable.Headers.Count <= 0)
        {
            return;
        }

        vm.SearchInProgress = true;
        try
        {
            // Detach so FilteredRows mutations do not layout against a live DataGrid.
            ((ISqlResultsViewBridge)this).SuspendGridBinding();

            _searchService.ApplySearch(CurrentResultsTable, SearchText, null, ContainsGeneralSearch == true);

            if (SelectedItems.Count > 5_000)
            {
                SelectedItems.Clear();
            }

            vm.GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);

            rowsLoadingMessage.Text = $"{vm.GridCollectionView.Count:N0} rows";
            RefreshSummaryRowWidths();
            vm.RefreshFind();
            _summaryScrollService.InvalidateRowHeaderWidthCache();
        }
        finally
        {
            ((ISqlResultsViewBridge)this).ResumeGridBinding();
            vm.SearchInProgress = false;
        }
    }



    private void DataGrid_Initialized(object? sender, System.EventArgs e)
    {
        RefreshDataGridColumns();
    }

    private bool _refreshingColumns;

    /// <summary>
    /// Refreshes DataGrid columns when data changes. Called from DataGrid_Initialized and OnCurrentResultsTableChanged.
    /// </summary>
    internal void RefreshDataGridColumns()
    {
        if (_refreshingColumns) return;
        if (CurrentResultsTable is null || ResultDataGrid is null || CurrentResultsTable.Headers.Count == 0)
        {
            return;
        }

        _refreshingColumns = true;
        try
        {
            // Clear existing columns to handle both new and recycled views
            ResultDataGrid.Columns.Clear();
            _pinnedColumns.Clear();
            _summaryScrollService.InvalidateRowHeaderWidthCache();

            // Update autocomplete items
            if (columnAutoComplet is not null)
            {
                List<string> headersListCopy = new(CurrentResultsTable.Headers);
                headersListCopy.Sort();
                columnAutoComplet.ItemsSource = headersListCopy;
            }

            // Recreate columns
            List<IValueConverter> valueConverters = [];
            for (var i = 0; i < CurrentResultsTable.Headers.Count; ++i)
            {
                FuncDataTemplate<object> headerTemplate = GetHeaderTemplate(CurrentResultsTable, i, i);

                DataGridBoundColumn col = ResultGridColumnFactory.CreateColumn(CurrentResultsTable, i, headerTemplate, _pinnedColumns, valueConverters);
                ResultDataGrid.Columns.Add(col);
            }
            ResultDataGrid.FrozenColumnCount = _pinnedColumns.Count;
        }
        finally
        {
            _refreshingColumns = false;
        }
    }

    private readonly Dictionary<string, int> _pinnedColumns = [];

    // This payload is consumed only by JustyBase. An application format keeps it
    // available to the in-process drop target on every Avalonia platform.
    private readonly DataFormat<string> _columnNameDataFormat =
        DataFormat.CreateStringApplicationFormat("JustyBase.ColumnName");
    private readonly List<string> _groupedCols = [];
    
    private FuncDataTemplate<object> GetHeaderTemplate(TableOfSqlResults table, int index, int savedI)
    {
        return new FuncDataTemplate<object>((_, _) =>
        {
            var ctx = new ColumnHeaderContext
            {
                ColumnNameDataFormat = _columnNameDataFormat,
                PinnedColumns = _pinnedColumns,
                DataGrid = ResultDataGrid,
                PinIcon = this.Resources["btPinData"] as StreamGeometry ?? throw new InvalidOperationException("btPinData resource not found"),
                UnpinIcon = this.Resources["btPinData2"] as StreamGeometry ?? throw new InvalidOperationException("btPinData2 resource not found"),
                ViewModel = DataContext as SqlResultsViewModel,
                RefreshSummaryRowWidths = RefreshSummaryRowWidths,
                SavedIndex = savedI
            };
            return ColumnHeaderFactory.CreateHeaderControl(table, index, ctx);
        });
    }

    [RelayCommand]
    private void GroupByOneColumn(string name)
    {
        if (GridCollectionView.Count >= 1_000_000)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("to many items");
            return;
        }
        var groupedPropertyNames = GridCollectionView.GroupDescriptions
            .Select(static gd => gd.PropertyName)
            .ToList();
        var togglePlan = _groupingService.BuildTogglePlan(name, CurrentResultsTable.Headers, groupedPropertyNames);
        if (togglePlan.Action == GroupingToggleAction.None)
        {
            return;
        }

        if (togglePlan.Action == GroupingToggleAction.Remove)
        {
            if ((uint)togglePlan.ExistingIndex < (uint)GridCollectionView.GroupDescriptions.Count)
            {
                GridCollectionView.GroupDescriptions.RemoveAt(togglePlan.ExistingIndex);
            }
        }
        else
        {
            // Add sort description for grouping - DataGridCollectionView will handle sorting automatically
            var dataGridSortDescription = DataGridSortDescription.FromPath(togglePlan.PropertyName, ListSortDirection.Ascending);
            GridCollectionView.SortDescriptions.Add(dataGridSortDescription);

            var group = new DataGridPathGroupDescription(togglePlan.PropertyName)
            {
                ValueConverter = new ForGroupValueConverter()
            };
            GridCollectionView.GroupDescriptions.Add(group);
        }
        RefreshGroupedColumnsState();
         
        // Refresh summary row layout after grouping changes
        // Post to dispatcher to allow DataGrid layout to update first (headers shifting)
        Dispatcher.UIThread.Post(() =>
        {
            RefreshSummaryRowWidths();
        }, DispatcherPriority.Input);
    }

    private void RefreshGroupedColumnsState()
    {
        _groupedCols.Clear();
        foreach (var groupDescription in GridCollectionView.GroupDescriptions)
        {
            _groupedCols.Add(groupDescription.PropertyName);
        }

        UpdateViewModelGroupedColumns();
    }

    /// <summary>
    /// Updates the ViewModel's GroupedColumns collection based on current grouping state
    /// </summary>
    private void UpdateViewModelGroupedColumns()
    {
        if (DataContext is not SqlResultsViewModel vm || CurrentResultsTable is null)
        {
            return;
        }

        vm.GroupedColumns.Clear();
        var groupedColumns = _groupingService.ToGroupedColumnNames(_groupedCols, CurrentResultsTable.Headers);
        foreach (var groupedColumn in groupedColumns)
        {
            vm.GroupedColumns.Add(groupedColumn);
        }
    }

    /// <summary>
    /// Removes grouping for a specific column - called from ViewModel via view bridge.
    /// </summary>
    private void RemoveGroupByColumnName(string columnName)
    {
        GroupByOneColumn(columnName);
    }

    private void ResultDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        var boundColumn = e.Column as DataGridBoundColumn;
        var cmp = boundColumn?.CustomSortComparer as CustomResultComparer;
        if (boundColumn is not null && cmp is not null && CurrentResultsTable is not null
            && DataContext is SqlResultsViewModel vm)
        {
            var newDirection = e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            foreach (var column in ResultDataGrid.Columns)
            {
                if (!ReferenceEquals(column, e.Column))
                {
                    column.SortDirection = null;
                }
            }
            e.Column.SortDirection = newDirection;

            CurrentResultsTable.ColumnsToSort.Clear();
            CurrentResultsTable.ColumnsToSort.Add(new TableOfSqlResults.SortInfo
            {
                ColNumber = cmp.Index,
                SortDirection = newDirection,
                Comparer = cmp
            });

            ((ISqlResultsViewBridge)this).SuspendGridBinding();
            try
            {
                CurrentResultsTable.SortFilteredRows();
                vm.GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);
            }
            finally
            {
                ((ISqlResultsViewBridge)this).ResumeGridBinding();
            }

            e.Handled = true;
        }
    }
    private void DataGrid_LoadingRowGroup(object? sender, DataGridRowGroupHeaderEventArgs e)
    {
        DataGridRowGroupHeader group = e.RowGroupHeader;
        group.IsItemCountVisible = true;
        group.ItemCountFormat = "({0:N0} Items)";
    }

    private void DataGrid_DoubleTapped(object sender, RoutedEventArgs e)
    {
        DataGrid dg = (sender as DataGrid);
        var sourceDataContext = (e.Source as Control)?.DataContext;
        bool headerClicked = e.Source is not Control control || control.DataContext is not TableRow;

        bool shouldHandleHeaderDoubleTap = _doubleTapService.ShouldHandleHeaderDoubleTap(
            headerClicked,
            dg.Name == nameof(rowDetailsDataGrid),
            sourceDataContext is SqlResultsViewModel,
            sourceDataContext is Avalonia.Collections.DataGridCollectionViewGroup);
        if (shouldHandleHeaderDoubleTap)
        {
            DataGridDoubleClicked(_doubleTapService.GetHeaderDoubleTapValue(sourceDataContext), true);
            return;
        }
        else if (dg?.SelectedItem is TableRow row)
        {
            int currentColumnIndex = ResultDataGrid.Columns.IndexOf(ResultDataGrid.CurrentColumn);
            var value = _doubleTapService.GetTableRowDoubleTapValue(row, currentColumnIndex);
            DataGridDoubleClicked(value, false);
        }
        else if (dg?.SelectedItem is RowDetail rowDetail)
        {
            var payload = _doubleTapService.GetRowDetailDoubleTapPayload(rowDetail, dg.CurrentColumn.DisplayIndex, dg.Columns.Count);
            DataGridDoubleClicked(payload.Value, payload.RawMode);
        }
    }

    public void CollapseAllGroups()
    {
        ExecuteGroupOperation(ResultGridGroupOperation.Collapse);
    }

    public void ExpandAllGroups()
    {
        ExecuteGroupOperation(ResultGridGroupOperation.Expand);
    }

    private void ExecuteGroupOperation(ResultGridGroupOperation operation)
    {
        // Workaround for Avalonia DataGrid bug: clear validation state before group operation.
        if (!_groupExpandCollapseService.TryCommitPendingEdit(() => _ = ResultDataGrid.CommitEdit(), TrackGroupOperationError))
        {
            return;
        }

        // Defer operation to allow UI to settle.
        Dispatcher.UIThread.Post(() =>
        {
            _groupExpandCollapseService.TryExecuteGroupOperation(
                operation,
                ResultDataGrid.CollapseAllGroups,
                ResultDataGrid.ExpandAllGroups,
                TrackGroupOperationError);
        }, DispatcherPriority.Background);
    }

    private void TrackGroupOperationError(Exception ex)
    {
        _simpleLogger.TrackError(ex, isCrash: false);
    }

    void ISqlResultsViewBridge.RemoveGroupByColumnName(string columnName)
    {
        RemoveGroupByColumnName(columnName);
    }

    void ISqlResultsViewBridge.RecalculateSummaryValues()
    {
        RecalculateSummaryValues();
    }

    void ISqlResultsViewBridge.RefreshColumns()
    {
        RefreshDataGridColumns();
    }

    void ISqlResultsViewBridge.SuspendGridBinding()
    {
        if (ResultDataGrid is not null)
        {
            ResultDataGrid.ItemsSource = null;
        }
    }

    void ISqlResultsViewBridge.ResumeGridBinding()
    {
        if (ResultDataGrid is null)
        {
            return;
        }

        if (DataContext is SqlResultsViewModel vm)
        {
            ResultDataGrid.ItemsSource = vm.GridCollectionView;
        }
    }
}

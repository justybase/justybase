using System.Collections;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Text;
using Avalonia.Collections;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommons;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Documents;

namespace JustyBase.ViewModels.Tools;

public sealed partial class SqlResultsViewModel : Tool, ICleanableViewModel
{
    /// <summary>
    /// Bridge to view-only operations that should not be implemented inside ViewModel.
    /// </summary>
    public ISqlResultsViewBridge? ViewBridge { get; set; }

    [RelayCommand]
    private void GridDoubleClick(GridDoubleClickArg gridDoubleClickArg)
    {
        _activeDocumentManager.InsertTextToActiveDocument(gridDoubleClickArg.Data, gridDoubleClickArg.RawMode);
    }

    [RelayCommand]
    private void GridSelectionChanged(string text)
    {
        _activeDocumentManager.SelectedDataGridAction?.Invoke(text);
    }

    private readonly ResultHelper _resultHelperService;

    [ObservableProperty]
    public partial bool VisibleExpand { get; set; } = false;

    [ObservableProperty]
    public partial string DpWidth { get; set; } = "10";

    partial void OnDpWidthChanged(string value)
    {
        if (int.TryParse(value, out int refWidth))
        {
            if (refWidth <= 30)
            {
                VisibleExpand = false;
            }
            else
            {
                VisibleExpand = true;
            }
        }
    }

    public string RelatedSqlDocumentId { get; set; }

    [ObservableProperty]
    public partial TableOfSqlResults CurrentResultsTable { get; set; }
    public ObservableCollection<RowDetail> RowDetailCollection { get; set; }
    public ObservableCollection<string> GroupedColumns { get; } = [];

    /// <summary>
    /// Dictionary tracking which columns have summaries enabled and their types.
    /// Key is column index, value is summary type.
    /// </summary>
    public Dictionary<int, ColumnSummaryType> ColumnSummaries { get; } = [];

    /// <summary>
    /// Computed summary values for each column. Index corresponds to column index.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> SummaryRowValues { get; set; } = [];

    /// <summary>
    /// Whether the summary row should be visible (true if any column has summary enabled)
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSummaryRow { get; set; } = false;

    /// <summary>
    /// Toggle summary for a column. Called from View via command.
    /// </summary>
    public void SetColumnSummary(int columnIndex, ColumnSummaryType summaryType)
    {
        if (summaryType == ColumnSummaryType.None)
        {
            ColumnSummaries.Remove(columnIndex);
        }
        else
        {
            ColumnSummaries[columnIndex] = summaryType;
        }
        ShowSummaryRow = ColumnSummaries.Count > 0;
        ViewBridge?.RecalculateSummaryValues();
    }

    /// <summary>
    /// Get the current summary type for a column
    /// </summary>
    public ColumnSummaryType GetColumnSummaryType(int columnIndex)
    {
        return ColumnSummaries.TryGetValue(columnIndex, out var summaryType) ? summaryType : ColumnSummaryType.None;
    }

    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IClipboardService _clipboardService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IResultGridActionRoutingService _actionRoutingService;
    private readonly IActiveDocumentManager _activeDocumentManager;
    private readonly IDataGridClipboardService _dataGridClipboardService;

    public IClipboardService Clipboard => _clipboardService;
    public SqlResultsViewModel(IFactory factory, IAvaloniaSpecificHelpers avaloniaSpecificHelpers, IClipboardService clipboardService,
        IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools,
        ISimpleLogger simpleLogger,
        IResultGridActionRoutingService actionRoutingService,
        IActiveDocumentManager activeDocumentManager,
        IDataGridClipboardService dataGridClipboardService
        )
    {
        Factory = factory;
        _activeDocumentManager = activeDocumentManager;
        _dataGridClipboardService = dataGridClipboardService;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _clipboardService = clipboardService;
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
        _actionRoutingService = actionRoutingService;
        _resultHelperService = new(generalApplicationData, _messageForUserTools, _simpleLogger);

        DpWidth = "10";
        RowDetailCollection = [];

        CurrentResultsTable = new TableOfSqlResults();
        //GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows, isDataSorted: true, isDataInGroupOrder: true);
        GridCollectionView = new DataGridCollectionView(CurrentResultsTable.FilteredRows);

        FindModel.ResultsChanged += (_, _) => UpdateFindSummary();
        FindModel.CurrentChanged += (_, _) => UpdateFindSummary();
    }

    [RelayCommand]
    private void ExpandCollapseRowView()
    {
        VisibleExpand = !VisibleExpand;
        if (VisibleExpand)
        {
            DpWidth = "200";
            RefreshRowDetails();
        }
        else
        {
            DpWidth = "10";
        }
    }

    [RelayCommand]
    private void ScreenShot()
    {
        _messageForUserTools.ScreenShot();
    }

    [RelayCommand]
    private void RemoveFromGroup(string columnName)
    {
        ViewBridge?.RemoveGroupByColumnName(columnName);
    }

    public DataGridCollectionView GridCollectionView { get; set; }

    public string SQL { get; set; }


    private bool _doCollapseInNextCollapseAction = true;

    [RelayCommand]
    private void CollapseAll(object o)
    {
        if (_doCollapseInNextCollapseAction)
        {
            ViewBridge?.CollapseAllGroups();
        }
        else
        {
            ViewBridge?.ExpandAllGroups();
        }
        _doCollapseInNextCollapseAction = !_doCollapseInNextCollapseAction;
    }

    public ICommand ShowFlyoutCommand { get; set; } // !!! Mode=OneWayToSource
    public ICommand ChangeColumVisiblityCommand { get; set; } // !!! Mode=OneWayToSource


    [ObservableProperty]
    public partial string ExportMessage { get; set; }

    [ObservableProperty]
    public partial string RowsLoadingMessage { get; set; }

    [ObservableProperty]
    public partial IList SelectedItems { get; set; }

    // DataGrid.SelectedIndex uses -1 for no selection. Keeping that as the
    // initial value prevents the first loaded result row from being selected.
    private int _selInd = -1;
    public int SelInd
    {
        get => _selInd;
        set
        {
            if (!SetProperty(ref _selInd, value))
            {
                return;
            }

            if (VisibleExpand)
            {
                RefreshRowDetails();
            }
        }
    }

    private void RefreshRowDetails()
    {
        if (SelInd < 0 || SelInd >= CurrentResultsTable.FilteredRows.Count || SelectedItems is null)
        {
            return;
        }

        var selectedRows = SelectedItems;
        int selectedCount = selectedRows.Count;
        int cntLimited = selectedCount > 10 ? 10 : selectedCount;

        RowDetailCollection.Clear();
        for (int i = 0; i < CurrentResultsTable.Headers.Count; i++)
        {
            var headerName = CurrentResultsTable.Headers[i];
            var tpe = CurrentResultsTable.DataTypeNames[i];
            var rd = new RowDetail()
            {
                Name = headerName, /*ColumnValue = val,*/
                TypeName = tpe,
                ChangeColVisiblity = () => ChangeColumVisiblityCommand?.Execute(headerName)
            };

            if (cntLimited >= 1)
            {
                rd.FieldsValues = new List<string>(cntLimited);
                for (int i1 = 0; i1 < cntLimited; i1++)
                {
                    object item = selectedRows[i1];
                    rd.FieldsValues.Add((item as TableRow)?.Fields[i]?.ToString());
                }
            }
            RowDetailCollection.Add(rd);
        }
    }

    [ObservableProperty]
    public partial IEnumerable<object> SelectedColumnCells { get; set; } = null;

    [ObservableProperty]
    public partial string StatsText { get; set; }

    partial void OnStatsTextChanged(string value)
    {
        _activeDocumentManager.SelectedDataGridAction?.Invoke(StatsText);
    }


    [ObservableProperty]
    public partial bool GridEnabled { get; set; } = true;
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";
    [ObservableProperty]
    public partial bool GridVisible { get; set; } = false;
    [ObservableProperty]
    public partial bool SearchInProgress { get; set; } = false;
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    partial void OnSearchTextChanged(string value)
    {
        TriggerSearchTimerCommand?.Execute(null);
    }

    public ICommand TriggerSearchTimerCommand { get; set; } // 

    public void DoCleanup()
    {
        _findDebounceCts?.Cancel();
        DisposeSpill();
        if (CurrentResultsTable is not null)
        {
            long n = CurrentResultsTable.FilteredRows.Count * CurrentResultsTable.Headers.Count;
            CurrentResultsTable.DoClear();
            if (n > 5_000_000 && _generalApplicationData.Config.DoGcCollect)
            {
                GC.Collect();
                //GC.WaitForFullGCComplete();
                //GC.WaitForPendingFinalizers();
            }
        }
    }

    [ObservableProperty]
    public partial bool IsSpillMode { get; set; }

    [ObservableProperty]
    public partial int SpillPageIndex { get; set; }

    [ObservableProperty]
    public partial int SpillPageSize { get; set; } = 500;

    [ObservableProperty]
    public partial int SpillTotalRows { get; set; }

    public int SpillPageCount =>
        SpillPageSize <= 0 || SpillTotalRows == 0
            ? 0
            : (SpillTotalRows + SpillPageSize - 1) / SpillPageSize;

    [RelayCommand(CanExecute = nameof(CanGoSpillPrev))]
    private void SpillPrevPage()
    {
        if (!IsSpillMode || SpillPageIndex <= 0)
        {
            return;
        }

        ApplySpillPage(SpillPageIndex - 1);
        RowsLoadingMessage = $"Spill {SpillTotalRows:N0} rows · page {SpillPageIndex + 1}/{SpillPageCount}";
        NotifySpillCommands();
    }

    [RelayCommand(CanExecute = nameof(CanGoSpillNext))]
    private void SpillNextPage()
    {
        if (!IsSpillMode || SpillPageIndex >= SpillPageCount - 1)
        {
            return;
        }

        ApplySpillPage(SpillPageIndex + 1);
        RowsLoadingMessage = $"Spill {SpillTotalRows:N0} rows · page {SpillPageIndex + 1}/{SpillPageCount}";
        NotifySpillCommands();
    }

    private bool CanGoSpillPrev() => IsSpillMode && SpillPageIndex > 0;
    private bool CanGoSpillNext() => IsSpillMode && SpillPageIndex < SpillPageCount - 1;

    private void NotifySpillCommands()
    {
        SpillPrevPageCommand.NotifyCanExecuteChanged();
        SpillNextPageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SpillPageCount));
    }

    public bool ContainsGeneralSearch
    {
        get;
        set
        {
            SetProperty(ref field, value);
            TriggerSearchTimerCommand.Execute(null);
        }
    } = true;

    /// <summary>
    /// Ctrl+F highlight-and-navigate search. Independent from the row filtering
    /// performed by <see cref="SearchText"/>.
    /// </summary>
    public SearchModel FindModel { get; } = new()
    {
        HighlightMode = SearchHighlightMode.TextAndCell,
        HighlightCurrent = true,
        WrapNavigation = true,
        UpdateSelectionOnNavigate = false
    };

    /// <summary>
    /// Experimental: drives the built-in distinct-value column filter (ProDataGrid #318).
    /// </summary>
    public FilteringModel FilteringModel { get; } = new() { OwnsViewFilter = true };

    [ObservableProperty]
    public partial string FindText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsFindVisible { get; set; } = false;

    [ObservableProperty]
    public partial string FindResultSummary { get; set; } = "";

    private CancellationTokenSource? _findDebounceCts;

    partial void OnFindTextChanged(string value)
    {
        if (!IsFindVisible)
        {
            return;
        }
        _findDebounceCts?.Cancel();
        var cts = _findDebounceCts = new CancellationTokenSource();
        _ = DebounceFindAsync(cts.Token);
    }

    partial void OnIsFindVisibleChanged(bool value)
    {
        if (!value)
        {
            FindText = "";
            _findDebounceCts?.Cancel();
            FindModel.Clear();
            FindResultSummary = "";
        }
    }

    private async Task DebounceFindAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested || !IsFindVisible)
                {
                    return;
                }
                ApplyFind();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer find request supersedes this one; nothing to report.
        }
    }

    [RelayCommand]
    private void FindNext()
    {
        EnsureFindVisible();
        FindModel.MoveNext();
        UpdateFindSummary();
    }

    [RelayCommand]
    private void FindPrevious()
    {
        EnsureFindVisible();
        FindModel.MovePrevious();
        UpdateFindSummary();
    }

    [RelayCommand]
    private void CloseFind()
    {
        IsFindVisible = false;
    }

    private void EnsureFindVisible()
    {
        if (!IsFindVisible)
        {
            IsFindVisible = true;
            ApplyFind();
        }
    }

    /// <summary>
    /// Re-runs the active find query after the grid data changed (filter, load,
    /// spill page, sort or grouping).
    /// </summary>
    public void RefreshFind()
    {
        if (!IsFindVisible)
        {
            return;
        }
        _findDebounceCts?.Cancel();
        ApplyFind();
    }

    private void ApplyFind()
    {
        string query = FindText?.Trim() ?? "";
        if (query.Length == 0)
        {
            FindModel.Clear();
        }
        else
        {
            FindModel.SetOrUpdate(new SearchDescriptor(
                query: query,
                matchMode: SearchMatchMode.Contains,
                termMode: SearchTermCombineMode.Any,
                scope: SearchScope.AllColumns,
                comparison: StringComparison.OrdinalIgnoreCase,
                wholeWord: false,
                normalizeWhitespace: true,
                ignoreDiacritics: true));
        }
        UpdateFindSummary();
    }

    private void UpdateFindSummary()
    {
        int count = FindModel.Results.Count;
        FindResultSummary = count > 0 ? $"{FindModel.CurrentIndex + 1} of {count}" : "";
    }

    [ObservableProperty]
    public partial bool DataLoadingInProgress { get; set; } = false;

    /// <summary>
    /// Shown in the results area only when the grid is hidden during load.
    /// </summary>
    [ObservableProperty]
    public partial string LoadingPlaceholderMessage { get; set; } = "";

    public bool ShowLoadingOverlay => DataLoadingInProgress && !GridVisible;

    partial void OnDataLoadingInProgressChanged(bool value) => OnPropertyChanged(nameof(ShowLoadingOverlay));
    partial void OnGridVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowLoadingOverlay));
    [ObservableProperty]
    public partial bool IsResultVisible { get; set; } = true;

    public bool IsDocked
    {
        get;
        set
        {
            field = value;
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged();
            }
            if (IsDocked)
            {
                this.Title += " [DOCKED]";
            }
            else
            {
                this.Title = this.Title.Replace(" [DOCKED]", "");
            }
            OnPropertyChanged(nameof(this.Title));

        }
    }


    private readonly Lock _lock = new();

    [ObservableProperty]
    public partial Queue<int> PrevCols { get; set; } = new Queue<int>();

    [RelayCommand]
    private async Task Copy1(string text)
    {
        await Clipboard?.SetTextAsync(text);
    }
}

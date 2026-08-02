using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;

namespace JustyBase.ViewModels.Documents;

public sealed partial class HistoryViewModel : DocumentBaseVM
{
    private string _searchTxt = "";
    private DispatcherTimer? _searchTimer;
    private bool _favoritesOnly;
    private HistoryStatusFilterOption _selectedStatusFilter;
    private HistoryDurationFilterOption _selectedDurationFilter;

    public string SearchTxt
    {
        get => _searchTxt;
        set
        {
            if (SetProperty(ref _searchTxt, value))
            {
                if (_searchTimer is null)
                {
                    _searchTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500)
                    };
                    _searchTimer.Tick += SearchTimer_Tick;
                }
                _searchTimer.Stop();
                _searchTimer.Start();
            }
        }
    }

    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetProperty(ref _favoritesOnly, value))
            {
                RefreshFilteredItems();
            }
        }
    }

    public IReadOnlyList<HistoryStatusFilterOption> StatusFilterOptions { get; } =
    [
        new("All statuses", null),
        new("OK", HistoryRunStatus.Success),
        new("Failed", HistoryRunStatus.Failed),
        new("Cancelled", HistoryRunStatus.Cancelled),
        new("Unknown", HistoryRunStatus.Unknown),
    ];

    public IReadOnlyList<HistoryDurationFilterOption> DurationFilterOptions { get; } =
    [
        new("Any duration", HistoryDurationPreset.All),
        new("< 1 s", HistoryDurationPreset.Under1s),
        new("1–10 s", HistoryDurationPreset.From1To10s),
        new("10 s–1 min", HistoryDurationPreset.From10sTo1min),
        new("> 1 min", HistoryDurationPreset.Over1min),
    ];

    public HistoryStatusFilterOption SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            var next = value ?? StatusFilterOptions[0];
            if (SetProperty(ref _selectedStatusFilter, next))
            {
                RefreshFilteredItems();
            }
        }
    }

    public HistoryDurationFilterOption SelectedDurationFilter
    {
        get => _selectedDurationFilter;
        set
        {
            var next = value ?? DurationFilterOptions[0];
            if (SetProperty(ref _selectedDurationFilter, next))
            {
                RefreshFilteredItems();
            }
        }
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        RefreshFilteredItems();
    }

    public ICommand RefreshCmd { get; set; }
    public ICommand RerunWithParamsCmd { get; set; }

    private readonly IClipboardService _clipboardService;
    private readonly IMessageForUserTools _ui;
    public IClipboardService Clipboard => _clipboardService;
    public HistoryViewModel(
        HistoryService historyService,
        IClipboardService clipboardService,
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDocumentCloseDecisionService documentCloseDecisionService,
        IActiveDocumentManager activeDocumentManager)
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        _historyService = historyService;
        _clipboardService = clipboardService;
        _ui = messageForUserTools;
        Title = "History";

        _selectedStatusFilter = StatusFilterOptions[0];
        _selectedDurationFilter = DurationFilterOptions[0];

        SearchTxt = "";
        Doc = new TextDocument();
        RefreshCmd = new RelayCommand(RefreshFilteredItems);
        RerunWithParamsCmd = new RelayCommand(RerunWithParams, () => SelectedItem is not null);

        _historyService.HistoryChanged += OnHistoryChanged;
        RefreshFilteredItems();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        _ui.DispatcherActionInstance(RefreshFilteredItems);
    }

    public override bool OnClose()
    {
        _historyService.HistoryChanged -= OnHistoryChanged;
        return base.OnClose();
    }

}

public sealed record HistoryStatusFilterOption(string Display, HistoryRunStatus? Status)
{
    public override string ToString() => Display;
}

public sealed record HistoryDurationFilterOption(string Display, HistoryDurationPreset Preset)
{
    public override string ToString() => Display;
}

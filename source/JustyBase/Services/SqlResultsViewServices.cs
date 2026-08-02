using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.DataGrid;

namespace JustyBase.Services;

/// <summary>
/// Implementation of ISqlResultsViewServices that aggregates all services
/// required by SqlResultsView for proper dependency injection.
/// </summary>
public sealed class SqlResultsViewServices : ISqlResultsViewServices
{
    public ISummaryRowService SummaryRowService { get; }
    public IResultGridSearchService SearchService { get; }
    public IResultGridSummaryRefreshService SummaryRefreshService { get; }
    public IResultGridSummaryScrollService SummaryScrollService { get; }
    public IResultGridSelectionService SelectionService { get; }
    public IResultGridDoubleTapService DoubleTapService { get; }
    public IDataGridClipboardService ClipboardService { get; }
    public IResultGridGroupingService GroupingService { get; }
    public IResultGridGroupingDragService GroupingDragService { get; }
    public IResultGridGroupExpandCollapseService GroupExpandCollapseService { get; }
    public IResultGridStatsService StatsService { get; }
    public IResultGridKeyboardService KeyboardService { get; }
    public IMessageForUserTools MessageForUserTools { get; }
    public ISimpleLogger SimpleLogger { get; }

    public SqlResultsViewServices(
        ISummaryRowService summaryRowService,
        IResultGridSearchService searchService,
        IResultGridSummaryRefreshService summaryRefreshService,
        IResultGridSummaryScrollService summaryScrollService,
        IResultGridSelectionService selectionService,
        IResultGridDoubleTapService doubleTapService,
        IDataGridClipboardService clipboardService,
        IResultGridGroupingService groupingService,
        IResultGridGroupingDragService groupingDragService,
        IResultGridGroupExpandCollapseService groupExpandCollapseService,
        IResultGridStatsService statsService,
        IResultGridKeyboardService keyboardService,
        IMessageForUserTools messageForUserTools,
        ISimpleLogger simpleLogger)
    {
        SummaryRowService = summaryRowService;
        SearchService = searchService;
        SummaryRefreshService = summaryRefreshService;
        SummaryScrollService = summaryScrollService;
        SelectionService = selectionService;
        DoubleTapService = doubleTapService;
        ClipboardService = clipboardService;
        GroupingService = groupingService;
        GroupingDragService = groupingDragService;
        GroupExpandCollapseService = groupExpandCollapseService;
        StatsService = statsService;
        KeyboardService = keyboardService;
        MessageForUserTools = messageForUserTools;
        SimpleLogger = simpleLogger;
    }
}

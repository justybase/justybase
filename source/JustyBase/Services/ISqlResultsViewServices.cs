using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.DataGrid;

namespace JustyBase.Services;

/// <summary>
/// Aggregates all services required by SqlResultsView to enable proper DI.
/// Reduces Service Locator pattern usage in code-behind.
/// </summary>
public interface ISqlResultsViewServices
{
    ISummaryRowService SummaryRowService { get; }
    IResultGridSearchService SearchService { get; }
    IResultGridSummaryRefreshService SummaryRefreshService { get; }
    IResultGridSummaryScrollService SummaryScrollService { get; }
    IResultGridSelectionService SelectionService { get; }
    IResultGridDoubleTapService DoubleTapService { get; }
    IDataGridClipboardService ClipboardService { get; }
    IResultGridGroupingService GroupingService { get; }
    IResultGridGroupingDragService GroupingDragService { get; }
    IResultGridGroupExpandCollapseService GroupExpandCollapseService { get; }
    IResultGridStatsService StatsService { get; }
    IResultGridKeyboardService KeyboardService { get; }
    IMessageForUserTools MessageForUserTools { get; }
    ISimpleLogger SimpleLogger { get; }
}

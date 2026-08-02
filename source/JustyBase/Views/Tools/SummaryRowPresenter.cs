using Avalonia.Collections;
using Avalonia.VisualTree;
using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.Views.Tools;

/// <summary>
/// Builds and updates summary row UI controls for the DataGrid.
/// Extracted from SqlResultsView to separate control creation from the main view code.
/// </summary>
public sealed class SummaryRowPresenter
{
    private readonly ISummaryRowService _summaryRowService;

    public SummaryRowPresenter(ISummaryRowService summaryRowService)
    {
        _summaryRowService = summaryRowService;
    }

    /// <summary>
    /// Builds summary row cells matching column widths in visual order.
    /// </summary>
    /// <param name="summaryPanel">The StackPanel to populate with summary cells.</param>
    /// <param name="columns">DataGrid columns in their original order.</param>
    /// <param name="table">Current results table for calculations.</param>
    /// <param name="columnSummaries">Summary type configuration per column index.</param>
    /// <param name="spacerWidth">Width of the spacer to account for row headers/indentation.</param>
    public void BuildSummaryRow(
        StackPanel summaryPanel,
        IEnumerable<DataGridColumn> columns,
        TableOfSqlResults table,
        Dictionary<int, ColumnSummaryType> columnSummaries,
        double spacerWidth)
    {
        summaryPanel.Children.Clear();

        if (spacerWidth > 0)
        {
            summaryPanel.Children.Add(new Border { Width = spacerWidth });
        }

        // Build summary cells in visual order (by DisplayIndex)
        var columnsInVisualOrder = columns
            .Select((col, idx) => (Column: col, OriginalIndex: idx))
            .OrderBy(x => x.Column.DisplayIndex)
            .ToList();

        foreach (var (column, originalIndex) in columnsInVisualOrder)
        {
            if (!column.IsVisible)
                continue;

            string value = "";
            string tooltip = "";

            if (columnSummaries.TryGetValue(originalIndex, out var summaryType) && summaryType != ColumnSummaryType.None)
            {
                value = _summaryRowService.CalculateSummaryValue(table, originalIndex, summaryType);
                tooltip = _summaryRowService.GetAllStatsTooltip(table, originalIndex);
            }

            var border = new Border
            {
                Width = column.ActualWidth,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 2, 4, 2)
            };

            if (!string.IsNullOrEmpty(tooltip))
            {
                ToolTip.SetTip(border, tooltip);
            }

            var textBlock = new TextBlock
            {
                Text = value,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };
            textBlock.Classes.Add("SummaryText");

            border.Child = textBlock;
            summaryPanel.Children.Add(border);
        }
    }

    /// <summary>
    /// Updates all visible DataGridRowGroupHeader elements with summary text.
    /// </summary>
    public void UpdateGroupHeaderSummaries(
        DataGrid dataGrid,
        TableOfSqlResults table,
        DataGridCollectionView collectionView,
        Dictionary<int, ColumnSummaryType> columnSummaries)
    {
        if (columnSummaries.Count == 0)
            return;

        var groupHeaders = dataGrid.GetVisualDescendants()
            .OfType<DataGridRowGroupHeader>()
            .ToList();

        foreach (var header in groupHeaders)
        {
            if (header.DataContext is DataGridCollectionViewGroup group)
            {
                var summaryText = _summaryRowService.CalculateGroupSummaryText(table, group, columnSummaries);

                var existingSummaryBlock = header.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.Name == "GroupSummaryText");

                if (existingSummaryBlock != null)
                {
                    existingSummaryBlock.Text = summaryText;
                }
                else
                {
                    var panel = header.GetVisualDescendants()
                        .OfType<StackPanel>()
                        .FirstOrDefault(p => p.Orientation == Avalonia.Layout.Orientation.Horizontal);

                    if (panel != null && !string.IsNullOrEmpty(summaryText))
                    {
                        var summaryBlock = new TextBlock
                        {
                            Name = "GroupSummaryText",
                            Text = summaryText,
                            Margin = new Thickness(15, 0, 0, 0),
                            FontWeight = FontWeight.Bold,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                        summaryBlock.Classes.Add("SummaryText");
                        panel.Children.Add(summaryBlock);
                    }
                }
            }
        }
    }
}

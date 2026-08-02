using JustyBase.Models;
using JustyBase.Models.Tools;
using JustyBase.ViewModels.Tools;
using System.Globalization;
using Avalonia.Collections;

namespace JustyBase.Converters
{
    public class GroupSummariesToTextBlockConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // Expected values:
            // 0: DataGridCollectionViewGroup (the group)
            // 1: SqlResultsViewModel (the view model context)
            
            if (values == null || values.Count < 2)
                return null;

            if (values[0] is not DataGridCollectionViewGroup group ||
                values[1] is not SqlResultsViewModel vm)
                return null;

            if (vm.ColumnSummaries.Count == 0 || vm.CurrentResultsTable?.Headers == null)
                return null;

            var groupItems = group.Items;
            if (groupItems == null || groupItems.Count == 0)
                return null;

            var tableRows = groupItems.OfType<TableRow>().ToList();
            if (tableRows.Count == 0)
                return null;

            // We will return a StackPanel or similar content? 
            // Better to return a string or a collection suitable for an ItemsControl?
            // Since we are in a Converter for a ContentPresenter or TextBlock, let's return a readable string.
            // Or better: Let's construct a StackPanel with styled blocks for each summary.
            // But returning Controls from Converter is sometimes heavy.
            // Let's return a StackPanel to allow nice formatting (colors).

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };

            // Sort summaries by column index
            var sortedSummaries = vm.ColumnSummaries.OrderBy(x => x.Key);

            foreach (var kvp in sortedSummaries)
            {
                int colIndex = kvp.Key;
                var summaryType = kvp.Value;
                if (summaryType == ColumnSummaryType.None) continue;
                if (colIndex >= vm.CurrentResultsTable.Headers.Count) continue;

                string headerName = vm.CurrentResultsTable.Headers[colIndex];
                
                var stats = new TableRowStats(vm.CurrentResultsTable, tableRows, colIndex);
                var scale = vm.CurrentResultsTable.GetNumericScale(colIndex);
                string format = $"N{(scale <= 0 ? 2 : scale)}";

                string valStr = summaryType switch
                {
                    ColumnSummaryType.Sum => $"Σ {stats.Sum.ToString(format)}",
                    ColumnSummaryType.Count => $"# {stats.NotNullCnt:N0}",
                    ColumnSummaryType.Average when stats.NotNullCnt > 0 => $"Ø {(stats.Sum / stats.NotNullCnt).ToString(format)}",
                    ColumnSummaryType.Min when stats.MinOfColumn.HasValue => $"↓ {stats.MinOfColumn.Value.ToString(format)}",
                    ColumnSummaryType.Max when stats.MaxOfColumn.HasValue => $"↑ {stats.MaxOfColumn.Value.ToString(format)}",
                    ColumnSummaryType.Distinct => $"≠ {stats.DistinctCnt:N0}",
                    _ => ""
                };

                if (!string.IsNullOrEmpty(valStr))
                {
                    // Create visual block for this summary
                    var border = new Border 
                    { 
                        Background = new SolidColorBrush(Color.Parse("#20808080")), // Subtle background
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1)
                    };
                    
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    
                    // Column Name (lighter)
                    sp.Children.Add(new TextBlock 
                    { 
                        Text = headerName + ":", 
                        FontStyle = FontStyle.Italic,
                        Foreground = Brushes.Gray,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    // Value (Bold, Accent Color)
                    sp.Children.Add(new TextBlock 
                    { 
                        Text = valStr, 
                        FontWeight = FontWeight.Bold,
                        //Foreground = Brushes.DarkBlue, // Or theme dependent
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    
                    // Add style class for theme compat if possible, or bind foreground.
                    // For now use default text color or explicit if needed.
                    
                    border.Child = sp;
                    panel.Children.Add(border);
                }
            }

            if (panel.Children.Count == 0) return null;
            return panel;
        }
    }
}

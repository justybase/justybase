using Avalonia.Xaml.Interactivity;
using JustyBase.Models;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Behaviors;

public sealed class ColumnHeaderContext
{
    public required DataFormat<string> ColumnNameDataFormat { get; init; }
    public required Dictionary<string, int> PinnedColumns { get; init; }
    public Dictionary<int, AditionalOneFilter>? AdditionalValues { get; set; }
    public required DataGrid DataGrid { get; init; }
    public required StreamGeometry PinIcon { get; init; }
    public required StreamGeometry UnpinIcon { get; init; }
    public required StreamGeometry FilterFilledIcon { get; init; }
    public required StreamGeometry FilterNormalIcon { get; init; }
    public SqlResultsViewModel? ViewModel { get; init; }
    public System.Action? TriggerSearchTimer { get; init; }
    public System.Action? RefreshSummaryRowWidths { get; init; }
    public Func<int, CustomListBoxViewModel>? GetFilterDataContext { get; init; }
    public RefreshActionHolder? RefreshHolder { get; init; }
    public int SavedIndex { get; init; }
}

public sealed class RefreshActionHolder
{
    public System.Action? RefreshAction { get; set; }
}

public static class ColumnHeaderFactory
{
    private static readonly HashSet<TypeCode> NumericTypeCodes =
    [
        TypeCode.Byte, TypeCode.SByte, TypeCode.Int16, TypeCode.UInt16,
        TypeCode.Int32, TypeCode.Int64, TypeCode.Single, TypeCode.Double, TypeCode.Decimal
    ];

    public static Grid CreateHeaderControl(TableOfSqlResults table, int index, ColumnHeaderContext ctx)
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto,Auto")
        };

        var headerText = CreateHeaderTextBlock(table, index, ctx);
        grid.Children.Add(headerText);
        Grid.SetColumn(headerText, 0);

        var pinButton = CreatePinButton(table, index, ctx);
        grid.Children.Add(pinButton);
        Grid.SetColumn(pinButton, 2);

        var summaryButton = CreateSummaryButton(table, index, ctx);
        grid.Children.Add(summaryButton);
        Grid.SetColumn(summaryButton, 3);

        var filterButton = CreateFilterButton(table, index, ctx);
        grid.Children.Add(filterButton);
        Grid.SetColumn(filterButton, 4);

        SetupDropHandlers(grid, table, index, ctx);

        return grid;
    }

    private static TextBlock CreateHeaderTextBlock(TableOfSqlResults table, int index, ColumnHeaderContext ctx)
    {
        var tb = new TextBlock
        {
            Text = table.Headers[index],
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Thickness(6, 1, 7, 1),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var behavior = new ColumnHeaderDragBehavior
        {
            ColumnName = table.Headers[index],
            DataFormat = ctx.ColumnNameDataFormat
        };
        
        Interaction.GetBehaviors(tb).Add(behavior);

        return tb;
    }

    private static Button CreatePinButton(TableOfSqlResults table, int index, ColumnHeaderContext ctx)
    {
        var columnName = table.Headers[index];
        bool isPinned = ctx.PinnedColumns.ContainsKey(columnName);

        var col = ctx.DataGrid.Columns[index];
        if (col is DataGridTextColumn textCol)
        {
            textCol.FontWeight = isPinned ? FontWeight.SemiBold : FontWeight.Normal;
        }

        var button = new Button
        {
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(0),
            FontSize = 20,
            Background = Brushes.Transparent,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Tag = isPinned ? "Pin+" : "Pin-",
            Content = new PathIcon
            {
                Data = isPinned ? ctx.PinIcon : ctx.UnpinIcon
            }
        };

        button.Click += (_, _) =>
        {
            if (button.Tag is string tag && tag == "Pin+")
            {
                button.Tag = "Pin-";
                button.Content = new PathIcon { Data = ctx.UnpinIcon };
                ctx.PinnedColumns.Remove(columnName);
                
                var clickCol = ctx.DataGrid.Columns[index];
                clickCol.DisplayIndex = ctx.PinnedColumns.Count;
                if (clickCol is DataGridTextColumn clickTextCol)
                    clickTextCol.FontWeight = FontWeight.Normal;
            }
            else
            {
                button.Tag = "Pin+";
                button.Content = new PathIcon { Data = ctx.PinIcon };
                ctx.PinnedColumns[columnName] = ctx.DataGrid.FrozenColumnCount;
                
                var clickCol = ctx.DataGrid.Columns[index];
                clickCol.DisplayIndex = ctx.PinnedColumns.Count - 1;
                if (clickCol is DataGridTextColumn clickTextCol)
                    clickTextCol.FontWeight = FontWeight.UltraBold;
            }
            
            ctx.DataGrid.FrozenColumnCount = ctx.PinnedColumns.Count;
            ctx.DataGrid.Columns[index].IsVisible = false;
            ctx.DataGrid.Columns[index].IsVisible = true;
        };

        return button;
    }

    private static Button CreateSummaryButton(TableOfSqlResults table, int index, ColumnHeaderContext ctx)
    {
        bool isNumeric = NumericTypeCodes.Contains(table.TypeCodes[index]);
        
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = "Σ",
                FontWeight = FontWeight.Normal,
                FontSize = 18,
                Foreground = Brushes.Gray,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            },
            Margin = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = isNumeric 
                ? "Click to add summary (Sum/Count/Avg...)" 
                : "Click to add summary (Count/Distinct)"
        };

        var flyout = new MenuFlyout();
        
        void AddMenuItem(string header, ColumnSummaryType type)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) =>
            {
                ctx.ViewModel?.SetColumnSummary(ctx.SavedIndex, type);
                if (button.Content is TextBlock tb)
                {
                    tb.FontWeight = type == ColumnSummaryType.None ? FontWeight.Normal : FontWeight.Bold;
                    tb.Foreground = type == ColumnSummaryType.None ? Brushes.Gray : Brushes.Green;
                }
            };
            flyout.Items.Add(item);
        }

        if (isNumeric)
        {
            AddMenuItem("Σ Sum", ColumnSummaryType.Sum);
            AddMenuItem("# Count", ColumnSummaryType.Count);
            AddMenuItem("Ø Average", ColumnSummaryType.Average);
            AddMenuItem("↓ Min", ColumnSummaryType.Min);
            AddMenuItem("↑ Max", ColumnSummaryType.Max);
            AddMenuItem("≠ Distinct", ColumnSummaryType.Distinct);
        }
        else
        {
            AddMenuItem("# Count", ColumnSummaryType.Count);
            AddMenuItem("≠ Distinct", ColumnSummaryType.Distinct);
        }

        flyout.Items.Add(new Separator());
        AddMenuItem("✕ None (Remove)", ColumnSummaryType.None);

        button.Flyout = flyout;

        if (ctx.ViewModel?.ColumnSummaries.ContainsKey(ctx.SavedIndex) == true)
        {
            if (button.Content is TextBlock tb)
            {
                tb.FontWeight = FontWeight.Bold;
                tb.Foreground = Brushes.Green;
            }
        }

        return button;
    }

    private static Button CreateFilterButton(TableOfSqlResults table, int index, ColumnHeaderContext ctx)
    {
        bool hasFilter = ctx.AdditionalValues?.ContainsKey(index) == true;

        var button = new Button
        {
            Content = new PathIcon { Data = hasFilter ? ctx.FilterFilledIcon : ctx.FilterNormalIcon },
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(0),
            FontSize = 20,
            Background = Brushes.Transparent,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        Avalonia.Automation.AutomationProperties.SetName(button, $"ColumnFilter_{index}");
        Avalonia.Automation.AutomationProperties.SetAutomationId(button, $"ColumnFilter_{index}");

        var listBox = new CustomListBox();
        var listBoxViewModel = ctx.GetFilterDataContext?.Invoke(index);
        if (listBoxViewModel == null) return button;
        
        listBox.DataContext = listBoxViewModel;

        var flyout = new Flyout
        {
            Content = listBox,
            ShowMode = FlyoutShowMode.Standard
        };

        flyout.Opening += (_, _) =>
        {
            listBoxViewModel.OpeningAction();
            if (ctx.RefreshHolder != null)
                ctx.RefreshHolder.RefreshAction = () => listBoxViewModel?.RefreshList();
        };

        listBoxViewModel.CloseAction = () =>
        {
            if (ctx.RefreshHolder != null)
                ctx.RefreshHolder.RefreshAction = null;
            flyout.Hide();
        };

        void ApplyFilter()
        {
            var newFilter = new AditionalOneFilter(listBoxViewModel.FilterTextForList)
            {
                InList = listBoxViewModel.CheckItems,
                NotList = listBoxViewModel.UncheckItems,
                FilterType = listBoxViewModel.FilterType
            };

            if (string.IsNullOrEmpty(newFilter.FilterEnteredTextPhase) && 
                (newFilter.InList?.Count ?? 0) == 0 &&
                (newFilter.NotList?.Count ?? 0) == 0 &&
                newFilter.FilterType != FilterTypeEnum.isNull && 
                newFilter.FilterType != FilterTypeEnum.isNotNull)
            {
                ctx.AdditionalValues?.Remove(index);
            }
            else
            {
                var values = ctx.AdditionalValues ??= [];
                values[index] = newFilter;
            }

            ctx.TriggerSearchTimer?.Invoke();
        }

        flyout.Closed += (_, _) =>
        {
            button.Content = new PathIcon
            {
                Data = ctx.AdditionalValues?.ContainsKey(index) == true
                    ? ctx.FilterFilledIcon
                    : ctx.FilterNormalIcon
            };
            ApplyFilter();
        };

        listBoxViewModel.OnlineSearchAction = ApplyFilter;

        button.ContextFlyout = flyout;
        button.Click += (_, _) => flyout.ShowAt(button, true);

        return button;
    }

    private static void SetupDropHandlers(Grid grid, TableOfSqlResults table, int index, ColumnHeaderContext ctx)
    {
        DragDrop.SetAllowDrop(grid, true);

        grid.AddHandler(DragDrop.DragOverEvent, (s, e) =>
        {
            if (e.DataTransfer.Contains(ctx.ColumnNameDataFormat))
            {
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
            }
        });

        grid.AddHandler(DragDrop.DropEvent, (s, e) =>
        {
            if (e.DataTransfer.TryGetValue(ctx.ColumnNameDataFormat) is string sourceColName)
            {
                var sourceCol = ctx.DataGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == sourceColName);
                var targetCol = ctx.DataGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == table.Headers[index]);

                if (sourceCol != null && targetCol != null && sourceCol != targetCol)
                {
                    int sourceIndex = sourceCol.DisplayIndex;
                    int targetIndex = targetCol.DisplayIndex;

                    var pos = e.GetPosition(grid);
                    bool insertAfter = pos.X > (grid.Bounds.Width / 2.0);

                    int newDisplayIndex = insertAfter ? targetIndex + 1 : targetIndex;
                    if (sourceIndex < newDisplayIndex)
                        newDisplayIndex--;

                    newDisplayIndex = Math.Max(0, Math.Min(newDisplayIndex, ctx.DataGrid.Columns.Count - 1));
                    sourceCol.DisplayIndex = newDisplayIndex;

                    ctx.RefreshSummaryRowWidths?.Invoke();
                }
                e.Handled = true;
            }
        });
    }
}

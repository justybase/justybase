using JustyBase.Models;
using JustyBase.Themes;

namespace JustyBase.Views.Tools;

public class CustomDataGridTextColumn : DataGridTextColumn
{
    private static readonly Brush _nullBrushLight = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE0));
    private static readonly Brush _nullBrushDark = new SolidColorBrush(Color.FromRgb(0x64, 0x64, 0x46));

    public static readonly AttachedProperty<int> SelectedFieldIndexProperty = 
        AvaloniaProperty.RegisterAttached<CustomDataGridTextColumn, DataGridCell, int>("SelectedFieldIndex", -1);

    public int SelectedFieldIndex { get; set; } = -1;

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var textBlock = (TextBlock)base.GenerateElement(cell, dataItem);
        
        cell.SetValue(SelectedFieldIndexProperty, SelectedFieldIndex);
        UpdateCellBackgroundStatic(cell, dataItem);

        // Standard way to handle cell virtualization updates in Avalonia DataGrid
        cell.DataContextChanged -= Cell_DataContextChangedStatic;
        cell.DataContextChanged += Cell_DataContextChangedStatic;

        return textBlock;
    }

    private static void Cell_DataContextChangedStatic(object? sender, EventArgs e)
    {
        if (sender is DataGridCell cell)
        {
            UpdateCellBackgroundStatic(cell, cell.DataContext);
        }
    }

    private static void UpdateCellBackgroundStatic(DataGridCell cell, object? dataItem)
    {
        int index = cell.GetValue(SelectedFieldIndexProperty);
        if (dataItem is TableRow row && index >= 0 && index < row.Fields.Length)
        {
            if (row.Fields[index] is null)
            {
                cell.Background = FluentThemeManager.IsDark ? _nullBrushDark : _nullBrushLight;
            }
            else
            {
                cell.ClearValue(DataGridCell.BackgroundProperty);
            }
        }
        else
        {
            cell.ClearValue(DataGridCell.BackgroundProperty);
        }
    }
}


public class CustomDataGridCheckBoxColumn : DataGridCheckBoxColumn
{
    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var checkBox = (CheckBox)base.GenerateElement(cell, dataItem);
        
        UpdateCellBackgroundStatic(cell);
        
        cell.DataContextChanged -= Cell_DataContextChangedStatic;
        cell.DataContextChanged += Cell_DataContextChangedStatic;

        return checkBox;
    }

    private static void Cell_DataContextChangedStatic(object? sender, EventArgs e)
    {
        if (sender is DataGridCell cell)
        {
            UpdateCellBackgroundStatic(cell);
        }
    }

    private static void UpdateCellBackgroundStatic(DataGridCell cell)
    {
        cell.ClearValue(DataGridCell.BackgroundProperty);
        cell.ClearValue(DataGridCell.ForegroundProperty);
    }
}

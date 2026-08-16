using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using JustyBase.Converters;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.Views.Tools;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Creates result-grid columns with consistent binding and formatting rules.
/// </summary>
public static class ResultGridColumnFactory
{
    public static DataGridBoundColumn CreateColumn(
        TableOfSqlResults table,
        int index,
        FuncDataTemplate<object> headerTemplate,
        IDictionary<string, int> pinnedColumns,
        IList<IValueConverter> valueConverters)
    {
        var nullConverter = CreateNullValueConverter(table, index);
        valueConverters.Add(nullConverter);

        var cellValueBinding = new Binding($"{nameof(TableRow.Fields)}[{index}]")
        {
            Mode = BindingMode.OneWay,
            Converter = nullConverter
        };

        DataGridBoundColumn col = table.TypeCodes[index] == TypeCode.Boolean
            ? CreateCheckBoxColumn(table, index, headerTemplate, cellValueBinding)
            : CreateTextColumn(table, index, headerTemplate, cellValueBinding);

        // Enable the grid search adapter (Ctrl+F) to read this column's values
        // through the same Fields[index] path used by the cell binding.
        DataGridColumnSearch.SetSearchMemberPath(col, $"{TableOfSqlResults.FIELDS_WORD}[{index}]");

        ConfigureBuiltInColumnFilter(table, index, col);

        if (col.Header is string header && pinnedColumns.TryGetValue(header, out var displayIndex))
        {
            col.DisplayIndex = displayIndex;
        }

        return col;
    }

    private static void ConfigureBuiltInColumnFilter(TableOfSqlResults table, int index, DataGridBoundColumn col)
    {
        string fieldsPath = $"{TableOfSqlResults.FIELDS_WORD}[{index}]";

        col.SortMemberPath = fieldsPath;
        col.ColumnKey = $"col{index}";
        var valueAccessor = new DataGridColumnValueAccessor<TableRow, object>(row => row.Fields[index]);
        DataGridColumnFilter.SetValueAccessor(col, valueAccessor);
        col.ShowFilterButton = true;
        col.FilterFlyout = new CascadingDistinctValueFilterFlyout
        {
            Column = col,
            ValueAccessor = valueAccessor,
            Placement = PlacementMode.Bottom
        };
    }

    private static CustomDataGridCheckBoxColumn CreateCheckBoxColumn(
        TableOfSqlResults table,
        int index,
        FuncDataTemplate<object> headerTemplate,
        Binding cellValueBinding)
    {
        return new CustomDataGridCheckBoxColumn()
        {
            Header = table.Headers[index],
            HeaderTemplate = headerTemplate,
            MaxWidth = 1_000,
            Binding = cellValueBinding,
            Width = DataGridLength.SizeToHeader,
            IsReadOnly = true,
            CanUserSort = true,
            CustomSortComparer = new CustomResultComparer(table.TypeCodes[index], index),
            IsThreeState = true,
        };
    }

    private static DataGridBoundColumn CreateTextColumn(
        TableOfSqlResults table,
        int index,
        FuncDataTemplate<object> headerTemplate,
        Binding cellValueBinding)
    {
        DataGridBoundColumn col = new CustomDataGridTextColumn()
        {
            Header = table.Headers[index],
            HeaderTemplate = headerTemplate,
            MaxWidth = 1_000,
            Binding = cellValueBinding,
            Width = DataGridLength.SizeToHeader,
            IsReadOnly = true,
            CanUserSort = true,
            CustomSortComparer = new CustomResultComparer(table.TypeCodes[index], index),
            SelectedFieldIndex = index
        };

        if (ShouldApplyPinnedStyle(table.TypeCodes[index]))
        {
            col.CellStyleClasses.Add("pinnedStyle");
        }

        return col;
    }

    private static NullValueConverter CreateNullValueConverter(TableOfSqlResults table, int index)
    {
        var converter = new NullValueConverter();

        if (table.TypeCodes[index] == TypeCode.Decimal)
        {
            var scale = table.GetNumericScale(index);
            converter.NumericFormat = $"N{(scale <= 0 ? 1 : scale)}";
        }
        else if ((table.TypeCodes[index] == TypeCode.Int32 || table.TypeCodes[index] == TypeCode.Int64) &&
                 ShouldApplyDateIntFormat(table, index))
        {
            converter.NumericIntFormat = "#### ## ##";
        }

        return converter;
    }

    private static bool ShouldApplyDateIntFormat(TableOfSqlResults table, int index)
    {
        foreach (TableRow item in table.Rows.Take(50))
        {
            if (!DateOnly.TryParseExact(item.Fields[index]?.ToString(), "yyyyMMdd", out _))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ShouldApplyPinnedStyle(TypeCode typeCode)
    {
        return typeCode == TypeCode.Char || typeCode == TypeCode.SByte || typeCode == TypeCode.Int16
            || typeCode == TypeCode.Int32 || typeCode == TypeCode.Int64 || typeCode == TypeCode.Single
            || typeCode == TypeCode.Double || typeCode == TypeCode.Decimal;
    }
}

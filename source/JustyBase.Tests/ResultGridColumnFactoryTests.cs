using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using JustyBase.Converters;
using JustyBase.Models;
using JustyBase.Services.DataGrid;
using JustyBase.Views.Tools;
using System;
using System.Collections.Generic;

namespace JustyBase.Tests;

public class ResultGridColumnFactoryTests
{
    [Fact]
    public void CreateColumn_DecimalColumn_UsesScaleBasedNumericFormat()
    {
        var table = CreateSingleColumnTable(TypeCode.Decimal, 3, [1.25m, 2.50m]);
        var valueConverters = new List<IValueConverter>();

        DataGridBoundColumn column = ResultGridColumnFactory.CreateColumn(
            table,
            0,
            CreateHeaderTemplate(),
            new Dictionary<string, int>(),
            valueConverters);

        _ = Assert.IsType<CustomDataGridTextColumn>(column);
        var converter = Assert.IsType<NullValueConverter>(Assert.Single(valueConverters));
        Assert.Equal("N3", converter.NumericFormat);

        var binding = Assert.IsType<Binding>(column.Binding);
        Assert.Same(converter, binding.Converter);
    }

    [Fact]
    public void CreateColumn_IntDateColumn_UsesDateIntFormat()
    {
        var table = CreateSingleColumnTable(TypeCode.Int32, 6, [20240101, 20241231]);
        var valueConverters = new List<IValueConverter>();

        _ = ResultGridColumnFactory.CreateColumn(
            table,
            0,
            CreateHeaderTemplate(),
            new Dictionary<string, int>(),
            valueConverters);

        var converter = Assert.IsType<NullValueConverter>(Assert.Single(valueConverters));
        Assert.Equal("#### ## ##", converter.NumericIntFormat);
    }

    [Fact]
    public void CreateColumn_NonDateIntColumn_UsesDefaultIntFormat()
    {
        var table = CreateSingleColumnTable(TypeCode.Int64, 6, [123, 999]);
        var valueConverters = new List<IValueConverter>();

        _ = ResultGridColumnFactory.CreateColumn(
            table,
            0,
            CreateHeaderTemplate(),
            new Dictionary<string, int>(),
            valueConverters);

        var converter = Assert.IsType<NullValueConverter>(Assert.Single(valueConverters));
        Assert.Equal("N0", converter.NumericIntFormat);
    }

    [Fact]
    public void CreateColumn_BooleanColumn_ReturnsCheckBoxColumn()
    {
        var table = CreateSingleColumnTable(TypeCode.Boolean, 6, [true, false]);
        var valueConverters = new List<IValueConverter>();

        DataGridBoundColumn column = ResultGridColumnFactory.CreateColumn(
            table,
            0,
            CreateHeaderTemplate(),
            new Dictionary<string, int>(),
            valueConverters);

        var checkBoxColumn = Assert.IsType<CustomDataGridCheckBoxColumn>(column);
        Assert.True(checkBoxColumn.IsThreeState);
    }

    [Fact]
    public void CreateColumn_AppliesPinnedDisplayIndex()
    {
        var table = CreateSingleColumnTable(TypeCode.String, 6, ["a", "b"]);
        var valueConverters = new List<IValueConverter>();
        var pinnedColumns = new Dictionary<string, int> { [table.Headers[0]] = 2 };

        DataGridBoundColumn column = ResultGridColumnFactory.CreateColumn(
            table,
            0,
            CreateHeaderTemplate(),
            pinnedColumns,
            valueConverters);

        Assert.Equal(2, column.DisplayIndex);
    }

    private static FuncDataTemplate<object> CreateHeaderTemplate()
    {
        return new FuncDataTemplate<object>((_, _) => new TextBlock());
    }

    private static TableOfSqlResults CreateSingleColumnTable(TypeCode typeCode, byte scale, IReadOnlyList<object> values)
    {
        var table = new TableOfSqlResults
        {
            Headers = ["COL_1"],
            DataTypeNames = [],
            TypeCodes = [typeCode],
            NumericScales = [scale],
            Rows = [],
            FilteredRows = new BulkObservableCollection<TableRow>()
        };

        foreach (var value in values)
        {
            table.Rows.Add(new TableRow { Fields = [value] });
        }

        return table;
    }
}

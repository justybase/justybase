using System;
using System.Collections.Generic;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public class CellStatsCalculatorTests
{
    private readonly CellStatsCalculator _calculator;

    public CellStatsCalculatorTests()
    {
        _calculator = new CellStatsCalculator();
    }

    [Fact]
    public void Calculate_WithEmptyInput_ReturnsZeroedResult()
    {
        var input = new List<(object?, TypeCode)>();
        
        var result = _calculator.Calculate(input);

        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(0m, result.Sum);
        Assert.Equal(0, result.NotNullCount);
        Assert.Equal(0, result.DistinctCount);
        Assert.Null(result.Min);
        Assert.Null(result.Max);
        Assert.Empty(result.SelectedValues);
    }

    [Fact]
    public void Calculate_WithNullValues_IgnoresNulls()
    {
        var input = new List<(object?, TypeCode)>
        {
            (null, TypeCode.String),
            (null, TypeCode.Int32),
        };

        var result = _calculator.Calculate(input);

        Assert.Equal(2, result.SelectedCount);
        Assert.Equal(0, result.NotNullCount);
        Assert.Equal(0m, result.Sum);
        Assert.Equal(0, result.DistinctCount);
        Assert.Empty(result.SelectedValues);
    }

    [Fact]
    public void Calculate_WithNumericValues_CalculatesStatsCorrectly()
    {
        var input = new List<(object?, TypeCode)>
        {
            (10, TypeCode.Int32),
            (20.5m, TypeCode.Decimal),
            (5, TypeCode.Int32),
            (10, TypeCode.Int32), // duplicate
            (null, TypeCode.Int32)
        };

        var result = _calculator.Calculate(input);

        Assert.Equal(5, result.SelectedCount);
        Assert.Equal(4, result.NotNullCount);
        Assert.Equal(3, result.DistinctCount); // 10, 20.5m, 5
        Assert.Equal(45.5m, result.Sum);
        Assert.Equal(5m, result.Min);
        Assert.Equal(20.5m, result.Max);
        Assert.Equal(4, result.SelectedValues.Count);
    }

    [Fact]
    public void Calculate_WithNonNumericValues_CountsThemButDoesNotSum()
    {
        var input = new List<(object?, TypeCode)>
        {
            ("Value1", TypeCode.String),
            ("Value2", TypeCode.String),
            ("Value1", TypeCode.String), // duplicate
            (100, TypeCode.Int32)
        };

        var result = _calculator.Calculate(input);

        Assert.Equal(4, result.SelectedCount);
        Assert.Equal(4, result.NotNullCount);
        Assert.Equal(3, result.DistinctCount); // "Value1", "Value2", 100
        Assert.Equal(100m, result.Sum);
        Assert.Equal(100m, result.Min);
        Assert.Equal(100m, result.Max);
    }

    [Theory]
    [InlineData((byte)10, TypeCode.Byte, true, 10)]
    [InlineData((sbyte)-5, TypeCode.SByte, true, -5)]
    [InlineData((int)42, TypeCode.Int32, true, 42)]
    [InlineData((long)123456789, TypeCode.Int64, true, 123456789)]
    [InlineData(10.5f, TypeCode.Single, true, 10.5)]
    [InlineData(20.25d, TypeCode.Double, true, 20.25)]
    [InlineData("Not a number", TypeCode.String, false, 0)]
    [InlineData(true, TypeCode.Boolean, false, 0)]
    public void TryConvertNumericCellValue_ReturnsExpectedResult(object value, TypeCode typeCode, bool expectedSuccess, double expectedNumericValueAsDouble)
    {
        decimal expectedDecimal = (decimal)expectedNumericValueAsDouble;

        bool success = CellStatsCalculator.TryConvertNumericCellValue(value, typeCode, out decimal numericValue);

        Assert.Equal(expectedSuccess, success);
        if (success)
        {
            Assert.Equal(expectedDecimal, numericValue);
        }
    }
}

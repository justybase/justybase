using JustyBase.ImportExport.Import;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Tests;

public sealed class ImportCompatibilityTests
{
    private static (string Summary, string[]? TargetColumnNames, bool HasErrors) Report(
        string[] source,
        ImportColumnKind[] kinds,
        (string Name, string FullTypeName)[] target)
        => ImportViewModel.BuildCompatibilityReport(source, kinds, target);

    [Fact]
    public void Report_AllColumnsCompatible_ReturnsMappingAndNoErrors()
    {
        var (summary, mapping, hasErrors) = Report(
            ["ID", "PRICE", "LABEL"],
            [ImportColumnKind.Integer, ImportColumnKind.Numeric, ImportColumnKind.Nvarchar],
            [("ID", "BIGINT"), ("PRICE", "NUMERIC(16,2)"), ("LABEL", "VARCHAR(50)")]);

        Assert.False(hasErrors);
        Assert.Equal(["ID", "PRICE", "LABEL"], mapping);
        Assert.Contains("Compatibility OK", summary);
    }

    [Fact]
    public void Report_MissingTargetColumn_IsBlocking()
    {
        var (summary, mapping, hasErrors) = Report(
            ["ID", "PRICE"],
            [ImportColumnKind.Integer, ImportColumnKind.Numeric],
            [("ID", "BIGINT")]);

        Assert.True(hasErrors);
        Assert.Null(mapping);
        Assert.Contains("PRICE", summary);
    }

    [Fact]
    public void Report_TypeConflict_IsBlocking()
    {
        var (summary, mapping, hasErrors) = Report(
            ["ID"],
            [ImportColumnKind.Integer],
            [("ID", "VARCHAR(10)")]);

        Assert.True(hasErrors);
        Assert.Null(mapping);
        Assert.Contains("Type conflict for 'ID'", summary);
    }

    [Fact]
    public void Report_ExtraTargetColumns_AreWarningsNotErrors()
    {
        var (summary, mapping, hasErrors) = Report(
            ["ID"],
            [ImportColumnKind.Integer],
            [("ID", "BIGINT"), ("EXTRA", "VARCHAR(10)")]);

        Assert.False(hasErrors);
        Assert.Equal(["ID"], mapping);
        Assert.Contains("extra column", summary);
    }

    [Theory]
    [InlineData(ImportColumnKind.Integer, "BIGINT", true)]
    [InlineData(ImportColumnKind.Integer, "VARCHAR(20)", false)]
    [InlineData(ImportColumnKind.Numeric, "DECIMAL(10,2)", true)]
    [InlineData(ImportColumnKind.Nvarchar, "CHAR(10)", true)]
    [InlineData(ImportColumnKind.Date, "DATE", true)]
    [InlineData(ImportColumnKind.Date, "TIMESTAMP", false)]
    [InlineData(ImportColumnKind.TimeStamp, "DATETIME", true)]
    [InlineData(ImportColumnKind.Boolean, "BOOLEAN", true)]
    [InlineData(ImportColumnKind.Boolean, "INTEGER", false)]
    public void IsTypeCompatible_KindFamilies(ImportColumnKind kind, string target, bool expected)
    {
        Assert.Equal(expected, ImportViewModel.IsTypeCompatible(kind, target));
    }
}

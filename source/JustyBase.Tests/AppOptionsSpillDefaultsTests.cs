using JustyBase.Common;

namespace JustyBase.Tests;

public sealed class AppOptionsSpillDefaultsTests
{
    [Fact]
    public void AddDefaultValues_BackfillsBelowThresholdSpillSettings()
    {
        var options = new AppOptions
        {
            ResultRowsLimit = 20_000,
            ResultSpillThreshold = 0,
            ResultSpillPageSize = 0,
            ResultMemoryBudgetBytes = 0,
            ResultSpillQuotaBytes = 0
        };

        options.AddDefaultValues();

        Assert.Equal(1_000_000, options.ResultRowsLimit);
        Assert.Equal(250_000, options.ResultSpillThreshold);
        Assert.Equal(20_000, options.ResultSpillPageSize);
        Assert.Equal(4L * 1024 * 1024 * 1024, options.ResultMemoryBudgetBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, options.ResultSpillQuotaBytes);
    }

    [Fact]
    public void AddDefaultValues_PreservesExplicitSpillSettingsAtOrAboveThreshold()
    {
        var options = new AppOptions
        {
            ResultRowsLimit = 1_200_000,
            ResultSpillThreshold = 1_200_000,
            ResultSpillPageSize = 1_250_000,
            ResultMemoryBudgetBytes = 6L * 1024 * 1024 * 1024,
            ResultSpillQuotaBytes = 3L * 1024 * 1024 * 1024
        };

        options.AddDefaultValues();

        Assert.Equal(1_200_000, options.ResultRowsLimit);
        Assert.Equal(1_200_000, options.ResultSpillThreshold);
        // The page-size setting is clamped to the supported scroll window.
        Assert.Equal(100_000, options.ResultSpillPageSize);
        Assert.Equal(6L * 1024 * 1024 * 1024, options.ResultMemoryBudgetBytes);
        Assert.Equal(3L * 1024 * 1024 * 1024, options.ResultSpillQuotaBytes);
    }

    [Fact]
    public void AddDefaultValues_ClampsTinyAndHugeMemoryBudgets()
    {
        var tiny = new AppOptions { ResultMemoryBudgetBytes = 1, ResultSpillQuotaBytes = 1 };
        tiny.AddDefaultValues();

        Assert.Equal(512L * 1024 * 1024, tiny.ResultMemoryBudgetBytes);
        Assert.Equal(256L * 1024 * 1024, tiny.ResultSpillQuotaBytes);

        var huge = new AppOptions
        {
            ResultMemoryBudgetBytes = long.MaxValue,
            ResultSpillQuotaBytes = long.MaxValue
        };
        huge.AddDefaultValues();

        Assert.Equal(64L * 1024 * 1024 * 1024, huge.ResultMemoryBudgetBytes);
        Assert.Equal(64L * 1024 * 1024 * 1024, huge.ResultSpillQuotaBytes);
    }
}

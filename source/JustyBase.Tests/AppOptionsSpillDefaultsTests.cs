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
            ResultSpillPageSize = 0
        };

        options.AddDefaultValues();

        Assert.Equal(1_000_000, options.ResultRowsLimit);
        Assert.Equal(1_000_000, options.ResultSpillThreshold);
        Assert.Equal(1_000_000, options.ResultSpillPageSize);
    }

    [Fact]
    public void AddDefaultValues_PreservesExplicitSpillSettingsAtOrAboveThreshold()
    {
        var options = new AppOptions
        {
            ResultRowsLimit = 1_200_000,
            ResultSpillThreshold = 1_200_000,
            ResultSpillPageSize = 1_250_000
        };

        options.AddDefaultValues();

        Assert.Equal(1_200_000, options.ResultRowsLimit);
        Assert.Equal(1_200_000, options.ResultSpillThreshold);
        Assert.Equal(1_250_000, options.ResultSpillPageSize);
    }
}

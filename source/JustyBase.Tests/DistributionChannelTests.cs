using JustyBase.Services.Updates;

namespace JustyBase.Tests;

public sealed class DistributionChannelTests
{
    [Theory]
    [InlineData("Full", DistributionChannel.Full)]
    [InlineData("full", DistributionChannel.Full)]
    [InlineData(" Slim ", DistributionChannel.Slim)]
    [InlineData("slim", DistributionChannel.Slim)]
    [InlineData("light", DistributionChannel.Slim)]
    public void FromVariant_MapsKnownVariantNames(string variant, string expected)
    {
        Assert.Equal(expected, DistributionChannel.FromVariant(variant));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("beta")]
    [InlineData("win-arm64")]
    public void FromVariant_ReturnsNullForUnknownNames(string? variant)
    {
        Assert.Null(DistributionChannel.FromVariant(variant));
    }

    [Theory]
    [InlineData(DistributionChannel.Full, "Full")]
    [InlineData(DistributionChannel.Slim, "Slim")]
    [InlineData("WIN", "Full")]
    [InlineData("WIN-SLIM", "Slim")]
    public void ToVariant_MapsInstalledChannels(string channel, string expected)
    {
        Assert.Equal(expected, DistributionChannel.ToVariant(channel));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("osx")]
    [InlineData("linux")]
    public void ToVariant_ReturnsNullForOtherChannels(string? channel)
    {
        Assert.Null(DistributionChannel.ToVariant(channel));
    }

    [Fact]
    public void VariantNamesRoundTrip()
    {
        Assert.Equal("Full", DistributionChannel.ToVariant(DistributionChannel.FromVariant("Full")));
        Assert.Equal("Slim", DistributionChannel.ToVariant(DistributionChannel.FromVariant("Slim")));
    }
}

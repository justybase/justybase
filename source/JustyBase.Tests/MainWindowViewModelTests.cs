using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Updates;
using JustyBase.ViewModels;
using Moq;
using System.Text.Json;

namespace JustyBase.Tests;

/// <summary>
/// Thin coverage for MainWindowViewModel. Full DI ctor needs a live DockFactory
/// (CreateLayout / InitLayout / pipe setup), so tests use the designer parameterless ctor.
/// </summary>
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void DesignerConstructor_LeavesLayoutUnsetAndAutoDownloadNull()
    {
        var vm = new MainWindowViewModel();

        Assert.Null(vm.Layout);
        Assert.Null(vm.AutoDownloadUpdate);
    }

    [Fact]
    public void DefaultAppOptions_EnableAutoUpdateByDefault()
    {
        var options = new AppOptions();

        Assert.True(options.AutoDownloadUpdate);
    }

    [Fact]
    public void UpdateCheckTimestamp_RoundTripsThroughSourceGeneratedJson()
    {
        DateTimeOffset timestamp = new(2026, 8, 16, 12, 30, 0, TimeSpan.Zero);
        var options = new AppOptions { LastUpdateCheckUtc = timestamp };

        string json = JsonSerializer.Serialize(options, MyJsonContextAppOptions.Default.AppOptions);
        AppOptions restored = JsonSerializer.Deserialize(
            json,
            MyJsonContextAppOptions.Default.AppOptions)!;

        Assert.Equal(timestamp, restored.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task UpdateService_SkipsNonVelopackInstallation()
    {
        var applicationData = new Mock<IGeneralApplicationData>();
        applicationData.SetupGet(x => x.Config).Returns(new AppOptions());
        var logger = new Mock<ISimpleLogger>();
        using var service = new ApplicationUpdateService(applicationData.Object, logger.Object);

        ApplicationUpdateResult result = await service.CheckAndDownloadAsync(manual: true);

        Assert.Equal(ApplicationUpdateStatus.Unsupported, result.Status);
        applicationData.Verify(x => x.SaveConfig(), Times.Never);
    }

    [Fact]
    public void CharAtMessage_CanBeAssigned()
    {
        var vm = new MainWindowViewModel
        {
            CharAtMessage = "Ln 1, Col 2"
        };

        Assert.Equal("Ln 1, Col 2", vm.CharAtMessage);
    }
}

using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Services.Ai;
using Moq;

namespace JustyBase.Tests;

/// <summary>
/// Regression tests for A.3: settings-only changes must persist via
/// <c>SaveAppConfig</c> and never rewrite the credentials file.
/// Rewriting credentials on every snippet/chat/export setting change
/// multiplied corruption windows for no reason.
/// </summary>
public class SettingsSaveSeparationTests
{
    [Fact]
    public void ChatSettingsStore_Update_SavesAppConfigOnly()
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());
        var store = new AppOptionsChatSettingsStore(appData.Object);

        store.Update(settings => settings.AiChatHistoryLimit = 42);

        Assert.Equal(42, appData.Object.Config.AiChatHistoryLimit);
        appData.Verify(x => x.SaveAppConfig(), Times.Once);
        appData.Verify(x => x.SaveConfig(), Times.Never);
        appData.Verify(x => x.SaveCredentials(), Times.Never);
    }

    [Fact]
    public void FimSettingsStore_Update_SavesAppConfigOnly()
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions());
        var store = new AppOptionsFimSettingsStore(appData.Object);

        store.Update(settings => settings.FimDebounceMs = 1234);

        Assert.Equal(1234, appData.Object.Config.FimDebounceMs);
        appData.Verify(x => x.SaveAppConfig(), Times.Once);
        appData.Verify(x => x.SaveConfig(), Times.Never);
        appData.Verify(x => x.SaveCredentials(), Times.Never);
    }
}

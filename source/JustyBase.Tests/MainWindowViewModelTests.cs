using JustyBase.Common;
using JustyBase.ViewModels;

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
    public void DefaultAppOptions_DisableAutoUpdateByDefault()
    {
        var options = new AppOptions();

        Assert.False(options.AutoDownloadUpdate);
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

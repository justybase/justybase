using Dock.Model.Mvvm.Controls;
using JustyBase.Helpers;

namespace JustyBase.Tests;

public sealed class DockCapabilityHelperTests
{
    [Fact]
    public void SyncOverridesFromFlags_MirrorsCanFlagsIntoOverrides()
    {
        var tool = new Tool
        {
            CanClose = false,
            CanPin = true,
            CanFloat = false
        };

        DockCapabilityHelper.SyncOverridesFromFlags(tool);

        Assert.NotNull(tool.DockCapabilityOverrides);
        Assert.False(tool.DockCapabilityOverrides!.CanClose);
        Assert.True(tool.DockCapabilityOverrides.CanPin);
        Assert.False(tool.DockCapabilityOverrides.CanFloat);
    }

    [Fact]
    public void SyncOverridesFromFlags_NullDockable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DockCapabilityHelper.SyncOverridesFromFlags(null!));
    }
}

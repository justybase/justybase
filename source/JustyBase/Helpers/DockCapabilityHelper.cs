using Dock.Model.Core;

namespace JustyBase.Helpers;

/// <summary>
/// Dock Fluent chrome binds pin/close visibility through
/// <c>ActiveDockable.DockCapabilityOverrides.CanPin/CanClose</c>.
/// Leaving overrides null produces Avalonia "[Binding] Value is null" noise
/// (and can hide chrome buttons). Mirror the base <c>Can*</c> flags into overrides.
/// </summary>
public static class DockCapabilityHelper
{
    public static void SyncOverridesFromFlags(IDockable dockable)
    {
        ArgumentNullException.ThrowIfNull(dockable);

        dockable.DockCapabilityOverrides = new DockCapabilityOverrides
        {
            CanClose = dockable.CanClose,
            CanPin = dockable.CanPin,
            CanFloat = dockable.CanFloat,
            CanDrag = dockable.CanDrag,
            CanDrop = dockable.CanDrop,
            CanDockAsDocument = dockable.CanDockAsDocument
        };
    }
}

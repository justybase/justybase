using Avalonia.Controls;
using JustyBase.Helpers;
using JustyBase.Services.Actions;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services.Dialogs;

/// <summary>Shows Find Action and returns the chosen action. Running it stays with the caller, which
/// has the full workspace context (the active document and the main window).</summary>
public interface IActionPaletteService
{
    Task<IdeAction?> ChooseAsync(ActionContext context, CancellationToken cancellationToken = default);
}

public sealed class ActionPaletteService : IActionPaletteService
{
    private readonly ActionRegistry _registry;
    private readonly IAvaloniaSpecificHelpers _windows;
    private readonly KeymapService? _keymap;

    public ActionPaletteService(
        ActionRegistry registry,
        IAvaloniaSpecificHelpers windows,
        KeymapService? keymap = null)
    {
        _registry = registry;
        _windows = windows;
        _keymap = keymap;
    }

    public async Task<IdeAction?> ChooseAsync(
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IdeAction? chosen = null;
        Func<string, string?>? getGesture = _keymap is null ? null : _keymap.GetGesture;
        var window = new ActionPaletteWindow(
            _registry,
            context,
            action => chosen = action,
            getGesture);
        Window? owner = _windows.GetMainWindow();
        if (owner is null)
        {
            return null;
        }

        await window.ShowDialog(owner);
        return chosen;
    }
}

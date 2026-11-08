using Avalonia.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Services.Actions;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services.Dialogs;

/// <summary>Opens the keyboard-shortcut editor and owns its window.</summary>
public interface IKeymapDialogService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}

public sealed class KeymapDialogService : IKeymapDialogService
{
    private readonly KeymapService _keymap;
    private readonly IAvaloniaSpecificHelpers _windows;

    public KeymapDialogService(KeymapService keymap, IAvaloniaSpecificHelpers windows)
    {
        _keymap = keymap;
        _windows = windows;
    }

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window = new KeymapWindow(_keymap);
        Window? owner = _windows.GetMainWindow();
        if (owner is null)
        {
            window.Show();
            return;
        }

        await window.ShowDialog(owner);
    }
}

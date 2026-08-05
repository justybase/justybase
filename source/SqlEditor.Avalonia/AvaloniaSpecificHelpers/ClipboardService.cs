using Avalonia.Input.Platform;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;

namespace SqlEditor.Avalonia.AvaloniaSpecificHelpers;

public sealed class ClipboardService : IClipboardService
{

    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private IClipboard? _clipboard;
    private IClipboard? Clipboard => _clipboard ??= _avaloniaSpecificHelpers.GetClipboard();

    public ClipboardService(IAvaloniaSpecificHelpers avaloniaSpecificHelpers)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
    }
    public async Task<object?> GetDataAsync(string format)
    {
        if (Clipboard is null)
        {
            return null;
        }
        var r1 = await Clipboard.TryGetDataAsync();
        if (r1 is null)
        {
            return null;
        }
        return await r1.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(format));

        //return await Clipboard?.TryGetDataAsync(format);
    }

    public async Task<string[]> GetFormatsAsync()
    {
        if (Clipboard is null)
        {
            return [];
        }
        var res = await Clipboard.GetDataFormatsAsync();

        var results = new string[res.Count];
        int i = 0;
        foreach (var format in res)
        {
            results[i++] = format.Identifier;
        }
        return results;
    }

    public async Task<string> GetTextAsync()
    {
        if (Clipboard is null) return string.Empty;
        return await Clipboard.TryGetTextAsync() ?? string.Empty;
    }

    public async Task SetTextAsync(string txt)
    {
        if (Clipboard is not null) await Clipboard.SetTextAsync(txt);
    }
}

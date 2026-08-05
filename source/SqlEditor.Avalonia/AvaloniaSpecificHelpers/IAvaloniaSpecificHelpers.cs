using Avalonia.Input.Platform;

namespace JustyBase.Services;

public interface IAvaloniaSpecificHelpers
{
    void CloseMainWindow();
    IClipboard? GetClipboard();
    IStorageProvider? GetStorageProvider();
    Window? GetMainWindow();
    Task CopyFileToClipboard(string path);
}
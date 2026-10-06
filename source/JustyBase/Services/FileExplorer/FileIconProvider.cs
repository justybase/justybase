using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace JustyBase.Services.FileExplorer;

/// <summary>
/// F2: Avalonia-backed icon provider. Owns <c>Bitmap</c> lifetime
/// (previously owned by <c>FileExplorerViewModel</c> ctor).
/// Registered as singleton; Bitmaps live for app lifetime.
/// </summary>
public sealed class FileIconProvider : IFileIconProvider, IDisposable
{
    private readonly Bitmap _fileIcon;
    private readonly Bitmap _folderIcon;
    private readonly Bitmap _folderOpenIcon;
    private bool _disposed;

    public FileIconProvider()
    {
        using var fileStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/file.png"));
        using var folderStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/folder.png"));
        using var folderOpenStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/folder-open.png"));
#pragma warning disable CA2000 // Ownership transferred to fields; disposed with provider.
        _fileIcon = new Bitmap(fileStream);
        _folderIcon = new Bitmap(folderStream);
        _folderOpenIcon = new Bitmap(folderOpenStream);
#pragma warning restore CA2000
    }

    public object FileIcon => _fileIcon;
    public object FolderIcon => _folderIcon;
    public object FolderOpenIcon => _folderOpenIcon;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileIcon.Dispose();
        _folderIcon.Dispose();
        _folderOpenIcon.Dispose();
    }
}

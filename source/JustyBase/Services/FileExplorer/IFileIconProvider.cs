namespace JustyBase.Services.FileExplorer;

/// <summary>
/// F2: Abstraction over Avalonia <c>Bitmap</c> + <c>AssetLoader</c> for file/folder icons.
/// Lets <c>FileExplorerViewModel</c> stay testable without Avalonia imaging.
/// </summary>
public interface IFileIconProvider
{
    /// <summary>Opaque icon handles; the view converts them to <c>Bitmap</c> via converter.</summary>
    object FileIcon { get; }
    object FolderIcon { get; }
    object FolderOpenIcon { get; }
}

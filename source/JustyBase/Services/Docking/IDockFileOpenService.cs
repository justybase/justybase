using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.Common.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services.Docking;

public interface IDockFileOpenService
{
    /// <summary>
    /// Creates (or reuses) SQL document VMs for the given files without docking them.
    /// Caller must <c>InsertDockable</c>/<c>AddDockable</c> each item in <paramref name="documentsToDock"/>.
    /// </summary>
    /// <returns>The document that should become active (last opened or reused), or null.</returns>
    SqlDocumentViewModel? PrepareDocuments(
        IEnumerable<string> files,
        IList<IDockable> visibleDockables,
        out IReadOnlyList<SqlDocumentViewModel> documentsToDock);
}

public sealed class DockFileOpenService(
    IGeneralApplicationData generalApplicationData,
    IOtherHelpers otherHelpers,
    IDockSqlDocumentFactory dockSqlDocumentFactory) : IDockFileOpenService
{
    private const long LargeFileThresholdBytes = 20L * 1024L * 1024L;

    public SqlDocumentViewModel? PrepareDocuments(
        IEnumerable<string> files,
        IList<IDockable> visibleDockables,
        out IReadOnlyList<SqlDocumentViewModel> documentsToDock)
    {
        SqlDocumentViewModel? lastOpenedDocument = null;
        List<SqlDocumentViewModel> created = [];
        // Titles for untitled preview tabs use current dock count + pending creates.
        int pendingCount = visibleDockables.Count;

        foreach (string fullFileName in files)
        {
            if (generalApplicationData.TryGetOpenedDocumentVmByFilePath(fullFileName, out var openedVm)
                && openedVm is SqlDocumentViewModel openedDocument)
            {
                lastOpenedDocument = openedDocument;
                continue;
            }

            FileInfo fileInfo = new(fullFileName);
            if (fileInfo.Length >= LargeFileThresholdBytes)
            {
                lastOpenedDocument = CreatePreviewDocument(fullFileName, pendingCount + 1);
                created.Add(lastOpenedDocument);
                pendingCount++;
                continue;
            }

            lastOpenedDocument = CreateRegularDocument(fullFileName);
            created.Add(lastOpenedDocument);
            pendingCount++;
        }

        documentsToDock = created;
        return lastOpenedDocument;
    }

    private SqlDocumentViewModel CreatePreviewDocument(string fullFileName, int documentNumber)
    {
        string previewText = otherHelpers.CsvTxtPreviewer(fullFileName);
        string title = $"Document{documentNumber}";
        return dockSqlDocumentFactory.CreateDocument(title, previewText, txtPreview: true);
    }

    private SqlDocumentViewModel CreateRegularDocument(string fullFileName)
    {
        string title = Path.GetFileName(fullFileName);
        return dockSqlDocumentFactory.CreateDocument(
            title,
            filePath: fullFileName,
            fontSize: ISomeEditorOptions.DEFAULT_DOCUMENT_FONT_SIZE);
    }
}

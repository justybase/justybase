using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

public interface ILocalStateProvider
{
    void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider);
    void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider);
    (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? GetSqlEditorContextSnapshot();
    string BuildDatabaseContextSection();
    bool TryGetActiveDatabaseService(out IDatabaseService? databaseService, out string connectionName, out string databaseName, out string errorMessage);
    string BuildAttachmentMetadataSection(List<ChatAttachment>? attachments);
}

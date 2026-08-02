using JustyBase.Editor;

namespace JustyBase.Services;

public interface ISqlCodeFormatterService : ISqlAutocompleteData
{
    string SelectedConnectionName { get; set; }
    string SelectedDatabase { get; set; }

    void FormatSql(SqlCodeEditor editor);
    void InsertSnippet(SqlCodeEditor editor, string text);
}

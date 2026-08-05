using JustyBase.Editor;
using JustyBase.NetezzaSqlParser.Dialects;

namespace JustyBase.Services;

public interface ISqlCodeFormatterService : ISqlAutocompleteData
{
    string SelectedConnectionName { get; set; }
    string SelectedDatabase { get; set; }

    /// <summary>Formats the editor selection with the given SQL dialect (Db2 for Db2 documents).</summary>
    void FormatSql(SqlCodeEditor editor, SqlDialect dialect = SqlDialect.Netezza);
    void InsertSnippet(SqlCodeEditor editor, string text);
}

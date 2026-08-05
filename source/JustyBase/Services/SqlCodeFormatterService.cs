using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.Helpers;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

public class SqlCodeFormatterService : ISqlCodeFormatterService
{
    private readonly AutocompleteService _autocompleteService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private IDatabaseService _databaseService;

    public string SelectedConnectionName { get; set; }
    public string SelectedDatabase { get; set; }

    public SqlCodeFormatterService(AutocompleteService autocompleteService, IGeneralApplicationData generalApplicationData)
    {
        _autocompleteService = autocompleteService;
        _generalApplicationData = generalApplicationData;
    }

    public void FormatSql(SqlCodeEditor editor, SqlDialect dialect = SqlDialect.Netezza)
    {
        if (editor is null) return;
        
        editor.IsReadOnly = true;
        try
        {
            if (editor.SelectionLength == 0)
            {
                editor.SelectAll();
            }

            string selectedSql = editor.SelectedText;
            int start = editor.SelectionStart;
            int len = editor.SelectionLength;
            var res = NzSqlDocumentFormatter.Format(selectedSql, dialect);
            editor.Document.Replace(start, len, res);
        }
        finally
        {
            editor.IsReadOnly = false;
        }
    }

    public void InsertSnippet(SqlCodeEditor editor, string text)
    {
        if (editor is null) return;
        
        var snippet = new CodeSnippet("ABC", "DEF", text, "GHI");
        var editorSnippet = snippet.CreateAvalonEditSnippet();

        using (editor.TextArea.Document.RunUpdate())
        {
            editorSnippet.Insert(editor.TextArea);
        }
    }

    public async IAsyncEnumerable<CompletionDataSql> GetWordsList(
        string input, 
        Dictionary<string, List<string>> aliasDbTable,
        Dictionary<string, List<string>> subqueryHints,
        Dictionary<string, List<string>> withHints,
        Dictionary<string, List<string>> tempTableHints)
    {
        if (string.IsNullOrEmpty(SelectedConnectionName))
        {
            yield break;
        }

        if (_databaseService is null || _databaseService.Name != SelectedConnectionName)
        {
            _databaseService = await Task.Run(() => DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, SelectedConnectionName));
            yield return new CompletionDataSql("", "", false, Glyph.None, null);
        }

        var wordsList = _autocompleteService.GetWordsList(input, aliasDbTable, subqueryHints, withHints, tempTableHints,
            _databaseService, SelectedDatabase);
            
        foreach (var item in wordsList)
        {
            yield return item;
        }
    }
}

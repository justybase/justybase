using JustyBase.Common.Models;

namespace JustyBase.Services;

public interface ISnippetEditorService
{
    IEnumerable<SnippetModel> LoadSnippets();
    void SaveSnippets(IEnumerable<SnippetModel> models);
    SnippetModel CreateNewSnippet();
}

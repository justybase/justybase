using JustyBase.Common.Contracts;
using JustyBase.Common.Models;

namespace JustyBase.Services;

public sealed class SnippetEditorService : ISnippetEditorService
{
    private readonly IGeneralApplicationData _generalApplicationData;

    public SnippetEditorService(IGeneralApplicationData generalApplicationData)
    {
        _generalApplicationData = generalApplicationData;
    }

    public IEnumerable<SnippetModel> LoadSnippets()
    {
        foreach (var itm in _generalApplicationData.Config.AllSnippets)
        {
            yield return new SnippetModel
            {
                SnippetType = itm.Value.snippetType,
                SnippetDesc = itm.Value.Description,
                SnippetName = itm.Key,
                SnippetText = itm.Value.Text
            };
        }
    }

    public void SaveSnippets(IEnumerable<SnippetModel> models)
    {
        _generalApplicationData.Config.AllSnippets.Clear();
        _generalApplicationData.ClearTempSippetsObjects();
        foreach (var item in models)
        {
            _generalApplicationData.Config.AllSnippets[item.SnippetName] =
                (item.SnippetType, item.SnippetDesc, item.SnippetText, item.SnippetName);
        }
    }

    public SnippetModel CreateNewSnippet()
    {
        return new SnippetModel { SnippetType = SnippetModel.STANDARD_STRING, SnippetName = "<NAME>" };
    }
}

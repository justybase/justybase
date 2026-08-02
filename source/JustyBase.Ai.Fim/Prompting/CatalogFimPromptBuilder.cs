using JustyBase.Ai.Fim.Abstractions;
using JustyBase.Ai.Fim.Download;

namespace JustyBase.Ai.Fim.Prompting;

/// <summary>Routes FIM formatting to the syntax required by the selected model family.</summary>
public sealed class CatalogFimPromptBuilder : IFimPromptBuilder
{
    private readonly IFimModelCatalog _catalog;
    private readonly Func<string?> _selectedModelId;

    public CatalogFimPromptBuilder(IFimModelCatalog catalog, Func<string?> selectedModelId)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _selectedModelId = selectedModelId ?? throw new ArgumentNullException(nameof(selectedModelId));
    }

    private IFimPromptBuilder Current => _catalog.Resolve(_selectedModelId()).Family switch
    {
        "CodeGemma" => new CodeGemmaFimPromptBuilder(),
        "StarCoder2" => new StarCoderFimPromptBuilder(),
        "Codestral" => new CodestralFimPromptBuilder(),
        _ => new QwenFimPromptBuilder(),
    };

    public string ModelFamilyId => Current.ModelFamilyId;
    public IReadOnlyList<string> StopSequences => Current.StopSequences;
    public string Build(string prefix, string suffix) => Current.Build(prefix, suffix);
}

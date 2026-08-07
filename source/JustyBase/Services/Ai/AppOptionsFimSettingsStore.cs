using JustyBase.Ai.Embedded.Settings;
using JustyBase.Common.Contracts;

namespace JustyBase.Services.Ai;

/// <summary>
/// Maps the host's <see cref="AppOptions"/> FIM settings onto the shared
/// <see cref="FimSettings"/> port.
/// </summary>
public sealed class AppOptionsFimSettingsStore : IFimSettingsStore
{
    private readonly IGeneralApplicationData _generalApplicationData;

    public AppOptionsFimSettingsStore(IGeneralApplicationData generalApplicationData)
    {
        _generalApplicationData = generalApplicationData;
    }

    public FimSettings Settings => Map(_generalApplicationData.Config);

    public void Update(Action<FimSettings> mutate)
    {
        var copy = Map(_generalApplicationData.Config);
        mutate(copy);
        Apply(_generalApplicationData.Config, copy);
        _generalApplicationData.SaveConfig();
    }

    private static FimSettings Map(JustyBase.Common.AppOptions config)
    {
        return new FimSettings
        {
            EnableFimAi = config.EnableFimServer,
            FimModelId = config.FimModelId,
            FimDebounceMs = config.FimDebounceMs,
            FimMaxTokens = config.FimMaxTokens,
            FimMaxPromptTokens = config.FimMaxPromptTokens,
            FimPrefixPercentage = config.FimPrefixPercentage,
            FimSuffixPercentage = config.FimSuffixPercentage,
            FimPreset = config.FimPreset,
            FimSchemaContext = config.FimSchemaContext,
            FimSchemaContextMaxTokens = config.FimSchemaContextMaxTokens,
            FimGpuLayers = config.FimGpuLayers,
            FimCtxSize = config.FimCtxSize,
            FimPreferVulkan = config.LlamaServerPreferVulkan
        };
    }

    private static void Apply(JustyBase.Common.AppOptions config, FimSettings settings)
    {
        config.EnableFimServer = settings.EnableFimAi;
        config.FimModelId = settings.FimModelId;
        config.FimDebounceMs = settings.FimDebounceMs;
        config.FimMaxTokens = settings.FimMaxTokens;
        config.FimMaxPromptTokens = settings.FimMaxPromptTokens;
        config.FimPrefixPercentage = settings.FimPrefixPercentage;
        config.FimSuffixPercentage = settings.FimSuffixPercentage;
        config.FimPreset = settings.FimPreset;
        config.FimSchemaContext = settings.FimSchemaContext;
        config.FimSchemaContextMaxTokens = settings.FimSchemaContextMaxTokens;
        config.FimGpuLayers = settings.FimGpuLayers;
        config.FimCtxSize = settings.FimCtxSize;
        config.LlamaServerPreferVulkan = settings.FimPreferVulkan;
    }
}

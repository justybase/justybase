using JustyBase.Ai.Ports;
using JustyBase.Common.Contracts;

namespace JustyBase.Services.Ai;

/// <summary>
/// Maps the host's <see cref="AppOptions"/> chat settings onto the shared
/// <see cref="ChatSettings"/> port.
/// </summary>
public sealed class AppOptionsChatSettingsStore : IChatSettingsStore
{
    private readonly IGeneralApplicationData _generalApplicationData;

    public AppOptionsChatSettingsStore(IGeneralApplicationData generalApplicationData)
    {
        _generalApplicationData = generalApplicationData;
    }

    public ChatSettings Settings => Map(_generalApplicationData.Config);

    public void Update(Action<ChatSettings> mutate)
    {
        var copy = Map(_generalApplicationData.Config);
        mutate(copy);
        Apply(_generalApplicationData.Config, copy);
        _generalApplicationData.SaveConfig();
    }

    private static ChatSettings Map(JustyBase.Common.AppOptions config)
    {
        return new ChatSettings
        {
            EnableAiChat = config.EnableAiChat,
            ChatSessions = config.ChatSessions,
            AiChatBackendId = config.AiChatBackendId,
            AiChatOpenAiCompatibleEndpoint = config.AiChatOpenAiCompatibleEndpoint,
            AiChatOpenAiCompatibleApiKey = config.AiChatOpenAiCompatibleApiKey,
            AiChatDefaultModel = config.AiChatDefaultModel,
            AiChatDefaultReasoningEffort = config.AiChatDefaultReasoningEffort,
            AiChatDefaultMode = config.AiChatDefaultMode,
            AiChatAutoConnect = config.AiChatAutoConnect,
            AiChatHistoryLimit = config.AiChatHistoryLimit,
            AiChatSystemPromptOverride = config.AiChatSystemPromptOverride,
            AiChatTemperature = config.AiChatTemperature,
            AiChatMaxTokens = config.AiChatMaxTokens,
            AiChatRequestTimeoutMs = config.AiChatRequestTimeoutMs,
            AiChatMaxRetries = config.AiChatMaxRetries,
            AiChatPreset = config.AiChatPreset,
            AiChatPresetIsCustom = config.AiChatPresetIsCustom,
            EnableEmbeddedChatAi = config.EnableEmbeddedChatAi,
            EmbeddedChatModelId = config.EmbeddedChatModelId,
            EmbeddedChatGpuLayers = config.EmbeddedChatGpuLayers,
            EmbeddedChatCtxSize = config.EmbeddedChatCtxSize,
            EmbeddedChatAcceptedLicenseModelIds = config.EmbeddedChatAcceptedLicenseModelIds,
            LlamaServerPreferVulkan = config.LlamaServerPreferVulkan
        };
    }

    private static void Apply(JustyBase.Common.AppOptions config, ChatSettings settings)
    {
        config.EnableAiChat = settings.EnableAiChat;
        config.ChatSessions = settings.ChatSessions;
        config.AiChatBackendId = settings.AiChatBackendId;
        config.AiChatOpenAiCompatibleEndpoint = settings.AiChatOpenAiCompatibleEndpoint;
        config.AiChatOpenAiCompatibleApiKey = settings.AiChatOpenAiCompatibleApiKey;
        config.AiChatDefaultModel = settings.AiChatDefaultModel;
        config.AiChatDefaultReasoningEffort = settings.AiChatDefaultReasoningEffort;
        config.AiChatDefaultMode = settings.AiChatDefaultMode;
        config.AiChatAutoConnect = settings.AiChatAutoConnect;
        config.AiChatHistoryLimit = settings.AiChatHistoryLimit;
        config.AiChatSystemPromptOverride = settings.AiChatSystemPromptOverride;
        config.AiChatTemperature = settings.AiChatTemperature;
        config.AiChatMaxTokens = settings.AiChatMaxTokens;
        config.AiChatRequestTimeoutMs = settings.AiChatRequestTimeoutMs;
        config.AiChatMaxRetries = settings.AiChatMaxRetries;
        config.AiChatPreset = settings.AiChatPreset;
        config.AiChatPresetIsCustom = settings.AiChatPresetIsCustom;
        config.EnableEmbeddedChatAi = settings.EnableEmbeddedChatAi;
        config.EmbeddedChatModelId = settings.EmbeddedChatModelId;
        config.EmbeddedChatGpuLayers = settings.EmbeddedChatGpuLayers;
        config.EmbeddedChatCtxSize = settings.EmbeddedChatCtxSize;
        config.EmbeddedChatAcceptedLicenseModelIds = settings.EmbeddedChatAcceptedLicenseModelIds;
        config.LlamaServerPreferVulkan = settings.LlamaServerPreferVulkan;
    }
}

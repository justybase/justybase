using AvaloniaEdit;
using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Ai.Embedded.Settings;
using JustyBase.Ai.Ports;
using JustyBase.Editor;
using JustyBase.Editor.InlineCompletion;
using JustyBase.Ai.Chat;
using JustyBase.Services.Fim;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Services.Embedded;

/// <summary>
/// Registers the bundled llama.cpp (llama-server) services: GGUF stores, catalogs, subprocess
/// management, the server-based FIM provider/bridge/bootstrap, and the Embedded AI chat backend.
/// </summary>
public static class EmbeddedAiServiceCollectionExtensions
{
    public const string FimStoreKey = "fim";

    public static IServiceCollection AddEmbeddedLlamaServerServices(this IServiceCollection collection)
    {
        // Apple Silicon runs the native MLX backend (uv + mlx_lm.server); everything else uses
        // the bundled llama.cpp llama-server with GGUF models.
        var useMlx = AppleSiliconRuntime.IsSupported;

        // Catalogs.
        collection.AddSingleton<FimModelCatalog>();
        collection.AddSingleton<EmbeddedChatModelCatalog>();

        // Model stores (FIM model + embedded chat model) in the shared models directory.
        collection.AddKeyedSingleton<IModelStore>(FimStoreKey, (sp, _) =>
        {
            var catalog = sp.GetRequiredService<FimModelCatalog>();
            var settings = sp.GetRequiredService<IFimSettingsStore>();
            return useMlx
                ? (IModelStore)new HuggingFaceMlxRepoStore(catalog, () => settings.Settings.FimModelId)
                : new HuggingFaceModelStore(catalog, () => settings.Settings.FimModelId);
        });
        collection.AddKeyedSingleton<IModelStore>(EmbeddedChatBackend.ChatModelStoreKey, (sp, _) =>
        {
            var catalog = sp.GetRequiredService<EmbeddedChatModelCatalog>();
            var settings = sp.GetRequiredService<IChatSettingsStore>();
            return useMlx
                ? (IModelStore)new HuggingFaceMlxRepoStore(catalog, () => settings.Settings.EmbeddedChatModelId)
                : new HuggingFaceModelStore(catalog, () => settings.Settings.EmbeddedChatModelId);
        });

        // Runtime + subprocess manager.
        if (useMlx)
        {
            collection.AddSingleton<MlxServerRuntime>();
        }

        collection.AddSingleton(sp =>
        {
            if (useMlx)
            {
                return new LlamaServerManager(sp.GetRequiredService<MlxServerRuntime>());
            }

            var settings = sp.GetRequiredService<IChatSettingsStore>();
            return new LlamaServerManager(new LlamaServerBinaryManager(() => settings.Settings.LlamaServerPreferVulkan));
        });

        // FIM: provider + bridge + bootstrap (server-based).
        collection.AddSingleton<ICompletionProvider>(sp =>
        {
            var manager = sp.GetRequiredService<LlamaServerManager>();
            var store = sp.GetRequiredKeyedService<IModelStore>(FimStoreKey);
            if (useMlx)
            {
                return new MlxFimProvider(manager, store);
            }

            var settings = sp.GetRequiredService<IFimSettingsStore>();
            return new LlamaServerFimProvider(
                manager,
                store,
                getGpuLayers: () => ResolveFimGpuLayers(settings.Settings),
                getContextSize: () => (uint)Math.Clamp(
                    settings.Settings.FimCtxSize > 0 ? settings.Settings.FimCtxSize : 4096, 512, 131_072));
        });
        collection.AddSingleton(sp =>
        {
            var provider = sp.GetRequiredService<ICompletionProvider>();
            var settings = sp.GetRequiredService<IFimSettingsStore>();
            return new FimInlineCompletionBridge(
                provider,
                () => settings.Settings.EnableFimAi,
                () =>
                {
                    // The prompt budget must never exceed the llama-server context window,
                    // otherwise llama.cpp rejects the request ("prompt too long") and FIM
                    // silently produces nothing.
                    var ctxTokens = (int)Math.Clamp(
                        (uint)(settings.Settings.FimCtxSize > 0 ? settings.Settings.FimCtxSize : 4096),
                        512,
                        131_072);
                    var promptTokens = settings.Settings.FimMaxPromptTokens > 0
                        ? settings.Settings.FimMaxPromptTokens
                        : 1536;
                    return new FimPromptBudget(
                        Math.Min(promptTokens, ctxTokens),
                        settings.Settings.FimPrefixPercentage,
                        settings.Settings.FimSuffixPercentage,
                        settings.Settings.FimMaxTokens);
                });
        });
        collection.AddSingleton<JustyBase.Ai.Embedded.Server.IFimModelBootstrapService>(sp =>
        {
            var provider = sp.GetRequiredService<ICompletionProvider>();
            var store = sp.GetRequiredKeyedService<IModelStore>(FimStoreKey);
            var manager = sp.GetRequiredService<LlamaServerManager>();
            var bridge = sp.GetRequiredService<FimInlineCompletionBridge>();
            return new JustyBase.Ai.Embedded.Server.LlamaServerFimBootstrapService(
                provider,
                store,
                manager,
                notifyModelReady: () => bridge.NotifyModelReady());
        });

        // Embedded AI chat backend.
        collection.AddSingleton<EmbeddedChatBackend>();
        collection.AddSingleton<ILocalChatBackend>(sp => sp.GetRequiredService<EmbeddedChatBackend>());

        return collection;
    }

    private static int ResolveFimGpuLayers(FimSettings settings)
    {
        if (!settings.FimPreferVulkan)
        {
            return 0;
        }

        // Negative = auto: llama-server offloads as many layers as fit in VRAM.
        var layers = settings.FimGpuLayers;
        return layers < 0 ? -1 : Math.Clamp(layers, 0, 999);
    }
}

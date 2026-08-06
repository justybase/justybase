using AvaloniaEdit;
using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.Editor.InlineCompletion;
using JustyBase.Services.Chat;
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
    public const string ChatStoreKey = "chat";

    public static IServiceCollection AddEmbeddedLlamaServerServices(this IServiceCollection collection)
    {
        // Catalogs.
        collection.AddSingleton<FimModelCatalog>();
        collection.AddSingleton<EmbeddedChatModelCatalog>();

        // GGUF stores (FIM model + embedded chat model) in the shared models directory.
        collection.AddKeyedSingleton<IModelStore>(FimStoreKey, (sp, _) =>
        {
            var catalog = sp.GetRequiredService<FimModelCatalog>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new HuggingFaceModelStore(catalog, () => appData.Config.FimModelId);
        });
        collection.AddKeyedSingleton<IModelStore>(ChatStoreKey, (sp, _) =>
        {
            var catalog = sp.GetRequiredService<EmbeddedChatModelCatalog>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new HuggingFaceModelStore(catalog, () => appData.Config.EmbeddedChatModelId);
        });

        // llama-server binary + subprocess manager.
        collection.AddSingleton(sp =>
        {
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new LlamaServerBinaryManager(() => appData.Config.LlamaServerPreferVulkan);
        });
        collection.AddSingleton<LlamaServerManager>();

        // FIM: provider + bridge + bootstrap (server-based).
        collection.AddSingleton<ICompletionProvider>(sp =>
        {
            var manager = sp.GetRequiredService<LlamaServerManager>();
            var store = sp.GetRequiredKeyedService<IModelStore>(FimStoreKey);
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new LlamaServerFimProvider(
                manager,
                store,
                getGpuLayers: () => ResolveFimGpuLayers(appData.Config),
                getContextSize: () => (uint)Math.Clamp(
                    appData.Config.FimCtxSize > 0 ? appData.Config.FimCtxSize : 4096, 512, 131_072));
        });
        collection.AddSingleton(sp =>
        {
            var provider = sp.GetRequiredService<ICompletionProvider>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new FimInlineCompletionBridge(
                provider,
                () => appData.Config.EnableFimServer,
                () => new FimPromptBudget(
                    appData.Config.FimMaxPromptTokens,
                    appData.Config.FimPrefixPercentage,
                    appData.Config.FimSuffixPercentage,
                    appData.Config.FimMaxTokens));
        });
        collection.AddSingleton<IFimModelBootstrapService>(sp =>
        {
            var provider = sp.GetRequiredService<ICompletionProvider>();
            var store = sp.GetRequiredKeyedService<IModelStore>(FimStoreKey);
            var manager = sp.GetRequiredService<LlamaServerManager>();
            var bridge = sp.GetRequiredService<FimInlineCompletionBridge>();
            return new LlamaServerFimBootstrapService(provider, store, manager, bridge);
        });

        // Embedded AI chat backend.
        collection.AddSingleton<EmbeddedChatBackend>();
        collection.AddSingleton<ILocalChatBackend>(sp => sp.GetRequiredService<EmbeddedChatBackend>());

        return collection;
    }

    private static int ResolveFimGpuLayers(JustyBase.Common.AppOptions config)
    {
        if (!config.LlamaServerPreferVulkan)
        {
            return 0;
        }

        var layers = config.FimGpuLayers;
        return Math.Clamp(layers < 0 ? 99 : layers, 0, 999);
    }
}

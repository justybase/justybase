using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Common.Contracts;
using JustyBase.Services.Embedded;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Services.Chat;

/// <summary>
/// "Embedded (local)" AI chat backend: a bundled llama.cpp llama-server subprocess hosting the
/// selected GGUF chat model. The server exposes an OpenAI-compatible endpoint, so the whole
/// existing chat pipeline (including tool calling / agent loop) works unchanged.
/// </summary>
public sealed class EmbeddedChatBackend : ILocalChatBackend
{
    private readonly IGeneralApplicationData _appData;
    private readonly LlamaServerManager _serverManager;
    private readonly IModelStore _chatModelStore;

    public EmbeddedChatBackend(
        IGeneralApplicationData appData,
        LlamaServerManager serverManager,
        [FromKeyedServices(EmbeddedAiServiceCollectionExtensions.ChatStoreKey)] IModelStore chatModelStore)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _chatModelStore = chatModelStore ?? throw new ArgumentNullException(nameof(chatModelStore));
    }

    public string Id => "embedded";
    public string DisplayName => "Embedded (local)";

    public Uri Endpoint
    {
        get => _serverManager.ChatServer?.Endpoint ?? new Uri("http://127.0.0.1:0");
        set => _ = value; // endpoint is managed by the llama-server process
    }

    /// <summary>Executes model tool calls (approval-gated). Wired by LocalChatService.</summary>
    public Func<string, string, Task<string>>? ToolExecutor { get; set; }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            // The EnableEmbeddedChatAi master switch gates the whole backend: do not download
            // the binary or spawn a server unless the user opted in.
            if (!_appData.Config.EnableEmbeddedChatAi)
            {
                return false;
            }

            var server = _serverManager.ChatServer;
            if (server is { IsRunning: true })
            {
                return await PingServerAsync(server, ct);
            }

            if (!_chatModelStore.IsModelPresent)
            {
                return false;
            }

            var config = _appData.Config;
            var instance = await _serverManager.GetOrStartServerAsync(
                LlamaServerRole.Chat,
                _chatModelStore.LocalModelPath,
                ResolveGpuLayers(config),
                (uint)Math.Clamp(config.EmbeddedChatCtxSize > 0 ? config.EmbeddedChatCtxSize : 4096, 512, 131_072),
                progress: null,
                ct).ConfigureAwait(false);
            return await PingServerAsync(instance, ct);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> PingServerAsync(LlamaServerInstance server, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync(new Uri(server.Endpoint, "/health"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return [_chatModelStore.CurrentModel.Id];
    }

    public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true)
    {
        var server = _serverManager.ChatServer;
        if (server is null)
        {
            throw new InvalidOperationException("Embedded llama-server is not running.");
        }

        return new OpenAiCompatibleChatClient(
            server.Endpoint,
            modelId,
            apiKey: null,
            enableFunctionInvocation ? ToolExecutor : null);
    }

    private static int ResolveGpuLayers(JustyBase.Common.AppOptions config)
    {
        if (!config.LlamaServerPreferVulkan)
        {
            return 0;
        }

        var layers = config.EmbeddedChatGpuLayers;
        return Math.Clamp(layers < 0 ? 99 : layers, 0, 999);
    }
}

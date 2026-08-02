using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Net.Http.Json;

namespace JustyBase.Services.Chat;

public sealed class LmStudioChatBackend : ILocalChatBackend
{
    public string Id => "lmstudio";
    public string DisplayName => "LM Studio";
    public Uri Endpoint { get; set; } = new Uri("http://localhost:1234/v1");

    private Uri ModelsUri => new($"{Endpoint.AbsoluteUri.TrimEnd('/')}/models");

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync(ModelsUri, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.GetFromJsonAsync<OpenAiModelsResponse>(ModelsUri, ct);
            return resp?.Data?.Select(m => m.Id).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = Endpoint };
        var openAiClient = new OpenAIClient(new ApiKeyCredential("not-needed"), clientOptions);
        var chatClient = openAiClient.GetChatClient(modelId);
        IChatClient inner;
        if (enableFunctionInvocation)
        {
            inner = chatClient
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        }
        else
        {
            inner = chatClient.AsIChatClient();
        }

        var http = new HttpClient { BaseAddress = Endpoint };
        return new ThinkSuppressingChatClient(inner, http, Endpoint, modelId);
    }

    private sealed class OpenAiModelsResponse
    {
        public List<OpenAiModelEntry>? Data { get; set; }
    }

    private sealed class OpenAiModelEntry
    {
        public string Id { get; set; } = string.Empty;
    }
}

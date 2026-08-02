using Microsoft.Extensions.AI;
using OllamaSharp;

namespace JustyBase.Services.Chat;

public sealed class OllamaChatBackend : ILocalChatBackend
{
    public string Id => "ollama";
    public string DisplayName => "Ollama";
    public Uri Endpoint { get; set; } = new Uri("http://localhost:11434");

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync(new Uri(Endpoint, "/api/version"), ct);
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
            using var client = new OllamaApiClient(Endpoint);
            var models = await client.ListLocalModelsAsync(ct);
            return models.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        }
        catch
        {
            return [];
        }
    }

    public IChatClient CreateChatClient(string modelId, bool enableFunctionInvocation = true)
    {
        var ollama = new OllamaApiClient(Endpoint, modelId);
        IChatClient inner;
        if (enableFunctionInvocation)
        {
            inner = ((IChatClient)ollama)
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        }
        else
        {
            inner = ollama;
        }

        var http = new HttpClient { BaseAddress = Endpoint };
        return new ThinkSuppressingChatClient(inner, http, Endpoint, modelId);
    }
}

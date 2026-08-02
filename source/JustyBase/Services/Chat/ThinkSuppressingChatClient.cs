using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace JustyBase.Services.Chat;

/// <summary>
/// Wraps IChatClient and routes streaming calls through raw HTTP
/// to inject {"think": false} into the request body.
/// </summary>
public sealed class ThinkSuppressingChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _modelId;

    public ThinkSuppressingChatClient(IChatClient inner, HttpClient http, Uri endpoint, string modelId)
    {
        _inner = inner;
        _http = http;
        _endpoint = endpoint;
        _modelId = modelId;
    }

    public void Dispose()
    {
        _inner.Dispose();
        _http.Dispose();
    }

    public object? GetService(Type serviceType, object? key = null)
        => _inner.GetService(serviceType, key);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetResponseAsync(messages, options, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var msgList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var isOllama = _endpoint.AbsolutePath.Contains("/api/");
        var uri = isOllama
            ? new Uri(_endpoint, "/api/chat")
            : new Uri(_endpoint.AbsoluteUri.TrimEnd('/') + "/chat/completions");

        string jsonBody;
        if (isOllama)
        {
            var body = BuildOllamaBody(msgList, options);
            jsonBody = JsonSerializer.Serialize(body, ThinkSuppressingJsonContext.Default.OllamaRequest);
        }
        else
        {
            var body = BuildOpenAiBody(msgList, options);
            jsonBody = JsonSerializer.Serialize(body, ThinkSuppressingJsonContext.Default.OpenAiRequest);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        if (isOllama)
        {
            await foreach (var update in ReadOllamaStreamAsync(reader, cancellationToken).ConfigureAwait(false))
                yield return update;
        }
        else
        {
            await foreach (var update in ReadOpenAiStreamAsync(reader, cancellationToken).ConfigureAwait(false))
                yield return update;
        }
    }

    private OllamaRequest BuildOllamaBody(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var ollamaMessages = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => new ChatRequestMessage
            {
                Role = m.Role == ChatRole.System ? "system" : m.Role == ChatRole.User ? "user" : "assistant",
                Content = m.Text ?? string.Empty
            }).ToList();

        return new OllamaRequest
        {
            Model = _modelId,
            Messages = ollamaMessages,
            Stream = true,
            Think = false,
            Options = options?.MaxOutputTokens is int maxTokens
                ? new OllamaRequestOptions { NumPredict = maxTokens }
                : null
        };
    }

    private OpenAiRequest BuildOpenAiBody(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var openAiMessages = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => new ChatRequestMessage
            {
                Role = m.Role == ChatRole.System ? "system" : m.Role == ChatRole.User ? "user" : "assistant",
                Content = m.Text ?? string.Empty
            }).ToList();

        return new OpenAiRequest
        {
            Model = _modelId,
            Messages = openAiMessages,
            Stream = true,
            Think = false,
            MaxTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature
        };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReadOllamaStreamAsync(
        StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return new ChatResponseUpdate(ChatRole.Assistant, text);
            }

            if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                yield break;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReadOpenAiStreamAsync(
        StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line == "data: [DONE]") yield break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line[6..];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content))
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
                }
            }
        }
    }
}

internal sealed class ChatRequestMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

internal sealed class OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatRequestMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("think")]
    public bool Think { get; init; }

    [JsonPropertyName("options")]
    public OllamaRequestOptions? Options { get; init; }
}

internal sealed class OllamaRequestOptions
{
    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; }
}

internal sealed class OpenAiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatRequestMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("think")]
    public bool Think { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OllamaRequest))]
[JsonSerializable(typeof(OpenAiRequest))]
internal partial class ThinkSuppressingJsonContext : JsonSerializerContext
{
}

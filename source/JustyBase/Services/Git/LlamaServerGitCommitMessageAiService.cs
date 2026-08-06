using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Common.Contracts;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Services.Git;

/// <summary>
/// Uses the bundled FIM llama-server with plain text completion (no FIM tokens) to draft
/// git commit messages. The FIM catalog model is base/non-instruct, so prompts must be
/// few-shot continuation — not chat "rules" lists.
/// </summary>
public sealed class LlamaServerGitCommitMessageAiService : IGitCommitMessageAiService
{
    private readonly LlamaServerManager _serverManager;
    private readonly IModelStore _fimStore;
    private readonly IGeneralApplicationData _appData;
    private readonly HttpClient _http;

    public LlamaServerGitCommitMessageAiService(
        LlamaServerManager serverManager,
        IModelStore fimStore,
        IGeneralApplicationData appData,
        HttpClient? httpClient = null)
    {
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _fimStore = fimStore ?? throw new ArgumentNullException(nameof(fimStore));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public bool IsAvailable => _appData.Config.EnableFimServer;

    public async Task<string?> GenerateAsync(string changeContext, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(changeContext))
        {
            return null;
        }

        var server = _serverManager.FimServer;
        if (server is not { IsRunning: true })
        {
            if (!_fimStore.IsModelPresent)
            {
                return null;
            }

            // Start the FIM server on demand (model must already be downloaded in Settings).
            var config = _appData.Config;
            try
            {
                server = await _serverManager.GetOrStartServerAsync(
                    LlamaServerRole.Fim,
                    _fimStore.LocalModelPath,
                    config.LlamaServerPreferVulkan
                        ? Math.Clamp(config.FimGpuLayers < 0 ? 99 : config.FimGpuLayers, 0, 999)
                        : 0,
                    (uint)Math.Clamp(config.FimCtxSize > 0 ? config.FimCtxSize : 4096, 512, 131_072),
                    progress: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        var prompt = BuildCompletionPrompt(changeContext);
        var body = new LlamaGitCompletionRequest
        {
            Prompt = prompt,
            NPredict = 96,
            Temperature = 0.2f,
            TopP = 0.9f,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.Endpoint, "/completion"))
        {
            Content = JsonContent.Create(body, GitLlamaJsonContext.Default.LlamaGitCompletionRequest),
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync(
            GitLlamaJsonContext.Default.LlamaGitCompletionResponse,
            cancellationToken).ConfigureAwait(false);

        var raw = payload?.Content;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = CleanMessage(raw);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Few-shot text completion: the base coder continues after the last "Commit message:" line.
    /// </summary>
    private static string BuildCompletionPrompt(string changeContext)
    {
        var sb = new StringBuilder(changeContext.Length + 900);
        sb.AppendLine("### Example 1");
        sb.AppendLine("Changes:");
        sb.AppendLine(" M Services/Auth/LoginService.cs");
        sb.AppendLine("diff --git a/Services/Auth/LoginService.cs b/Services/Auth/LoginService.cs");
        sb.AppendLine("@@");
        sb.AppendLine("- return password == stored;");
        sb.AppendLine("+ return SecureEquals(password, stored);");
        sb.AppendLine();
        sb.AppendLine("Commit message:");
        sb.AppendLine("Harden login password comparison");
        sb.AppendLine();
        sb.AppendLine("### Example 2");
        sb.AppendLine("Changes:");
        sb.AppendLine("A  Views/Tools/GitView.axaml");
        sb.AppendLine("M  ViewModels/Tools/GitViewModel.cs");
        sb.AppendLine();
        sb.AppendLine("Commit message:");
        sb.AppendLine("Add Git panel commit history tree");
        sb.AppendLine();
        sb.AppendLine("### Example 3");
        sb.AppendLine("Changes:");
        sb.AppendLine(changeContext.Trim());
        sb.AppendLine();
        sb.Append("Commit message:");
        sb.AppendLine();
        return sb.ToString();
    }

    internal static string CleanMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNl = text.IndexOf('\n', StringComparison.Ordinal);
            if (firstNl >= 0)
                text = text[(firstNl + 1)..];
            int fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
            text = text.Trim();
        }

        string[] prefixes =
        [
            "Commit message:",
            "Commit Message:",
            "Message:",
            "Subject:",
        ];
        foreach (string prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                text = text[prefix.Length..].TrimStart();
        }

        if ((text.StartsWith('"') && text.EndsWith('"')) || (text.StartsWith('\'') && text.EndsWith('\'')))
            text = text[1..^1].Trim();

        // Keep subject + short body; stop if the model starts another section.
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(Math.Min(lines.Length, 6));
        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.StartsWith("### ", StringComparison.Ordinal)
                || trimmed.StartsWith("Changes:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("diff --git ", StringComparison.Ordinal)
                || trimmed.StartsWith("Example ", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (kept.Count >= 6)
                break;

            kept.Add(trimmed);
        }

        string result = string.Join(Environment.NewLine, kept).Trim();
        if (LooksLikeInstructionEcho(result))
            return string.Empty;

        return result;
    }

    private static bool LooksLikeInstructionEcho(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        string upper = text.ToUpperInvariant();
        string[] rejectPhrases =
        [
            "imperative subject",
            "optional short body",
            "markdown fences",
            "output only the commit",
            "write a concise git commit",
            "max ~72",
            "max 72",
        ];
        foreach (string phrase in rejectPhrases)
        {
            if (upper.Contains(phrase.ToUpperInvariant(), StringComparison.Ordinal))
                return true;
        }

        // Bullet / numbered instruction dump.
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int bulletish = 0;
        foreach (string line in lines)
        {
            string t = line.TrimStart();
            if (t.StartsWith("- ", StringComparison.Ordinal)
                || t.StartsWith("* ", StringComparison.Ordinal)
                || (t.Length > 2 && char.IsDigit(t[0]) && t[1] == '.'))
            {
                bulletish++;
            }
        }

        return bulletish >= 2;
    }
}

internal sealed class LlamaGitCompletionRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("n_predict")]
    public int NPredict { get; init; } = 96;

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.2f;

    [JsonPropertyName("top_p")]
    public float TopP { get; init; } = 0.9f;
}

internal sealed class LlamaGitCompletionResponse
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LlamaGitCompletionRequest))]
[JsonSerializable(typeof(LlamaGitCompletionResponse))]
internal sealed partial class GitLlamaJsonContext : JsonSerializerContext
{
}

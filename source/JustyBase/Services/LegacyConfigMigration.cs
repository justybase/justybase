using JustyBase.Common;
using System.Text.Json;

namespace JustyBase.Services;

/// <summary>
/// One-time migration of pre-llama-server config keys (EmbeddedFim*/ollama/lmstudio) onto the
/// current AppOptions schema. Legacy keys are dropped by the strict source-generated
/// deserializer, so the raw JSON must be read before deserialization.
/// </summary>
public static class LegacyConfigMigration
{
    public static void Migrate(AppOptions config, string? rawJson)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // A new-schema key present in the file means the user already ran this version —
        // never overwrite newer values with legacy ones.
        Apply(root, "EnableEmbeddedFimAi", "EnableFimServer", v => config.EnableFimServer = v.GetBoolean());
        Apply(root, "EmbeddedFimModelId", "FimModelId", v => config.FimModelId = v.GetString() ?? config.FimModelId);
        Apply(root, "EmbeddedFimDebounceMs", "FimDebounceMs", v =>
        {
            if (v.TryGetInt32(out var ms) && ms > 0)
            {
                config.FimDebounceMs = ms;
            }
        });
        Apply(root, "EmbeddedFimMaxTokens", "FimMaxTokens", v =>
        {
            if (v.TryGetInt32(out var tokens) && tokens > 0)
            {
                config.FimMaxTokens = tokens;
            }
        });
        Apply(root, "EmbeddedFimPreset", "FimPreset", v => config.FimPreset = v.GetString() ?? config.FimPreset);
        Apply(root, "EmbeddedFimMaxPromptTokens", "FimMaxPromptTokens", v =>
        {
            if (v.TryGetInt32(out var tokens) && tokens > 0)
            {
                config.FimMaxPromptTokens = tokens;
            }
        });
        Apply(root, "EmbeddedFimPrefixPercentage", "FimPrefixPercentage", v =>
        {
            if (v.TryGetDouble(out var pct) && pct > 0)
            {
                config.FimPrefixPercentage = pct;
            }
        });
        Apply(root, "EmbeddedFimSuffixPercentage", "FimSuffixPercentage", v =>
        {
            if (v.TryGetDouble(out var pct) && pct > 0)
            {
                config.FimSuffixPercentage = pct;
            }
        });
        Apply(root, "EmbeddedFimGpuLayers", "FimGpuLayers", v =>
        {
            if (v.TryGetInt32(out var layers))
            {
                config.FimGpuLayers = Math.Clamp(layers, 0, 999);
            }
        });
        Apply(root, "EmbeddedFimPreferVulkan", "LlamaServerPreferVulkan", v => config.LlamaServerPreferVulkan = v.GetBoolean());
        Apply(root, "EmbeddedFimAcceptedLicenseModelIds", "FimAcceptedLicenseModelIds", v =>
        {
            if (v.ValueKind == JsonValueKind.Array)
            {
                config.FimAcceptedLicenseModelIds = v.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();
            }
        });

        // Backend consolidation: legacy "ollama" / "lmstudio" ids → "openai-compatible".
        if (root.TryGetProperty("AiChatBackendId", out var backendId)
            && backendId.ValueKind == JsonValueKind.String)
        {
            var id = backendId.GetString();
            if (string.Equals(id, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                config.AiChatBackendId = "openai-compatible";
                if (string.IsNullOrWhiteSpace(config.AiChatOpenAiCompatibleEndpoint))
                {
                    config.AiChatOpenAiCompatibleEndpoint = "http://localhost:11434/v1";
                }
            }
            else if (string.Equals(id, "lmstudio", StringComparison.OrdinalIgnoreCase))
            {
                config.AiChatBackendId = "openai-compatible";
                if (string.IsNullOrWhiteSpace(config.AiChatOpenAiCompatibleEndpoint))
                {
                    config.AiChatOpenAiCompatibleEndpoint = "http://localhost:1234/v1";
                }
            }
        }
    }

    private static void Apply(JsonElement root, string legacyKey, string newKey, Action<JsonElement> setter)
    {
        if (root.TryGetProperty(newKey, out _))
        {
            return;
        }

        if (root.TryGetProperty(legacyKey, out var legacy) && legacy.ValueKind != JsonValueKind.Null)
        {
            setter(legacy);
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Ai.Chat;
using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Prompting;
using JustyBase.Ai.Embedded.Server;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Ai.Models;
using JustyBase.Common.Models;
using JustyBase.Ai.Services;
using JustyBase.Services;
using JustyBase.Services.Embedded;
using JustyBase.Services.Fim;
using JustyBase.Editor.InlineCompletion;
using JustyBase.PluginCommon.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;

namespace JustyBase.ViewModels.Documents;

public partial class SettingsViewModel
{
    // ============================================================
    // === AI Chat setting properties (mirror of FIM pattern) ====
    // ============================================================

    /// <summary>Master On/Off switch for AI Chat. Default off — opt-in same as EnableEmbeddedFimAi.</summary>
    public bool EnableAiChat
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.EnableAiChat = value;
        }
    }

    /// <summary>
    /// Default backend id. The UI deliberately exposes the three supported providers only.
    /// </summary>
    public string AiChatBackendId
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AiChatBackendId = string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public IReadOnlyList<AiChatBackendChoiceItem> AiChatBackendChoices { get; } =
    [
        new("codex", "Codex / ChatGPT", "Official Codex app-server using a ChatGPT account."),
        new("openai-compatible", "OpenAI Compatible", "Any OpenAI-compatible endpoint (LM Studio, Ollama /v1, llama.cpp, vLLM, …)."),
        new("embedded", "Embedded (local)", "Bundled llama.cpp llama-server with a downloaded GGUF chat model."),
    ];

    public AiChatBackendChoiceItem? SelectedAiChatBackend
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value is null)
            {
                return;
            }

            AiChatBackendId = value.Id;
        }
    }

    /// <summary>Base URL of the OpenAI-compatible backend (default LM Studio /v1).</summary>
    public string AiChatOpenAiCompatibleEndpoint
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.AiChatOpenAiCompatibleEndpoint = string.IsNullOrWhiteSpace(value)
                ? "http://localhost:1234/v1"
                : value.Trim();
        }
    }

    /// <summary>Optional bearer API key for the OpenAI-compatible backend (empty for local servers).</summary>
    public string AiChatOpenAiCompatibleApiKey
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.AiChatOpenAiCompatibleApiKey = string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }

    /// <summary>Default chat model id (Codex uses Auto; local providers resolve it via /models).</summary>
    public string AiChatDefaultModel
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AiChatDefaultModel = string.IsNullOrWhiteSpace(value)
                ? "gpt-5.6-luna"
                : value;
        }
    }

    public IReadOnlyList<AiChatSystemPromptChoiceItem> AiChatSystemPromptChoices { get; } =
        SystemPromptBuilder.Definitions
            .Select(definition => new AiChatSystemPromptChoiceItem(
                definition.Mode.ToSlug(),
                definition.DisplayName,
                definition.Prompt.Trim()))
            .ToArray();

    public IReadOnlyList<AiChatModeChoiceItem> AiChatDefaultModeChoices { get; } =
        ChatPresets.AllModes
            .Select(mode => new AiChatModeChoiceItem(mode.Id, mode.DisplayName, mode.Description))
            .ToArray();

    public AiChatModeChoiceItem? SelectedAiChatDefaultMode
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value is null)
            {
                return;
            }
            _generalApplicationData.Config.AiChatDefaultMode = value.Slug;
        }
    }

    public bool AiChatAutoConnect
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AiChatAutoConnect = value;
        }
    }

    public int AiChatHistoryLimit
    {
        get;
        set
        {
            var clamped = Math.Clamp(value < 0 ? 10 : value, 0, 100);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }
            _generalApplicationData.Config.AiChatHistoryLimit = clamped;
        }
    }

    public string AiChatSystemPromptOverride
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AiChatSystemPromptOverride = value ?? string.Empty;
        }
    }

    public double AiChatTemperature
    {
        get;
        set
        {
            var clamped = ClampAiChatTemperature(value);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }
            _generalApplicationData.Config.AiChatTemperature = clamped;
            OnPropertyChanged(nameof(AiChatTemperatureLabel));
            MarkAiChatPresetCustom();
        }
    }

    public string AiChatTemperatureLabel => AiChatTemperature.ToString("F1", System.Globalization.CultureInfo.CurrentCulture);

    public int AiChatMaxTokens
    {
        get;
        set
        {
            var clamped = ClampAiChatMaxTokens(value);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }
            _generalApplicationData.Config.AiChatMaxTokens = clamped;
            OnPropertyChanged(nameof(AiChatMaxTokensLabel));
            MarkAiChatPresetCustom();
        }
    }

    public string AiChatMaxTokensLabel => $"{AiChatMaxTokens} tokens";

    public int AiChatRequestTimeoutMs
    {
        get;
        set
        {
            var clamped = Math.Clamp(value <= 0 ? 60000 : value, 5_000, 600_000);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }
            _generalApplicationData.Config.AiChatRequestTimeoutMs = clamped;
        }
    }

    public int AiChatMaxRetries
    {
        get;
        set
        {
            var clamped = Math.Clamp(value, 0, 5);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }
            _generalApplicationData.Config.AiChatMaxRetries = clamped;
        }
    }

    // === Presets (Balanced / Precise / Creative / Custom) ===

    public IReadOnlyList<AiChatPresetChoiceItem> AiChatPresetChoices { get; } =
        ChatPresets.All
            .Select(preset => new AiChatPresetChoiceItem(preset.Id, preset.DisplayName, preset.Description))
            .ToArray();

    public AiChatPresetChoiceItem? SelectedAiChatPreset
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value is null)
            {
                return;
            }

            _generalApplicationData.Config.AiChatPreset = value.Id;
            OnPropertyChanged(nameof(SelectedAiChatPresetNotes));

            if (!_suppressAiChatSideEffects && !_applyingAiChatPreset
                && !string.Equals(value.Id, "custom", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAiChatPreset(value.Id);
            }
        }
    }

    public string SelectedAiChatPresetNotes =>
        SelectedAiChatPreset?.Notes ?? "Select a quality/cost preset.";

    private static double ClampAiChatTemperature(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return 0.7;
        }
        return Math.Clamp(value, 0.0, 2.0);
    }

    private static int ClampAiChatMaxTokens(int value)
    {
        if (value <= 0)
        {
            return 2048;
        }
        return Math.Clamp(value, 256, 32_768);
    }

    private void MigrateLegacyAiChatPreset()
    {
        var cfg = _generalApplicationData.Config;
        var preset = cfg.AiChatPreset;
        var known = string.Equals(preset, "balanced", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "precise", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "creative", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "custom", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(preset) || !known)
        {
            cfg.AiChatPreset = "balanced";
        }

        if (cfg.AiChatPresetIsCustom
            && !string.Equals(cfg.AiChatPreset, "custom", StringComparison.OrdinalIgnoreCase))
        {
            cfg.AiChatPreset = "custom";
        }
    }

    private void ApplyAiChatPreset(string presetId)
    {
        if (string.Equals(presetId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            SelectedAiChatPreset = AiChatPresetChoices.First(c => string.Equals(c.Id, "custom", StringComparison.OrdinalIgnoreCase));
            return;
        }

        var def = presetId.ToLowerInvariant() switch
        {
            "precise" => (Id: "precise", Temperature: 0.2, MaxTokens: 4096),
            "creative" => (Id: "creative", Temperature: 1.1, MaxTokens: 2048),
            _ => (Id: "balanced", Temperature: 0.7, MaxTokens: 2048),
        };

        _applyingAiChatPreset = true;
        try
        {
            SelectedAiChatPreset = AiChatPresetChoices.FirstOrDefault(c =>
                string.Equals(c.Id, def.Id, StringComparison.OrdinalIgnoreCase))
                ?? AiChatPresetChoices[0];
            AiChatTemperature = def.Temperature;
            AiChatMaxTokens = def.MaxTokens;
            _generalApplicationData.Config.AiChatPreset = def.Id;
        }
        finally
        {
            _applyingAiChatPreset = false;
        }
    }

    private void MarkAiChatPresetCustom()
    {
        if (_suppressAiChatSideEffects || _applyingAiChatPreset)
        {
            return;
        }

        if (string.Equals(_generalApplicationData.Config.AiChatPreset, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _generalApplicationData.Config.AiChatPreset = "custom";
        _generalApplicationData.Config.AiChatPresetIsCustom = true;
        var custom = AiChatPresetChoices.First(c => string.Equals(c.Id, "custom", StringComparison.OrdinalIgnoreCase));
        if (!ReferenceEquals(SelectedAiChatPreset, custom))
        {
            _applyingAiChatPreset = true;
            try
            {
                SelectedAiChatPreset = custom;
            }
            finally
            {
                _applyingAiChatPreset = false;
            }
        }
    }
}

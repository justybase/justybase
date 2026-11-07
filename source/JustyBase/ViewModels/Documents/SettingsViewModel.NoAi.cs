using CommunityToolkit.Mvvm.Input;

namespace JustyBase.ViewModels.Documents;

/// <summary>
/// No-AI build stubs for the AI settings surface. The XAML stays unchanged so the
/// settings window keeps compiling; the AI navigation entries are removed from
/// <see cref="SettingsSections"/> by the main file, so these pages are unreachable.
/// </summary>
public partial class SettingsViewModel
{
    // ── AI Chat ────────────────────────────────────────────────────────────
    public bool EnableAiChat { get; set; }
    public string AiChatBackendId { get; set; } = "codex";
    public IReadOnlyList<AiChatBackendChoiceItem> AiChatBackendChoices { get; } = AiChatBackendChoiceItem.All;
    public AiChatBackendChoiceItem? SelectedAiChatBackend { get; set; } = AiChatBackendChoiceItem.Codex;
    public string AiChatOpenAiCompatibleEndpoint { get; set; } = string.Empty;
    public string AiChatOpenAiCompatibleApiKey { get; set; } = string.Empty;
    public string AiChatDefaultModel { get; set; } = string.Empty;
    public IReadOnlyList<AiChatSystemPromptChoiceItem> AiChatSystemPromptChoices { get; } = [];
    public IReadOnlyList<AiChatModeChoiceItem> AiChatDefaultModeChoices { get; } =
    [
        AiChatModeChoiceItem.FromSlug("expert"),
        AiChatModeChoiceItem.FromSlug("sqlfix"),
        AiChatModeChoiceItem.FromSlug("simple")
    ];
    public AiChatModeChoiceItem? SelectedAiChatDefaultMode { get; set; } = AiChatModeChoiceItem.FromSlug("expert");
    public bool AiChatAutoConnect { get; set; }
    public int AiChatHistoryLimit { get; set; } = 10;
    public string AiChatSystemPromptOverride { get; set; } = string.Empty;
    public double AiChatTemperature { get; set; } = 0.2;
    public string AiChatTemperatureLabel => string.Empty;
    public int AiChatMaxTokens { get; set; } = 4096;
    public string AiChatMaxTokensLabel => string.Empty;
    public int AiChatRequestTimeoutMs { get; set; } = 60_000;
    public int AiChatMaxRetries { get; set; } = 2;
    public IReadOnlyList<AiChatPresetChoiceItem> AiChatPresetChoices { get; } = [AiChatPresetChoiceItem.FromId("balanced")];
    public AiChatPresetChoiceItem? SelectedAiChatPreset { get; set; } = AiChatPresetChoiceItem.FromId("balanced");
    public string SelectedAiChatPresetNotes => string.Empty;

    // ── Embedded chat ──────────────────────────────────────────────────────
    public bool EnableEmbeddedChatAi { get; set; }
    public IReadOnlyList<FimModelChoiceItem> EmbeddedChatModelChoices { get; } = [];
    public FimModelChoiceItem? SelectedEmbeddedChatModel { get; set; }
    public string SelectedEmbeddedChatModelNotes => string.Empty;
    public string ChatModelsDirectory => string.Empty;
    public string ChatModelDiskStatus => string.Empty;
    public bool ChatModelPresentOnDisk => false;
    public string ChatPrepareStatusMessage => string.Empty;
    public double ChatPrepareProgressValue => 0;
    public string ChatPrepareProgressPercentText => string.Empty;
    public bool ChatPrepareIsIndeterminate => false;
    public bool ChatPrepareInProgress => false;
    public bool CanDeleteEmbeddedChatModel => false;
    public int EmbeddedChatGpuLayers { get; set; } = 99;
    public bool EmbeddedChatGpuLayersEnabled => false;
    public string EmbeddedChatGpuLayersLabel => string.Empty;
    public int EmbeddedChatCtxSize { get; set; } = 4096;

    public IRelayCommand ShowEmbeddedChatModelInFolderCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand OpenSelectedEmbeddedChatModelPageCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand PrepareEmbeddedChatModelCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand CancelEmbeddedChatPrepareCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand DeleteEmbeddedChatModelCommand { get; } = new RelayCommand(static () => { });

    // ── Embedded FIM ───────────────────────────────────────────────────────
    public int EmbeddedFimDebounceMs { get; set; } = 600;
    public IReadOnlyList<FimDebounceChoiceItem> EmbeddedFimDebounceChoices { get; } = [new(600, "600 ms")];
    public FimDebounceChoiceItem? SelectedEmbeddedFimDebounce { get; set; } = new(600, "600 ms");
    public bool EnableEmbeddedFimAi { get; set; }
    public int EmbeddedFimMaxTokens { get; set; } = 256;
    public string EmbeddedFimMaxTokensLabel => string.Empty;
    public int EmbeddedFimMaxPromptTokens { get; set; } = 4096;
    public double EmbeddedFimPrefixPercentage { get; set; } = 0.65;
    public double EmbeddedFimSuffixPercentage { get; set; } = 0.35;
    public bool EmbeddedFimSchemaContext { get; set; } = true;
    public int EmbeddedFimSchemaContextMaxTokens { get; set; } = 256;
    public string EmbeddedFimSchemaContextMaxTokensLabel => string.Empty;
    public int EmbeddedFimCtxSize { get; set; } = 4096;
    public IReadOnlyList<FimPresetChoiceItem> EmbeddedFimPresetChoices { get; } = [new("balanced", "Balanced", string.Empty)];
    public FimPresetChoiceItem? SelectedEmbeddedFimPreset { get; set; } = new("balanced", "Balanced", string.Empty);
    public string SelectedEmbeddedFimPresetNotes => string.Empty;
    public bool EmbeddedFimPreferVulkan { get; set; }
    public int EmbeddedFimGpuLayers { get; set; } = 99;
    public bool EmbeddedFimGpuLayersEnabled => false;
    public string EmbeddedFimGpuLayersLabel => string.Empty;
    public IReadOnlyList<FimModelChoiceItem> FimModelChoices { get; } = [];
    public FimModelChoiceItem? SelectedEmbeddedFimModel { get; set; }
    public string SelectedEmbeddedFimModelNotes => string.Empty;
    public string FimModelsDirectory => string.Empty;
    public string FimModelDiskStatus => string.Empty;
    public bool FimModelPresentOnDisk => false;
    public string FimPrepareStatusMessage => string.Empty;
    public double FimPrepareProgressValue => 0;
    public string FimPrepareProgressPercentText => string.Empty;
    public bool FimPrepareIsIndeterminate => false;
    public bool FimPrepareInProgress => false;
    public bool CanDeleteEmbeddedFimModel => false;
    public bool CanRunEmbeddedFimBenchmark => false;
    public string FimBenchmarkResult { get; set; } = string.Empty;
    public bool CanCopyFimBenchmarkResult => false;

    public IRelayCommand ShowEmbeddedFimModelInFolderCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand OpenSelectedFimModelPageCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand PrepareEmbeddedFimModelCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand CancelEmbeddedFimPrepareCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand DeleteEmbeddedFimModelCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand RunEmbeddedFimBenchmarkCommand { get; } = new RelayCommand(static () => { });
    public IRelayCommand CopyFimBenchmarkResultCommand { get; } = new RelayCommand(static () => { });

    // ── Codex account (main file keeps the #if AI implementation) ──────────
    public bool IsCodexSignedIn => false;
    public string CodexAccountStatus => "Not available in this build";

    [RelayCommand]
    private void SignInCodex()
    {
    }

    [RelayCommand]
    private void SignOutCodex()
    {
    }
}

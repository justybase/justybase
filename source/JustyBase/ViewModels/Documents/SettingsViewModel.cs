using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Services;
#if EMBEDDED_FIM
using JustyBase.Ai.Fim.Abstractions;
using JustyBase.Ai.Fim.Benchmark;
using JustyBase.Ai.Fim.Download;
using JustyBase.Ai.Fim.Prompting;
using JustyBase.Services.Fim;
#endif
using JustyBase.ViewModels;
using JustyBase.Editor.InlineCompletion;
using JustyBase.PluginCommon.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;

namespace JustyBase.ViewModels.Documents;

public partial class SettingsViewModel : DocumentBaseVM
{
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IClipboardService _clipboardService;
    private readonly ICopilotChatService _chatService;
#if EMBEDDED_FIM
    private readonly IFimModelBootstrapService _fimBootstrap;
    private readonly IFimModelCatalog _fimCatalog;
#endif
    private bool _fimPrepareInFlight;
    private bool _suppressFimSideEffects;
    private bool _applyingFimPreset;
    private bool _fimHardwareProfiled;
    private string _suggestedFimPresetId = "Medium";
    private CancellationTokenSource? _fimPrepareCts;
    private DateTime _lastFimProgressUiUtc = DateTime.MinValue;

    private bool _suppressAiChatSideEffects;
    private bool _applyingAiChatPreset;

    public SettingsViewModel(IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDocumentCloseDecisionService documentCloseDecisionService,
        IActiveDocumentManager activeDocumentManager,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IClipboardService clipboardService,
        ICopilotChatService chatService
#if EMBEDDED_FIM
        , IFimModelBootstrapService fimBootstrap
        , IFimModelCatalog fimCatalog
#endif
        )
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _clipboardService = clipboardService;
        _chatService = chatService;
        _ = avaloniaSpecificHelpers;
#if EMBEDDED_FIM
        _fimBootstrap = fimBootstrap ?? throw new ArgumentNullException(nameof(fimBootstrap));
        _fimCatalog = fimCatalog ?? throw new ArgumentNullException(nameof(fimCatalog));
        EmbeddedFimModelChoices = _fimCatalog.Models
            .Select(static m => new FimModelChoiceItem(
                Id: m.Id,
                DisplayName: m.DisplayName,
                SizeLabel: m.ApproxSizeLabel,
                Notes: m.Notes,
                SourceUrl: m.SourceModelUrl.ToString(),
                Family: m.Family,
                RequiresLicenseAcceptance: m.RequiresLicenseAcceptance,
                LicenseName: m.LicenseName,
                LicenseUrl: m.LicenseUrl?.ToString(),
                LicenseSummary: m.LicenseSummary))
            .ToArray();
#else
        EmbeddedFimModelChoices =
        [
            new(
                Id: "qwen2.5-coder-7b",
                DisplayName: "Qwen2.5-Coder 7B (stripped build)",
                SizeLabel: "n/a",
                Notes: "Embedded FIM was stripped from this binary.",
                SourceUrl: "https://huggingface.co/Qwen/Qwen2.5-Coder-7B",
                Family: "Qwen (recommended)"),
        ];
#endif
        Title = "Settings";

        ReloadSettings();
        CleanDataFolderCommand = new RelayCommand(ClearDataFolder);
        InitializeTheme();
    }

    public bool IsCodexSignedIn => _chatService.CodexAccount?.IsAuthenticated == true;

    [ObservableProperty]
    public partial bool ShowCodexEmail { get; set; }

    public string CodexAccountStatus
        => _chatService.CodexAccount?.IsAuthenticated == true
            ? ShowCodexEmail && !string.IsNullOrWhiteSpace(_chatService.CodexAccount.Email)
                ? _chatService.CodexAccount.Email
                : $"Signed in ({_chatService.CodexAccount.Plan ?? "ChatGPT"})"
            : "Not signed in";

    [RelayCommand]
    private async Task SignInCodex()
    {
        await _chatService.StartCodexLoginAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(IsCodexSignedIn));
        OnPropertyChanged(nameof(CodexAccountStatus));
    }

    [RelayCommand]
    private async Task SignOutCodex()
    {
        await _chatService.LogoutCodexAsync().ConfigureAwait(true);
        ShowCodexEmail = false;
        OnPropertyChanged(nameof(IsCodexSignedIn));
        OnPropertyChanged(nameof(CodexAccountStatus));
    }

    [RelayCommand]
    private void ToggleShowCodexEmail()
    {
        ShowCodexEmail = !ShowCodexEmail;
        OnPropertyChanged(nameof(CodexAccountStatus));
    }

    partial void OnShowCodexEmailChanged(bool value)
    {
        OnPropertyChanged(nameof(CodexAccountStatus));
    }

    partial void InitializeTheme();

    private void ReloadSettings()
    {
        FullSettingsJsonString = JsonSerializer.Serialize(_generalApplicationData.Config, MyJsonContextAppOptions.Default.AppOptions);

        ResultRowsLimit = _generalApplicationData.Config.ResultRowsLimit;
        ConnectionTimeout = _generalApplicationData.Config.ConnectionTimeout;
        CommandTimeout = _generalApplicationData.Config.CommandTimeout;

        SepInExportedCsv = _generalApplicationData.Config.SepInExportedCsv;
        SepRowsInExportedCsv = _generalApplicationData.Config.SepRowsInExportedCsv;
        EncondingName = _generalApplicationData.Config.EncondingName;
        DecimalDelimInCsv = _generalApplicationData.Config.DecimalDelimInCsv;

        ExcelFormat = _generalApplicationData.Config.UseXlsb == true ? "xlsb" : "xlsx";
        DefaultXlsxSheetName = _generalApplicationData.Config.DefaultXlsxSheetName;
        CloseUndocked = _generalApplicationData.Config.CloseUndocked == true;

        EnableFileLogging = _generalApplicationData.Config.EnableFileLogging;
        AutocompleteOnReturn = _generalApplicationData.Config.AutocompleteOnReturn;
        ConfirmDocumentClosing = _generalApplicationData.Config.ConfirmDocumentClosing;
        LineSpacing = _generalApplicationData.Config.LineSpacing;
        ShowDetailsButton = _generalApplicationData.Config.ShowDetailsButton;
        DocumentFontName = _generalApplicationData.Config.DocumentFontName;
        ControlContentThemeFontSize = _generalApplicationData.Config.ControlContentThemeFontSize;
        CompletitionFontSize = _generalApplicationData.Config.CompletitionFontSize;
        DefaultFontSizeForDocuments = _generalApplicationData.Config.DefaultFontSizeForDocuments;

        ControlContentThemeFontSize = _generalApplicationData.Config.ControlContentThemeFontSize;
        CompletitionFontSize = _generalApplicationData.Config.CompletitionFontSize;
        DefaultFontSizeForDocuments = _generalApplicationData.Config.DefaultFontSizeForDocuments;

        UseSplashScreen = _generalApplicationData.Config.UseSplashScreen;

        AutoDownloadUpdate = _generalApplicationData.Config.AutoDownloadUpdate;
        AutoDownloadPlugins = _generalApplicationData.Config.AutoDownloadPlugins;
        AllowToLoadPlugins = _generalApplicationData.Config.AllowToLoadPlugins;
        //UpdateMitigatePaloAlto = _generalApplicationData.Config.UpdateMitigateNextGenFirewalls;
        SqlLinterEnabled = _generalApplicationData.Config.SqlLinterEnabled;
        _suppressFimSideEffects = true;
        try
        {
            MigrateLegacyEmbeddedFimPreset();
            EnableEmbeddedFimAi = _generalApplicationData.Config.EnableEmbeddedFimAi;
            SelectedEmbeddedFimDebounce = EmbeddedFimDebounceChoices.FirstOrDefault(c =>
                c.Milliseconds == ResolveEmbeddedFimDebounceMs(_generalApplicationData.Config))
                ?? EmbeddedFimDebounceChoices.First(c => c.Milliseconds == 600);
            EmbeddedFimMaxPromptTokens = ClampEmbeddedFimMaxPromptTokens(
                _generalApplicationData.Config.EmbeddedFimMaxPromptTokens);
            EmbeddedFimPrefixPercentage = ClampEmbeddedFimPercentage(
                _generalApplicationData.Config.EmbeddedFimPrefixPercentage,
                0.65);
            EmbeddedFimSuffixPercentage = ClampEmbeddedFimPercentage(
                _generalApplicationData.Config.EmbeddedFimSuffixPercentage,
                0.35);
            EmbeddedFimMaxTokens = ClampEmbeddedFimMaxTokens(_generalApplicationData.Config.EmbeddedFimMaxTokens);
            SelectedEmbeddedFimModel = EmbeddedFimModelChoices.FirstOrDefault(m =>
                string.Equals(m.Id, _generalApplicationData.Config.EmbeddedFimModelId, StringComparison.OrdinalIgnoreCase))
                ?? EmbeddedFimModelChoices[0];
            SelectedEmbeddedFimPreset = EmbeddedFimPresetChoices.FirstOrDefault(c =>
                string.Equals(c.Id, _generalApplicationData.Config.EmbeddedFimPreset, StringComparison.OrdinalIgnoreCase))
                ?? EmbeddedFimPresetChoices[1];
            EmbeddedFimPreferVulkan = _generalApplicationData.Config.EmbeddedFimPreferVulkan;
            EmbeddedFimGpuLayers = Math.Clamp(
                _generalApplicationData.Config.EmbeddedFimGpuLayers < 0
                    ? 99
                    : _generalApplicationData.Config.EmbeddedFimGpuLayers,
                0,
                999);

            EnsureFimHardwareSuggestion();
            // Auto-apply runs asynchronously after GPU detect (see BeginFimHardwareProfiling).
        }
        finally
        {
            _suppressFimSideEffects = false;
        }

        RefreshEmbeddedFimDiskStatus();

        // === AI Chat settings ===
        _suppressAiChatSideEffects = true;
        try
        {
            MigrateLegacyAiChatPreset();
            EnableAiChat = _generalApplicationData.Config.EnableAiChat;
            SelectedAiChatBackend = AiChatBackendChoiceItem.FromId(_generalApplicationData.Config.AiChatBackendId);
            AiChatDefaultModel = string.IsNullOrWhiteSpace(_generalApplicationData.Config.AiChatDefaultModel)
                || _generalApplicationData.Config.AiChatDefaultModel.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                || _generalApplicationData.Config.AiChatDefaultModel.Equals("gpt-5-mini", StringComparison.OrdinalIgnoreCase)
                ? "gpt-5.6-luna"
                : _generalApplicationData.Config.AiChatDefaultModel;
            SelectedAiChatDefaultMode = AiChatModeChoiceItem.FromSlug(_generalApplicationData.Config.AiChatDefaultMode);
            AiChatAutoConnect = _generalApplicationData.Config.AiChatAutoConnect;
            AiChatHistoryLimit = Math.Clamp(
                _generalApplicationData.Config.AiChatHistoryLimit <= 0 ? 10 : _generalApplicationData.Config.AiChatHistoryLimit,
                0, 100);
            AiChatSystemPromptOverride = _generalApplicationData.Config.AiChatSystemPromptOverride ?? string.Empty;
            AiChatTemperature = ClampAiChatTemperature(_generalApplicationData.Config.AiChatTemperature);
            AiChatMaxTokens = ClampAiChatMaxTokens(_generalApplicationData.Config.AiChatMaxTokens);
            AiChatRequestTimeoutMs = Math.Clamp(
                _generalApplicationData.Config.AiChatRequestTimeoutMs <= 0 ? 60000 : _generalApplicationData.Config.AiChatRequestTimeoutMs,
                5_000, 600_000);
            AiChatMaxRetries = Math.Clamp(_generalApplicationData.Config.AiChatMaxRetries, 0, 5);
            SelectedAiChatPreset = AiChatPresetChoiceItem.FromId(_generalApplicationData.Config.AiChatPreset);
            OnPropertyChanged(nameof(AiChatMaxTokensLabel));
            OnPropertyChanged(nameof(AiChatTemperatureLabel));
            OnPropertyChanged(nameof(SelectedAiChatPresetNotes));
        }
        finally
        {
            _suppressAiChatSideEffects = false;
        }


        LintSeverityNz002 = _generalApplicationData.Config.LintSeverityNz002;
        LintSeverityNz003 = _generalApplicationData.Config.LintSeverityNz003;
        LintSeverityNz004 = _generalApplicationData.Config.LintSeverityNz004;
        LintSeverityNz005 = _generalApplicationData.Config.LintSeverityNz005;
        LintSeverityNz008 = _generalApplicationData.Config.LintSeverityNz008;
        LintSeverityNz011 = _generalApplicationData.Config.LintSeverityNz011;
        LintSeverityNz012 = _generalApplicationData.Config.LintSeverityNz012;
        LintSeverityNz013 = _generalApplicationData.Config.LintSeverityNz013;
        LintSeverityNz015 = _generalApplicationData.Config.LintSeverityNz015;
        LintSeverityNz102 = _generalApplicationData.Config.LintSeverityNz102;

        LimitHistoryMonths = _generalApplicationData.Config.LimitHistoryMonths;
        CollapseFoldingOnStartup = _generalApplicationData.Config.CollapseFoldingOnStartup;
        UseDarkTheme = _generalApplicationData.Config.ThemeNum == 1;
    }
    public ICommand CleanDataFolderCommand { get; }
    private void ClearDataFolder()
    {
        DirectoryInfo di = new(IGeneralApplicationData.DataDirectory);

        foreach (FileInfo file in di.GetFiles())
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            }
        }
        foreach (DirectoryInfo dir in di.GetDirectories())
        {
            try
            {
                dir.Delete(true);
            }
            catch (Exception ex)
            {
                _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            }
        }
    }

    public int? ResultRowsLimit
    {
        get;
        set
        {
            if (value < 100)
            {
                value = 100;
            }
            else if (value > 10_000_000)
            {
                value = 10_000_000;
            }
            SetProperty(ref field, value);
            _generalApplicationData.Config.ResultRowsLimit = ResultRowsLimit ?? 10_000;
        }
    }

    public int ConnectionTimeout
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.ConnectionTimeout = ConnectionTimeout;
        }
    }

    public int CommandTimeout
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.CommandTimeout = CommandTimeout;
        }
    }

    [ObservableProperty]
    public partial string FullSettingsJsonString { get; set; }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            _generalApplicationData.Config = JsonSerializer.Deserialize<AppOptions>(FullSettingsJsonString);
            ErrorInfo = "Success";
        }
        catch (Exception ex)
        {
            ErrorInfo = ex.Message;
        }
    }

    [ObservableProperty]
    public partial string ErrorInfo { get; set; }

    public List<string> SepInExportedCsvList { get; set; } =
    [
        ";",",","|"
    ];

    public List<string> SepRowsInExportedCsvList { get; set; } =
    [
        "windows","linux","unix"
    ];

    public List<string> EncondingNameList { get; set; } =
    [
        "UTF-8","Unicode","ASCII","UTF32","UTF16","Latin1"
    ];

    public List<string> DecimalDelimInCsvList { get; set; } =
    [
        ".",","
    ];

    public List<string> ExcelFormatList { get; set; } =
    [
        "xlsx","xlsb"
    ];

    public string ExcelFormat
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.UseXlsb = (ExcelFormat == "xlsb");

        }
    }

    public string SepRowsInExportedCsv
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.SepRowsInExportedCsv = SepRowsInExportedCsv;

        }
    }

    public string SepInExportedCsv
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.SepInExportedCsv = SepInExportedCsv;

        }
    }

    public string EncondingName
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.EncondingName = EncondingName;
        }
    }

    public string DecimalDelimInCsv
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.DecimalDelimInCsv = DecimalDelimInCsv;
        }
    }

    public string DefaultXlsxSheetName
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.DefaultXlsxSheetName = DefaultXlsxSheetName;
        }
    }

    public bool CloseUndocked
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.CloseUndocked = CloseUndocked;
        }
    }

    public bool EnableFileLogging
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.EnableFileLogging = EnableFileLogging;
        }
    }

    public bool AutocompleteOnReturn
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AutocompleteOnReturn = AutocompleteOnReturn;
        }
    }

    public bool UseSplashScreen
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.UseSplashScreen = UseSplashScreen;
        }
    }

    public bool CollapseFoldingOnStartup
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.CollapseFoldingOnStartup = value;
        }
    }

    public bool UseDarkTheme
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            var themeNum = value ? 1 : 0;
            var changed = _generalApplicationData.Config.ThemeNum != themeNum;
            _generalApplicationData.Config.ThemeNum = themeNum;
            if (changed)
            {
                ApplyThemeMode(value);
            }
        }
    }

    public int LimitHistoryMonths
    {
        get;
        set
        {
            if (value < 1)
            {
                value = 1;
            }
            else if (value > 120)
            {
                value = 120;
            }

            SetProperty(ref field, value);
            _generalApplicationData.Config.LimitHistoryMonths = value;
        }
    }

    public bool AutoDownloadUpdate
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AutoDownloadUpdate = AutoDownloadUpdate;
        }
    }

    public bool AutoDownloadPlugins
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AutoDownloadPlugins = AutoDownloadPlugins;
        }
    }

    public bool AllowToLoadPlugins
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.AllowToLoadPlugins = AllowToLoadPlugins;
        }
    }

    public IReadOnlyList<string> LintSeverityChoices { get; } = ["Off", "Warning", "Error"];

    public bool SqlLinterEnabled
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.SqlLinterEnabled = value;
            ApplyLintSeverities();
        }
    }

    public int EmbeddedFimDebounceMs
    {
        get;
        private set => SetProperty(ref field, value);
    } = InlineCompletionController.DefaultDebounceMs;

    public IReadOnlyList<FimDebounceChoiceItem> EmbeddedFimDebounceChoices { get; } =
    [
        new(250, "250 ms"),
        new(400, "400 ms"),
        new(600, "600 ms (default)"),
        new(1000, "1 s"),
        new(2000, "2 s"),
        new(3000, "3 s"),
    ];

    public FimDebounceChoiceItem? SelectedEmbeddedFimDebounce
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value is null)
            {
                return;
            }

            var snapped = InlineCompletionController.SnapDebounceMs(value.Milliseconds);
            EmbeddedFimDebounceMs = snapped;
            _generalApplicationData.Config.EmbeddedFimDebounceMs = snapped;
            _generalApplicationData.Config.EmbeddedFimDebounceSeconds = 0;
        }
    }

    private static int ResolveEmbeddedFimDebounceMs(AppOptions config)
    {
        if (config.EmbeddedFimDebounceMs > 0)
        {
            return InlineCompletionController.SnapDebounceMs(config.EmbeddedFimDebounceMs);
        }

        if (config.EmbeddedFimDebounceSeconds > 0)
        {
            return InlineCompletionController.DebounceMsFromSeconds(config.EmbeddedFimDebounceSeconds);
        }

        return InlineCompletionController.DefaultDebounceMs;
    }

    public bool EnableEmbeddedFimAi
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.EnableEmbeddedFimAi = value;

            if (value && !_suppressFimSideEffects)
            {
#if EMBEDDED_FIM
                if (!_fimBootstrap.IsSelectedModelPresent)
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance(
                        "Embedded FIM is enabled, but the selected model is not downloaded yet. " +
                        "Download the model below in the Embedded AI (FIM) settings. " +
                        "After the download, the current SQL tab will start using FIM without restarting the application.",
                        "FIM model required");
                }
#else
                _messageForUserTools.ShowSimpleMessageBoxInstance(
                    "Embedded FIM is not available in this build.",
                    "FIM unavailable");
#endif
            }
        }
    }

    public int EmbeddedFimMaxTokens
    {
        get;
        set
        {
            var clamped = ClampEmbeddedFimMaxTokens(value);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimMaxTokens = clamped;
            OnPropertyChanged(nameof(EmbeddedFimMaxTokensLabel));
            MarkPresetCustom();
        }
    }

    public string EmbeddedFimMaxTokensLabel => $"{EmbeddedFimMaxTokens} tokens";

    public int EmbeddedFimMaxPromptTokens
    {
        get;
        set
        {
            var clamped = ClampEmbeddedFimMaxPromptTokens(value);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimMaxPromptTokens = clamped;
            MarkPresetCustom();
        }
    }

    public double EmbeddedFimPrefixPercentage
    {
        get;
        set
        {
            var clamped = ClampEmbeddedFimPercentage(value, 0.65);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimPrefixPercentage = clamped;
            MarkPresetCustom();
        }
    }

    public double EmbeddedFimSuffixPercentage
    {
        get;
        set
        {
            var clamped = ClampEmbeddedFimPercentage(value, 0.35);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimSuffixPercentage = clamped;
            MarkPresetCustom();
        }
    }

    public IReadOnlyList<FimPresetChoiceItem> EmbeddedFimPresetChoices { get; } =
    [
        new("Small", "Small", "Fast / low VRAM — 1.5B, short context"),
        new("Medium", "Medium", "Balanced — 7B, good default for iGPU Vulkan"),
        new("Large", "Large", "Highest quality context — 7B default; pick 14B if VRAM allows"),
        new("Custom", "Custom", "Fine-tuned values (no longer matches a named preset)"),
    ];

    public FimPresetChoiceItem? SelectedEmbeddedFimPreset
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value is null)
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimPreset = value.Id;
            _generalApplicationData.Config.EmbeddedFimContextWindow = value.Id == "Custom"
                ? _generalApplicationData.Config.EmbeddedFimContextWindow
                : value.Id;
            OnPropertyChanged(nameof(SelectedEmbeddedFimPresetNotes));

            if (!_suppressFimSideEffects && !_applyingFimPreset
                && !string.Equals(value.Id, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                ApplyPreset(value.Id);
            }
        }
    }

    public string SelectedEmbeddedFimPresetNotes =>
        SelectedEmbeddedFimPreset?.Notes ?? "Select a quality/speed preset.";

    public string EmbeddedFimHardwareSuggestion
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Detecting hardware…";

    public bool EmbeddedFimPreferVulkan
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimPreferVulkan = value;
            OnPropertyChanged(nameof(EmbeddedFimGpuLayersEnabled));
            if (!_suppressFimSideEffects)
            {
                FimPrepareStatusMessage = value
                    ? "Vulkan preferred — reload model (Prepare) or restart if the native backend already loaded as CPU."
                    : "CPU backend preferred — restart the app if Vulkan was already loaded this session.";
            }
        }
    }

    public int EmbeddedFimGpuLayers
    {
        get;
        set
        {
            var clamped = Math.Clamp(value < 0 ? 99 : value, 0, 999);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimGpuLayers = clamped;
            OnPropertyChanged(nameof(EmbeddedFimGpuLayersLabel));
            if (!_suppressFimSideEffects)
            {
                _ = ReloadFimModelAfterGpuChangeAsync();
            }
        }
    }

    public bool EmbeddedFimGpuLayersEnabled => EmbeddedFimPreferVulkan;

    public string EmbeddedFimGpuLayersLabel =>
        EmbeddedFimGpuLayers <= 0
            ? "0 (CPU compute)"
            : EmbeddedFimGpuLayers >= 99
                ? $"{EmbeddedFimGpuLayers} (max offload)"
                : $"{EmbeddedFimGpuLayers} layers";

    private async Task ReloadFimModelAfterGpuChangeAsync()
    {
#if EMBEDDED_FIM
        if (_fimPrepareInFlight || !FimModelPresentOnDisk)
        {
            return;
        }

        try
        {
            await _fimBootstrap.ReloadModelAsync().ConfigureAwait(true);
            RefreshEmbeddedFimDiskStatus();
            FimPrepareStatusMessage = $"Reloaded with gpu_layers={EmbeddedFimGpuLayers}.";
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            FimPrepareStatusMessage = ex.Message;
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
#endif
    }

    private static int ClampEmbeddedFimMaxTokens(int value)
    {
        if (value <= 0)
        {
            return 50;
        }

        var clamped = Math.Clamp(value, 20, 200);
        var snapped = (int)(Math.Round(clamped / 10.0) * 10);
        return Math.Clamp(snapped, 20, 200);
    }

    private static int ClampEmbeddedFimMaxPromptTokens(int value) =>
        Math.Clamp(value <= 0 ? 1536 : value, 128, 8192);

    private static double ClampEmbeddedFimPercentage(double value, double fallback)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, 0.05, 0.95);
    }

    private void MigrateLegacyEmbeddedFimPreset()
    {
        var cfg = _generalApplicationData.Config;
        var preset = cfg.EmbeddedFimPreset;
        var known = string.Equals(preset, "Small", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "Medium", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "Large", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(preset) || !known)
        {
#if EMBEDDED_FIM
            cfg.EmbeddedFimPreset = FimPresets.Normalize(cfg.EmbeddedFimContextWindow);
#else
            cfg.EmbeddedFimPreset = string.IsNullOrWhiteSpace(cfg.EmbeddedFimContextWindow)
                ? "Medium"
                : cfg.EmbeddedFimContextWindow;
#endif
        }
    }

    private void EnsureFimHardwareSuggestion()
    {
        if (_fimHardwareProfiled)
        {
            return;
        }

        _fimHardwareProfiled = true;
        EmbeddedFimHardwareSuggestion = "Detecting GPU for preset suggestion…";
        _ = BeginFimHardwareProfilingAsync();
    }

    private async Task BeginFimHardwareProfilingAsync()
    {
#if EMBEDDED_FIM
        FimGpuClass gpu;
        string suggested;
        string description;
        try
        {
            (gpu, suggested, description) = await Task.Run(() =>
            {
                var detected = FimHardwareProfiler.DetectGpuClass();
                var preset = FimHardwareProfiler.SuggestPresetId(detected);
                return (detected, preset, FimHardwareProfiler.DescribeSuggestion(detected, preset));
            }).ConfigureAwait(true);
        }
#pragma warning disable CA1031
        catch
#pragma warning restore CA1031
        {
            gpu = FimGpuClass.None;
            suggested = FimPresets.Small;
            description = FimHardwareProfiler.DescribeSuggestion(gpu, suggested);
        }

        _suggestedFimPresetId = suggested;
        EmbeddedFimHardwareSuggestion = description;

        if (!_generalApplicationData.Config.EmbeddedFimAutoPresetApplied)
        {
            ApplyPreset(_suggestedFimPresetId);
            _generalApplicationData.Config.EmbeddedFimAutoPresetApplied = true;
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        _suggestedFimPresetId = "Medium";
        EmbeddedFimHardwareSuggestion = "Hardware detection unavailable in this build.";
#endif
    }

    private void ApplyPreset(string presetId)
    {
        if (string.Equals(presetId, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            SelectedEmbeddedFimPreset = EmbeddedFimPresetChoices.First(c => c.Id == "Custom");
            return;
        }

#if EMBEDDED_FIM
        var def = FimPresets.Get(presetId);
#else
        var def = presetId.ToUpperInvariant() switch
        {
            "SMALL" => (Id: "Small", MaxPromptTokens: 512, PrefixPercentage: 0.60, SuffixPercentage: 0.40, MaxGenerationTokens: 30, ModelId: "qwen2.5-coder-1.5b"),
            "LARGE" => (Id: "Large", MaxPromptTokens: 4096, PrefixPercentage: 0.70, SuffixPercentage: 0.30, MaxGenerationTokens: 80, ModelId: "qwen2.5-coder-7b"),
            _ => (Id: "Medium", MaxPromptTokens: 1536, PrefixPercentage: 0.65, SuffixPercentage: 0.35, MaxGenerationTokens: 50, ModelId: "qwen2.5-coder-3b"),
        };
#endif

        _applyingFimPreset = true;
        try
        {
            SelectedEmbeddedFimPreset = EmbeddedFimPresetChoices.FirstOrDefault(c =>
                string.Equals(c.Id, def.Id, StringComparison.OrdinalIgnoreCase))
                ?? EmbeddedFimPresetChoices[1];
            EmbeddedFimMaxPromptTokens = def.MaxPromptTokens;
            EmbeddedFimPrefixPercentage = def.PrefixPercentage;
            EmbeddedFimSuffixPercentage = def.SuffixPercentage;
            EmbeddedFimMaxTokens = def.MaxGenerationTokens;
            var model = EmbeddedFimModelChoices.FirstOrDefault(m =>
                string.Equals(m.Id, def.ModelId, StringComparison.OrdinalIgnoreCase));
            if (model is not null)
            {
                SelectedEmbeddedFimModel = model;
            }

            _generalApplicationData.Config.EmbeddedFimPreset = def.Id;
            _generalApplicationData.Config.EmbeddedFimContextWindow = def.Id;
        }
        finally
        {
            _applyingFimPreset = false;
        }
    }

    private void MarkPresetCustom()
    {
        if (_suppressFimSideEffects || _applyingFimPreset)
        {
            return;
        }

        if (string.Equals(_generalApplicationData.Config.EmbeddedFimPreset, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _generalApplicationData.Config.EmbeddedFimPreset = "Custom";
        var custom = EmbeddedFimPresetChoices.First(c => c.Id == "Custom");
        if (!ReferenceEquals(SelectedEmbeddedFimPreset, custom))
        {
            _applyingFimPreset = true;
            try
            {
                SelectedEmbeddedFimPreset = custom;
            }
            finally
            {
                _applyingFimPreset = false;
            }
        }
    }

    [RelayCommand]
    private void ApplySuggestedFimPreset()
    {
        EnsureFimHardwareSuggestion();
        ApplyPreset(_suggestedFimPresetId);
    }

    public IReadOnlyList<FimModelChoiceItem> EmbeddedFimModelChoices { get; }

    public FimModelChoiceItem? SelectedEmbeddedFimModel
    {
        get;
        set
        {
            if (value is null)
            {
                return;
            }

            if (!_suppressFimSideEffects
                && !_applyingFimPreset
                && NeedsEmbeddedFimLicenseAcceptance(value))
            {
                _ = SelectEmbeddedFimModelWithLicenseAsync(value);
                return;
            }

            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedFimModelId = value.Id;
            OnPropertyChanged(nameof(SelectedEmbeddedFimModelNotes));
            if (!_suppressFimSideEffects && !_applyingFimPreset)
            {
                MarkPresetCustom();
                RefreshEmbeddedFimDiskStatus();
            }
            else if (!_suppressFimSideEffects)
            {
                RefreshEmbeddedFimDiskStatus();
            }
        }
    }

    public string SelectedEmbeddedFimModelNotes =>
        SelectedEmbeddedFimModel?.Notes ?? "Select a model to see details.";

    private bool NeedsEmbeddedFimLicenseAcceptance(FimModelChoiceItem model)
    {
        if (!model.RequiresLicenseAcceptance)
        {
            return false;
        }

        var accepted = _generalApplicationData.Config.EmbeddedFimAcceptedLicenseModelIds;
        return accepted is null
            || !accepted.Any(id => string.Equals(id, model.Id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SelectEmbeddedFimModelWithLicenseAsync(FimModelChoiceItem value)
    {
        var summary = value.LicenseSummary ?? $"Accept the license for {value.DisplayName}?";
        var urlLine = string.IsNullOrWhiteSpace(value.LicenseUrl) ? "" : $"\n\n{value.LicenseUrl}";
        var title = string.IsNullOrWhiteSpace(value.LicenseName)
            ? "License acceptance"
            : $"Accept {value.LicenseName}?";
        var confirm = await _messageForUserTools
            .ShowConfirmationDialogAsync(summary + urlLine, title)
            .ConfigureAwait(true);

        if (!confirm)
        {
            // ComboBox may already show the declined item — snap back to the prior selection.
            OnPropertyChanged(nameof(SelectedEmbeddedFimModel));
            return;
        }

        var list = _generalApplicationData.Config.EmbeddedFimAcceptedLicenseModelIds
            ??= [];
        if (!list.Any(id => string.Equals(id, value.Id, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(value.Id);
        }

        SelectedEmbeddedFimModel = value;
    }
    public string FimModelsDirectory
    {
        get
        {
#if EMBEDDED_FIM
            return _fimBootstrap.ModelsDirectory;
#else
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JustyBase",
                "models");
#endif
        }
    }

    public string FimModelDiskStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Model status unknown.";

    public bool FimModelPresentOnDisk
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string FimPrepareStatusMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Idle — use Download / prepare when needed.";

    public double FimPrepareProgressValue
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string FimPrepareProgressPercentText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    public bool FimPrepareIsIndeterminate
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool FimPrepareInProgress
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanDeleteEmbeddedFimModel));
                OnPropertyChanged(nameof(CanRunEmbeddedFimBenchmark));
                PrepareEmbeddedFimModelCommand.NotifyCanExecuteChanged();
                CancelEmbeddedFimPrepareCommand.NotifyCanExecuteChanged();
                DeleteEmbeddedFimModelCommand.NotifyCanExecuteChanged();
                RunEmbeddedFimBenchmarkCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanDeleteEmbeddedFimModel => FimModelPresentOnDisk && !FimPrepareInProgress;

    public bool CanRunEmbeddedFimBenchmark => FimModelPresentOnDisk && !FimPrepareInProgress;

    public string FimBenchmarkResult
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanCopyFimBenchmarkResult));
                CopyFimBenchmarkResultCommand.NotifyCanExecuteChanged();
            }
        }
    } = "";

    public bool CanCopyFimBenchmarkResult => !string.IsNullOrWhiteSpace(FimBenchmarkResult);

    [RelayCommand(CanExecute = nameof(CanCopyFimBenchmarkResult))]
    private async Task CopyFimBenchmarkResult()
    {
        if (string.IsNullOrWhiteSpace(FimBenchmarkResult))
        {
            return;
        }

        try
        {
            await _clipboardService.SetTextAsync(FimBenchmarkResult).ConfigureAwait(true);
            ReportFimPrepareProgress(
                FimPrepareProgressValue,
                "Speed test results copied to clipboard.",
                isIndeterminate: FimPrepareIsIndeterminate,
                force: true);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReportFimPrepareProgress(0, ex.Message, force: true);
        }
    }

    private void RefreshEmbeddedFimDiskStatus()
    {
#if EMBEDDED_FIM
        FimModelPresentOnDisk = _fimBootstrap.IsSelectedModelPresent;
        FimModelDiskStatus = _fimBootstrap.SelectedModelDiskStatus;
#else
        FimModelPresentOnDisk = false;
        FimModelDiskStatus =
            "Embedded FIM was stripped from this binary (-p:EnableEmbeddedFim=false).";
#endif
        OnPropertyChanged(nameof(CanDeleteEmbeddedFimModel));
        OnPropertyChanged(nameof(CanRunEmbeddedFimBenchmark));
        DeleteEmbeddedFimModelCommand.NotifyCanExecuteChanged();
        RunEmbeddedFimBenchmarkCommand.NotifyCanExecuteChanged();
    }

    private void ReportFimPrepareProgress(double fraction, string message, bool isIndeterminate = false, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force
            && !isIndeterminate
            && fraction < 1.0
            && (now - _lastFimProgressUiUtc).TotalMilliseconds < 120)
        {
            return;
        }

        _lastFimProgressUiUtc = now;
        FimPrepareStatusMessage = message;
        FimPrepareIsIndeterminate = isIndeterminate;
        FimPrepareProgressValue = Math.Clamp(fraction, 0, 1);
        FimPrepareProgressPercentText = isIndeterminate
            ? "…"
            : $"{FimPrepareProgressValue * 100:0.#}%";
    }

    [RelayCommand]
    private void ShowEmbeddedFimModelInFolder()
    {
        try
        {
#if EMBEDDED_FIM
            var dir = _fimBootstrap.EnsureModelsDirectory();
            var path = _fimBootstrap.SelectedModelLocalPath;
            if (File.Exists(path))
            {
                _messageForUserTools.ShowOrShowInExplorerHelper(path);
                return;
            }

            _messageForUserTools.OpenInExplorerHelper(dir);
#else
            var dir = FimModelsDirectory;
            Directory.CreateDirectory(dir);
            _messageForUserTools.OpenInExplorerHelper(dir);
#endif
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            FimPrepareStatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenSelectedFimModelPage()
    {
        var url = SelectedEmbeddedFimModel?.SourceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            FimPrepareStatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPrepareEmbeddedFimModel))]
    private Task PrepareEmbeddedFimModel() => BootstrapFimModelAsync();

    private bool CanPrepareEmbeddedFimModel() => !FimPrepareInProgress;

    [RelayCommand(CanExecute = nameof(FimPrepareInProgress))]
    private void CancelEmbeddedFimPrepare()
    {
        try
        {
            _fimPrepareCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        FimPrepareStatusMessage = "Cancelling…";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteEmbeddedFimModel))]
    private async Task DeleteEmbeddedFimModel()
    {
#if EMBEDDED_FIM
        var model = SelectedEmbeddedFimModel?.DisplayName ?? "selected model";
        var confirm = await _messageForUserTools.ShowConfirmationDialogAsync(
            $"Delete local GGUF for {model} from disk?\n\n{_fimBootstrap.SelectedModelLocalPath}",
            "Delete FIM model").ConfigureAwait(true);
        if (!confirm)
        {
            return;
        }

        try
        {
            await _fimBootstrap.DeleteSelectedModelAsync().ConfigureAwait(true);
            FimPrepareStatusMessage = "Model deleted from disk.";
            FimPrepareProgressValue = 0;
            FimPrepareProgressPercentText = "";
            FimPrepareIsIndeterminate = false;
            FimBenchmarkResult = "";
            RefreshEmbeddedFimDiskStatus();
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            FimPrepareStatusMessage = ex.Message;
            RefreshEmbeddedFimDiskStatus();
        }
#else
        FimPrepareStatusMessage =
            "Embedded FIM was stripped from this binary (-p:EnableEmbeddedFim=false).";
        await Task.CompletedTask.ConfigureAwait(false);
#endif
    }

    [RelayCommand(CanExecute = nameof(CanRunEmbeddedFimBenchmark))]
    private async Task RunEmbeddedFimBenchmark()
    {
        if (_fimPrepareInFlight)
        {
            return;
        }

        _fimPrepareInFlight = true;
        FimPrepareInProgress = true;
        _fimPrepareCts?.Dispose();
        _fimPrepareCts = new CancellationTokenSource();
        var ct = _fimPrepareCts.Token;
        _lastFimProgressUiUtc = DateTime.MinValue;
        FimBenchmarkResult = "";
        ReportFimPrepareProgress(0, "Starting speed test…", isIndeterminate: true, force: true);

        try
        {
#if EMBEDDED_FIM
            var progress = new Progress<FimModelProgress>(p =>
                ReportFimPrepareProgress(p.Fraction, p.Message, p.IsIndeterminate, force: p.IsIndeterminate || p.Fraction >= 1.0));

            try
            {
                var report = await _fimBootstrap.RunSpeedBenchmarkAsync(
                    EmbeddedFimMaxTokens,
                    EmbeddedFimMaxPromptTokens,
                    EmbeddedFimPrefixPercentage,
                    EmbeddedFimSuffixPercentage,
                    EmbeddedFimDebounceMs,
                    EmbeddedFimPreferVulkan ? EmbeddedFimGpuLayers : 0,
                    progress,
                    ct).ConfigureAwait(true);

                FimBenchmarkResult = FimSpeedBenchmark.FormatComparison(report);
                ReportFimPrepareProgress(1.0, "Speed test finished.", force: true);
            }
            catch (OperationCanceledException)
            {
                ReportFimPrepareProgress(0, "Speed test cancelled.", force: true);
            }
            catch (Exception ex)
            {
                ReportFimPrepareProgress(0, ex.Message, force: true);
            }
#else
            ReportFimPrepareProgress(
                0,
                "Embedded FIM was stripped from this binary (-p:EnableEmbeddedFim=false).",
                force: true);
            await Task.CompletedTask.ConfigureAwait(false);
#endif
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReportFimPrepareProgress(0, ex.Message, force: true);
        }
        finally
        {
            FimPrepareInProgress = false;
            _fimPrepareInFlight = false;
            _fimPrepareCts?.Dispose();
            _fimPrepareCts = null;
        }
    }

    private async Task BootstrapFimModelAsync()
    {
        if (_fimPrepareInFlight)
        {
            return;
        }

        _fimPrepareInFlight = true;
        FimPrepareInProgress = true;
        _fimPrepareCts?.Dispose();
        _fimPrepareCts = new CancellationTokenSource();
        var ct = _fimPrepareCts.Token;
        _lastFimProgressUiUtc = DateTime.MinValue;
        ReportFimPrepareProgress(0, "Starting download…");

        try
        {
#if EMBEDDED_FIM
            var progress = new Progress<FimModelProgress>(p =>
                ReportFimPrepareProgress(p.Fraction, p.Message, p.IsIndeterminate));

            try
            {
                await _fimBootstrap.EnsureReadyAsync(progress, ct).ConfigureAwait(true);
                if (!ct.IsCancellationRequested)
                {
                    ReportFimPrepareProgress(1.0, "Model ready.");
                }
                else
                {
                    ReportFimPrepareProgress(0, "Download cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                ReportFimPrepareProgress(0, "Download cancelled.", force: true);
            }
            catch (Exception ex)
            {
                ReportFimPrepareProgress(FimPrepareProgressValue, ex.Message, force: true);
            }

            RefreshEmbeddedFimDiskStatus();
#else
            ReportFimPrepareProgress(
                0,
                "Embedded FIM was stripped from this binary (-p:EnableEmbeddedFim=false).",
                force: true);
            await Task.CompletedTask.ConfigureAwait(false);
#endif
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReportFimPrepareProgress(0, ex.Message, force: true);
        }
        finally
        {
            FimPrepareInProgress = false;
            _fimPrepareInFlight = false;
            _fimPrepareCts?.Dispose();
            _fimPrepareCts = null;
        }
    }

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
        new("lmstudio", "LM Studio", "OpenAI-compatible local endpoint at LM Studio."),
        new("ollama", "Ollama", "Local Ollama model server."),
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
    [
        new("expert", "Expert", "Full-featured SQL assistant with schema tools."),
        new("sqlfix", "SQL Fix", "Automated diagnostics fixer — read, fix, recheck."),
        new("simple", "Simple", "Plain chat — no tools, no schema."),
    ];

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
    [
        new("balanced", "Balanced", "General-purpose — temp 0.7, 2048 tokens. Good default."),
        new("precise", "Precise", "Deterministic answers — temp 0.2, 4096 tokens. Best for SQL fixes."),
        new("creative", "Creative", "More exploratory — temp 1.1, 2048 tokens. Best for brainstorming."),
        new("custom", "Custom", "User-tuned values (no longer matches a named preset)."),
    ];

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

    public string LintSeverityNz001
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz001 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz002
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz002 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz003
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz003 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz004
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz004 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz005
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz005 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz008
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz008 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz011
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz011 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz012
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz012 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz013
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz013 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz015
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz015 = value;
            ApplyLintSeverities();
        }
    }

    public string LintSeverityNz102
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz102 = value;
            ApplyLintSeverities();
        }
    }

    private void ApplyLintSeverities()
    {
        Program.ServiceProvider?.GetService<NzLinterService>()?.ApplyLintSeveritySettings(_generalApplicationData.Config);
    }

    //public bool UpdateMitigatePaloAlto
    //{
    //    get;
    //    set
    //    {
    //        SetProperty(ref field, value);
    //        _generalApplicationData.Config.UpdateMitigateNextGenFirewalls = UpdateMitigatePaloAlto;
    //    }
    //}

    public bool ConfirmDocumentClosing
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.ConfirmDocumentClosing = ConfirmDocumentClosing;
        }
    }

    public double LineSpacing
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LineSpacing = LineSpacing;
            LineSpacingStr = $"current value: {LineSpacing:N2}";
        }
    }

    [ObservableProperty]
    public partial string LineSpacingStr { get; set; }

    public bool ShowDetailsButton
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.ShowDetailsButton = ShowDetailsButton;
        }
    }

    public string DocumentFontName
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.DocumentFontName = DocumentFontName;
            foreach (var (_, value1) in _generalApplicationData.GetDocumentsKeyValueCollection())
            {
                value1.HotDocumentViewModel?.ResetFontStyle?.Invoke();
            }
        }
    }

    [RelayCommand]
    private void ChangeLineSpacing(object parametr)
    {
        if (parametr.ToString() == "+")
        {
            LineSpacing += 0.01;
        }
        else if (parametr.ToString() == "-")
        {
            LineSpacing -= 0.01;
        }
        else
        {
            LineSpacing = 1.0;
        }

        if (LineSpacing > 1.2)
        {
            LineSpacing = 1.2;
        }
        if (LineSpacing < 0.8)
        {
            LineSpacing = 0.8;
        }
    }

    [ObservableProperty]
    public partial object SeletedOption { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    /// <summary>
    /// Section ids that match the current search (empty search => all section ids).
    /// Tree parents stay visible when any child matches.
    /// </summary>
    public IReadOnlyList<string> MatchingSectionIds => GetMatchingSectionIds(SearchText);

    public string? FirstMatchingSectionId => MatchingSectionIds.Count > 0 ? MatchingSectionIds[0] : null;

    public bool IsSectionVisible(string sectionId)
    {
        var query = SearchText?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        return MatchingSectionIds.Contains(sectionId, StringComparer.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(MatchingSectionIds));
        OnPropertyChanged(nameof(FirstMatchingSectionId));
    }

    public static IReadOnlyList<SettingsSectionDescriptor> SettingsSections { get; } =
    [
        new("General", "General", ["connection timeout", "command timeout", "timeout"]),
        new("Export", "Export data", ["export", "csv", "column separator", "row separator", "encoding", "decimal", "excel", "xlsx", "xlsb", "sheet"]),
        new("SnipettsANDkeywords", "Snippets", ["snippet", "snippets", "keywords", "edit snippets"]),
        new("SqlLinter", "SQL Linter", ["sql", "linter", "lint", "nz001", "nz002", "nz003", "nz004", "nz005", "nz008", "nz011", "nz012", "nz013", "nz015", "nz102", "select *", "where", "distribute", "cross join", "like", "truncate", "union", "join"]),
        new("EmbeddedAi", "Embedded AI (FIM)", ["fim", "ai", "autocomplete", "ghost", "llamasharp", "gguf", "qwen", "coder", "1.5b", "3b", "7b", "14b", "preset", "vulkan", "inline", "model", "codestral", "starcoder", "codegemma"]),
        new("AiChat", "AI Chat", ["chat", "ai", "ollama", "lm studio", "model", "expert", "sqlfix", "assistant", "copilot", "chatgpt", "preset", "system prompt", "temperature", "tokens", "timeout"]),
        new("Results", "Results", ["results", "rows", "limit", "rows count"]),
        new("Limits", "Limits", ["limits", "rows count limit", "result rows"]),
        new("Apperance", "Appearance", ["appearance", "theme", "color", "font", "splash", "details button", "accent", "dark", "folding", "collapse"]),
        new("Others", "Others", ["others", "clear data", "autocomplete", "confirm", "update", "plugins", "log", "errors.log", "logging", "history", "months", "retention"]),
    ];

    private static IReadOnlyList<string> GetMatchingSectionIds(string? searchText)
    {
        var query = searchText?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return SettingsSections.Select(s => s.Id).ToArray();
        }

        return SettingsSections
            .Where(s => s.Matches(query))
            .Select(s => s.Id)
            .ToArray();
    }
}

public sealed record FimDebounceChoiceItem(int Milliseconds, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record FimModelChoiceItem(
    string Id,
    string DisplayName,
    string SizeLabel,
    string Notes,
    string SourceUrl,
    string Family = "Qwen (recommended)",
    bool RequiresLicenseAcceptance = false,
    string? LicenseName = null,
    string? LicenseUrl = null,
    string? LicenseSummary = null)
{
    public override string ToString() => DisplayName;
}

public sealed record FimPresetChoiceItem(
    string Id,
    string DisplayName,
    string Notes)
{
    public override string ToString() => DisplayName;
}

public sealed record AiChatPresetChoiceItem(
    string Id,
    string DisplayName,
    string Notes)
{
    public override string ToString() => DisplayName;

    public static AiChatPresetChoiceItem FromId(string? id)
    {
        var preset = CommonAiChatPresets.All.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        return preset ?? CommonAiChatPresets.Balanced;
    }
}

public sealed record AiChatModeChoiceItem(
    string Slug,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;

    public static AiChatModeChoiceItem FromSlug(string? slug)
    {
        var resolved = JustyBase.Common.Models.ChatModeExtensions.FromSlug(slug ?? string.Empty);
        return CommonAiChatPresets.AllModes.FirstOrDefault(m =>
            string.Equals(m.Slug, resolved.ToSlug(), StringComparison.OrdinalIgnoreCase))
            ?? CommonAiChatPresets.AllModes[0];
    }
}

public sealed record AiChatBackendChoiceItem(
    string Id,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;

    public static AiChatBackendChoiceItem FromId(string? id)
    {
        return All.FirstOrDefault(backend =>
                   string.Equals(backend.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? Codex;
    }

    public static readonly AiChatBackendChoiceItem Codex =
        new("codex", "Codex / ChatGPT", "Official Codex app-server using a ChatGPT account.");

    public static readonly IReadOnlyList<AiChatBackendChoiceItem> All =
    [
        Codex,
        new("lmstudio", "LM Studio", "OpenAI-compatible local endpoint at LM Studio."),
        new("ollama", "Ollama", "Local Ollama model server."),
    ];
}

public sealed record AiChatSystemPromptChoiceItem(
    string Slug,
    string DisplayName,
    string Prompt);

internal static class CommonAiChatPresets
{
    public static readonly AiChatPresetChoiceItem Balanced = new("balanced", "Balanced", "General-purpose — temp 0.7, 2048 tokens. Good default.");
    public static readonly AiChatPresetChoiceItem Precise = new("precise", "Precise", "Deterministic answers — temp 0.2, 4096 tokens. Best for SQL fixes.");
    public static readonly AiChatPresetChoiceItem Creative = new("creative", "Creative", "More exploratory — temp 1.1, 2048 tokens. Best for brainstorming.");
    public static readonly AiChatPresetChoiceItem Custom = new("custom", "Custom", "User-tuned values (no longer matches a named preset).");

    public static readonly IReadOnlyList<AiChatPresetChoiceItem> All = [Balanced, Precise, Creative, Custom];

    public static readonly IReadOnlyList<AiChatModeChoiceItem> AllModes =
    [
        new("expert", "Expert", "Full-featured SQL assistant with schema tools."),
        new("sqlfix", "SQL Fix", "Automated diagnostics fixer — read, fix, recheck."),
        new("simple", "Simple", "Plain chat — no tools, no schema."),
    ];
}

public sealed record SettingsSectionDescriptor(string Id, string Title, IReadOnlyList<string> Keywords)
{
    public bool Matches(string query)
    {
        if (Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Id.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}

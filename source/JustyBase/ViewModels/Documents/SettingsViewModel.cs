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

public partial class SettingsViewModel : DocumentBaseVM, IDisposable
{
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IClipboardService _clipboardService;
    private readonly ICopilotChatService _chatService;
    private readonly IFimModelBootstrapService _fimBootstrap;
    private readonly FimModelCatalog _fimCatalog;
    private readonly EmbeddedChatModelCatalog _embeddedChatCatalog;
    private readonly IModelStore _embeddedChatStore;
    private readonly JustyBase.Ai.Embedded.Server.LlamaServerManager? _llamaServerManager;
    private readonly NzLinterService? _linterService;
    private bool _fimPrepareInFlight;
    private bool _chatPrepareInFlight;
    private bool _suppressFimSideEffects;
    private bool _applyingFimPreset;
    private CancellationTokenSource? _fimPrepareCts;
    private CancellationTokenSource? _chatPrepareCts;
    private DateTime _lastFimProgressUiUtc = DateTime.MinValue;

    private bool _suppressAiChatSideEffects;
    private bool _applyingAiChatPreset;

    public SettingsViewModel(IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDocumentCloseDecisionService documentCloseDecisionService,
        IActiveDocumentManager activeDocumentManager,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IClipboardService clipboardService,
        ICopilotChatService chatService,
        IFimModelBootstrapService fimBootstrap,
        FimModelCatalog fimCatalog,
        EmbeddedChatModelCatalog embeddedChatCatalog,
        [FromKeyedServices(EmbeddedChatBackend.ChatModelStoreKey)] IModelStore embeddedChatStore,
        JustyBase.Ai.Embedded.Server.LlamaServerManager? llamaServerManager = null,
        NzLinterService? linterService = null)
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _clipboardService = clipboardService;
        _chatService = chatService;
        _ = avaloniaSpecificHelpers;
        _linterService = linterService;
        _fimBootstrap = fimBootstrap ?? throw new ArgumentNullException(nameof(fimBootstrap));
        _fimCatalog = fimCatalog ?? throw new ArgumentNullException(nameof(fimCatalog));
        _embeddedChatCatalog = embeddedChatCatalog ?? throw new ArgumentNullException(nameof(embeddedChatCatalog));
        _embeddedChatStore = embeddedChatStore ?? throw new ArgumentNullException(nameof(embeddedChatStore));
        _llamaServerManager = llamaServerManager;
        FimModelChoices = _fimCatalog.Models
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
        EmbeddedChatModelChoices = _embeddedChatCatalog.Models
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
        //UpdateMitigatePaloAlto = _generalApplicationData.Config.UpdateMitigateNextGenFirewalls;
        SqlLinterEnabled = _generalApplicationData.Config.SqlLinterEnabled;
        _suppressFimSideEffects = true;
        try
        {
            MigrateLegacyEmbeddedFimPreset();
            EnableEmbeddedFimAi = _generalApplicationData.Config.EnableFimServer;
            SelectedEmbeddedFimDebounce = EmbeddedFimDebounceChoices.FirstOrDefault(c =>
                c.Milliseconds == ResolveEmbeddedFimDebounceMs(_generalApplicationData.Config))
                ?? EmbeddedFimDebounceChoices.First(c => c.Milliseconds == 600);
            EmbeddedFimMaxPromptTokens = ClampEmbeddedFimMaxPromptTokens(
                _generalApplicationData.Config.FimMaxPromptTokens);
            EmbeddedFimPrefixPercentage = ClampEmbeddedFimPercentage(
                _generalApplicationData.Config.FimPrefixPercentage,
                0.65);
            EmbeddedFimSuffixPercentage = ClampEmbeddedFimPercentage(
                _generalApplicationData.Config.FimSuffixPercentage,
                0.35);
            EmbeddedFimMaxTokens = ClampEmbeddedFimMaxTokens(_generalApplicationData.Config.FimMaxTokens);
            EmbeddedFimSchemaContext = _generalApplicationData.Config.FimSchemaContext;
            EmbeddedFimSchemaContextMaxTokens = ClampEmbeddedFimSchemaContextMaxTokens(
                _generalApplicationData.Config.FimSchemaContextMaxTokens);
            EmbeddedFimCtxSize = Math.Clamp(
                _generalApplicationData.Config.FimCtxSize < 512
                    ? 4096
                    : _generalApplicationData.Config.FimCtxSize,
                512,
                131_072);
            SelectedEmbeddedFimModel = FimModelChoices.FirstOrDefault(m =>
                string.Equals(m.Id, _generalApplicationData.Config.FimModelId, StringComparison.OrdinalIgnoreCase))
                ?? FimModelChoices[0];
            SelectedEmbeddedFimPreset = EmbeddedFimPresetChoices.FirstOrDefault(c =>
                string.Equals(c.Id, _generalApplicationData.Config.FimPreset, StringComparison.OrdinalIgnoreCase))
                ?? EmbeddedFimPresetChoices[1];
            EmbeddedFimPreferVulkan = _generalApplicationData.Config.LlamaServerPreferVulkan;
            EmbeddedFimGpuLayers = Math.Clamp(
                _generalApplicationData.Config.FimGpuLayers < 0
                    ? 99
                    : _generalApplicationData.Config.FimGpuLayers,
                0,
                999);
        }
        finally
        {
            _suppressFimSideEffects = false;
        }

        RefreshEmbeddedFimDiskStatus();

        // === Embedded chat (llama-server) settings ===
        _suppressFimSideEffects = true;
        try
        {
            EnableEmbeddedChatAi = _generalApplicationData.Config.EnableEmbeddedChatAi;
            SelectedEmbeddedChatModel = EmbeddedChatModelChoices.FirstOrDefault(m =>
                string.Equals(m.Id, _generalApplicationData.Config.EmbeddedChatModelId, StringComparison.OrdinalIgnoreCase))
                ?? EmbeddedChatModelChoices[0];
            EmbeddedChatGpuLayers = Math.Clamp(
                _generalApplicationData.Config.EmbeddedChatGpuLayers < 0
                    ? 99
                    : _generalApplicationData.Config.EmbeddedChatGpuLayers,
                0,
                999);
            EmbeddedChatCtxSize = Math.Clamp(
                _generalApplicationData.Config.EmbeddedChatCtxSize <= 0
                    ? 4096
                    : _generalApplicationData.Config.EmbeddedChatCtxSize,
                512,
                131_072);
        }
        finally
        {
            _suppressFimSideEffects = false;
        }

        RefreshEmbeddedChatDiskStatus();

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
            AiChatOpenAiCompatibleEndpoint = _generalApplicationData.Config.AiChatOpenAiCompatibleEndpoint;
            AiChatOpenAiCompatibleApiKey = _generalApplicationData.Config.AiChatOpenAiCompatibleApiKey ?? string.Empty;
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
        LintSeverityNz025 = _generalApplicationData.Config.LintSeverityNz025;
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
            _generalApplicationData.Config = JsonSerializer.Deserialize(
                FullSettingsJsonString,
                MyJsonContextAppOptions.Default.AppOptions);
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

    public string LintSeverityNz025
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _generalApplicationData.Config.LintSeverityNz025 = value;
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
        // F1: prefer injected linter; fall back to ServiceLocator for legacy paths.
        var linter = _linterService ?? Program.ServiceProvider?.GetService<NzLinterService>();
        linter?.ApplyLintSeveritySettings(_generalApplicationData.Config);
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
        new("EmbeddedAi", "Embedded AI (FIM)", ["fim", "ai", "autocomplete", "ghost", "llama", "gguf", "qwen", "coder", "1.5b", "3b", "7b", "14b", "preset", "vulkan", "inline", "model", "codestral", "starcoder", "codegemma", "llama-server", "server"]),
        new("EmbeddedAiChat", "Embedded AI (Chat)", ["chat", "ai", "embedded", "gguf", "llama", "server", "model", "gemma", "qwen", "devstral", "moe", "vulkan", "download", "gpu", "context"]),
        new("AiChat", "AI Chat", ["chat", "ai", "openai", "compatible", "endpoint", "api key", "codex", "chatgpt", "model", "expert", "sqlfix", "assistant", "copilot", "preset", "system prompt", "temperature", "tokens", "timeout", "embedded"]),
        new("Results", "Results", ["results", "rows", "limit", "rows count"]),
        new("Limits", "Limits", ["limits", "rows count limit", "result rows"]),
        new("Apperance", "Appearance", ["appearance", "theme", "color", "font", "splash", "details button", "accent", "dark", "folding", "collapse"]),
        new("Others", "Others", ["others", "clear data", "autocomplete", "confirm", "update", "plugins", "log", "errors.log", "logging", "history", "months", "retention"]),
    ];

    private static string[] GetMatchingSectionIds(string? searchText)
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

    public void Dispose()
    {
        _fimPrepareCts?.Cancel();
        _fimPrepareCts?.Dispose();
        _fimPrepareCts = null;

        _chatPrepareCts?.Cancel();
        _chatPrepareCts?.Dispose();
        _chatPrepareCts = null;

        GC.SuppressFinalize(this);
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
        var preset = ChatPresets.All.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        return preset is null
            ? new AiChatPresetChoiceItem(ChatPresets.Balanced.Id, ChatPresets.Balanced.DisplayName, ChatPresets.Balanced.Description)
            : new AiChatPresetChoiceItem(preset.Id, preset.DisplayName, preset.Description);
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
        var resolved = JustyBase.Ai.Models.ChatModeExtensions.FromSlug(slug ?? string.Empty);
        var match = ChatPresets.AllModes.FirstOrDefault(m =>
            string.Equals(m.Id, resolved.ToSlug(), StringComparison.OrdinalIgnoreCase))
            ?? ChatPresets.AllModes[0];
        return new AiChatModeChoiceItem(match.Id, match.DisplayName, match.Description);
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
        new("openai-compatible", "OpenAI Compatible", "Any OpenAI-compatible endpoint (LM Studio, Ollama /v1, llama.cpp, vLLM, …)."),
        new("embedded", "Embedded (local)", "Bundled llama.cpp llama-server with a downloaded GGUF chat model."),
    ];
}

public sealed record AiChatSystemPromptChoiceItem(
    string Slug,
    string DisplayName,
    string Prompt);

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

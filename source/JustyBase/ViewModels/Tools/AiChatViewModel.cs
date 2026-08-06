using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools.Converters;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;

public sealed partial class AiChatViewModel : Tool
{
    private const string DefaultAiChatModel = "gpt-5.6-luna";
    private const string DefaultAiChatReasoningEffort = "low";
    private static readonly ChatMode DefaultMode = ChatMode.Expert;
    
    public static ModeToBoolConverter ModeToBoolConverter => ModeToBoolConverter.Instance;
    public static BoolToColorConverter BoolToColorConverter => BoolToColorConverter.Instance;
    public static BoolToSuccessColorConverter BoolToSuccessColorConverter => BoolToSuccessColorConverter.Instance;
    
    public static FuncValueConverter<ChatMode, bool> NotDefaultModeConverter { get; } = 
        new(mode => mode != DefaultMode);

    private readonly ICopilotChatService _chatService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private readonly ISimpleLogger _logger;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IClipboardService _clipboardService;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private CancellationTokenSource? _currentStreamingCts;
    private CancellationTokenSource? _codexLoginCts;
    private CancellationTokenSource? _backendSwitchCts;
    private readonly SemaphoreSlim _backendSwitchGate = new(1, 1);
    private bool _synchronizingBackendSelection;
    private bool _synchronizingSessionSelection;
    private ChatMessage? _activeAssistantMessage;

    [ObservableProperty]
    public partial ObservableCollection<ChatMessage> Messages { get; set; } = [];

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    public partial bool IsSessionChoicePending { get; set; } = true;

    [ObservableProperty]
    public partial ObservableCollection<ChatSession> SavedSessions { get; set; } = [];

    [ObservableProperty]
    public partial ChatSession? SelectedSavedSession { get; set; }

    [ObservableProperty]
    public partial bool HasSavedSessions { get; set; }

    [ObservableProperty]
    public partial bool HasSelectedSavedSession { get; set; }

    public bool CanCompose => !IsStreaming && !IsSessionChoicePending;

    public bool CanSwitchSession => !IsStreaming && !IsSessionChoicePending;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Initializing...";

    [ObservableProperty]
    public partial string CodexAccountLabel { get; set; } = "Not signed in";

    [ObservableProperty]
    public partial bool IsCodexSignedIn { get; set; }

    [ObservableProperty]
    public partial bool ShowCodexEmail { get; set; }

    [ObservableProperty]
    public partial ChatSession CurrentSession { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableModels { get; set; } = [];

    [ObservableProperty]
    public partial string SelectedModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedModelIndex { get; set; } = -1;

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableReasoningEfforts { get; set; } = [];

    [ObservableProperty]
    public partial string SelectedReasoningEffort { get; set; } = DefaultAiChatReasoningEffort;

    [ObservableProperty]
    public partial int SelectedReasoningEffortIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsCodexBackend { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableBackends { get; set; } = [];

    [ObservableProperty]
    public partial int SelectedBackendIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string CurrentThinkingContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<ChatAttachment> PendingAttachments { get; set; } = [];

    [ObservableProperty]
    public partial bool HasPendingAttachments { get; set; }

    [ObservableProperty]
    public partial ChatMode CurrentMode { get; set; } = ChatMode.Expert;

    [ObservableProperty]
    public partial string CurrentModeDisplayName { get; set; } = "SQL Expert";

    [ObservableProperty]
    public partial int SelectedModeIndex { get; set; } = 0;

    [ObservableProperty]
    public partial TodoList CurrentTodoList { get; set; } = new();

    [ObservableProperty]
    public partial bool HasTodoItems { get; set; }

    [ObservableProperty]
    public partial bool ShowTodoPanel { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ChatModeConfig> AvailableModes { get; set; } = [];

    [ObservableProperty]
    public partial bool ShowSlashCommandMenu { get; set; }

    [ObservableProperty]
    public partial string SlashCommandFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowMentionMenu { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MentionItem> MentionSuggestions { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<SlashCommand> AvailableSlashCommands { get; set; } = [];

    public AiChatViewModel(
        IFactory factory,
        ICopilotChatService chatService,
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver,
        ISimpleLogger logger,
        IMessageForUserTools messageForUserTools,
        IClipboardService clipboardService,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers)
    {
        Factory = factory;
        _chatService = chatService;
        _generalApplicationData = generalApplicationData;
        _databaseServiceResolver = databaseServiceResolver;
        _logger = logger;
        _messageForUserTools = messageForUserTools;
        _clipboardService = clipboardService;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;

        Title = "AI Chat";
        Id = "AiChat";
        CanClose = false;
        CanPin = true;
        CanFloat = false;
        DockCapabilityHelper.SyncOverridesFromFlags(this);

        _chatService.SetCurrentSqlProvider(GetCurrentSql);
        _chatService.SetSqlEditorContextProvider(GetCurrentSqlEditorContext);
        _chatService.SetSqlEditorBufferUpdater(UpdateCurrentSqlBuffer);
        _chatService.SetActiveSqlContextProvider(GetActiveSqlContext);
        _chatService.SetToolConfirmationHandler(HandleToolConfirmationAsync);

        PendingAttachments.CollectionChanged += (_, _) => HasPendingAttachments = PendingAttachments.Count > 0;

        foreach (var mode in ChatModeConfig.AllModes)
        {
            AvailableModes.Add(mode);
        }

        foreach (var cmd in SlashCommand.BuiltInCommands)
        {
            AvailableSlashCommands.Add(cmd);
        }

        // Populate backends. Connect lazily on first use unless auto-connect is enabled.
        AvailableBackends.Clear();
        foreach (var (_, name) in _chatService.AvailableBackends)
        {
            AvailableBackends.Add(name);
        }
        SynchronizeSelectedBackendIndex(_generalApplicationData.Config.AiChatBackendId);

        RefreshCodexAccountState();
        _ = RefreshCodexAccountAsync();

        // Apply configured default mode (expert / sqlfix / simple) to new sessions.
        var config = _generalApplicationData.Config;
        var configuredModel = ResolveConfiguredModel(config);
        SelectedModel = configuredModel;
        SelectedReasoningEffort = string.IsNullOrWhiteSpace(config.AiChatDefaultReasoningEffort)
            ? DefaultAiChatReasoningEffort
            : config.AiChatDefaultReasoningEffort;
        CurrentMode = ChatModeExtensions.FromSlug(config.AiChatDefaultMode);

        // Keep the last provider/model/reasoning selection visible before the
        // first network probe.  The chat panel must not look uninitialized just
        // because lazy connection is enabled.
        IsCodexBackend = string.Equals(config.AiChatBackendId, "codex", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(SelectedModel))
        {
            AvailableModels.Add(SelectedModel);
            SelectedModelIndex = 0;
        }
        if (IsCodexBackend && !string.IsNullOrWhiteSpace(SelectedReasoningEffort))
        {
            AvailableReasoningEfforts.Add(SelectedReasoningEffort);
            SelectedReasoningEffortIndex = 0;
        }

        LoadChatHistory();

        StatusMessage = "AI idle — choose a provider or send a message";
        IsConnected = false;

        // The selected provider is part of the chat's startup state. Connect in the
        // background when AiChatAutoConnect is on so the panel is usable without a dummy
        // first message; otherwise connect lazily on first send (EnsureConnectedAsync).
        if (_generalApplicationData.Config.AiChatAutoConnect)
        {
            _ = InitializeAsync();
        }
    }

    private ChatMessage? _pendingConfirmationMessage;

    private async Task<bool> HandleToolConfirmationAsync(string toolName, string toolArgs)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            var confirmationMessage = new ChatMessage
            {
                Role = "tool-confirmation",
                Content = $"The model wants to use a tool. Allow execution?",
                Timestamp = DateTime.Now,
                IsToolConfirmation = true,
                ToolName = toolName,
                ToolArgs = toolArgs,
                ConfirmationPending = true
            };

            confirmationMessage.ConfirmationTcs = tcs;
            _pendingConfirmationMessage = confirmationMessage;

            Messages.Add(confirmationMessage);
            StatusMessage = $"Waiting for tool approval: {toolName}";
        });

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(5)));
        var result = completedTask == tcs.Task && tcs.Task.Result;
        
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            if (_pendingConfirmationMessage != null)
            {
                _pendingConfirmationMessage.ConfirmationPending = false;
                _pendingConfirmationMessage.Content = completedTask == tcs.Task
                    ? (result ? $"✓ Tool '{toolName}' approved" : $"✗ Tool '{toolName}' denied")
                    : $"✗ Tool '{toolName}' denied (approval timeout)";
            }
            _pendingConfirmationMessage = null;
            StatusMessage = completedTask == tcs.Task
                ? (result ? $"Tool approved: {toolName}" : $"Tool denied: {toolName}")
                : $"Tool approval timeout: {toolName}";
        });
        
        return result;
    }

    [RelayCommand]
    private void ConfirmTool(string allowValue)
    {
        if (!bool.TryParse(allowValue, out var allow))
        {
            StatusMessage = "Invalid tool confirmation response.";
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ConfirmTool] Called with allow={allow}, pendingMessage={_pendingConfirmationMessage != null}");
        
        if (_pendingConfirmationMessage?.ConfirmationTcs != null)
        {
            _pendingConfirmationMessage.ConfirmationPending = false;
            _pendingConfirmationMessage.Content = allow 
                ? $"✓ Tool '{_pendingConfirmationMessage.ToolName}' approved"
                : $"✗ Tool '{_pendingConfirmationMessage.ToolName}' denied";
            _pendingConfirmationMessage.ConfirmationTcs.TrySetResult(allow);
            _pendingConfirmationMessage = null;
        }
    }

    private string? GetCurrentSql()
    {
        if (Factory is IActiveDocumentManager docManager && docManager.ActiveSqlDocumentViewModel is { } docVm)
        {
            return docVm.GetCurrentTextFunc?.Invoke() ?? docVm.SqlEditor?.Text;
        }
        return null;
    }

    private (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? GetCurrentSqlEditorContext()
    {
        if (Factory is not IActiveDocumentManager docManager || docManager.ActiveSqlDocumentViewModel is not { } docVm || docVm.SqlEditor is null)
        {
            return null;
        }

        var editor = docVm.SqlEditor;
        var fullText = docVm.GetCurrentTextFunc?.Invoke() ?? editor.Text ?? string.Empty;
        var selectedText = editor.SelectedText ?? string.Empty;
        return (fullText, selectedText, editor.SelectionStart, editor.SelectionLength, editor.CaretOffset);
    }

    private bool UpdateCurrentSqlBuffer(string updatedSql)
    {
        if (Factory is not IActiveDocumentManager docManager || docManager.ActiveSqlDocumentViewModel is not { } docVm || docVm.SqlEditor is null)
        {
            return false;
        }

        var applyResult = false;
        void Apply()
        {
            docVm.SqlEditor.Text = updatedSql ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(docVm.Title) && !docVm.Title.EndsWith('*'))
            {
                docVm.Title += "*";
            }
            applyResult = true;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return applyResult;
        }

        Exception? applyError = null;
        using var done = new ManualResetEventSlim(false);
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try { Apply(); }
            catch (Exception ex) { applyError = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(5))) // ManualResetEventSlim
        {
            return false;
        }
        if (applyError is not null)
        {
            throw applyError;
        }

        return applyResult;
    }

    private (string ConnectionName, string DatabaseName)? GetActiveSqlContext()
    {
        if (Factory is IActiveDocumentManager docManager && docManager.ActiveSqlDocumentViewModel is { } docVm)
        {
            var connectionName = docVm.SelectedConnectionName;
            if (string.IsNullOrWhiteSpace(connectionName))
            {
                return null;
            }

            return (connectionName, docVm.SelectedDatabase ?? string.Empty);
        }

        return null;
    }

    partial void OnSelectedModelChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _generalApplicationData.Config.AiChatDefaultModel = value;
            PersistAiChatSelection();
        }

        if (IsCodexBackend && !IsStreaming)
            _ = RefreshReasoningEffortsAsync(value);
    }

    partial void OnSelectedReasoningEffortChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _generalApplicationData.Config.AiChatDefaultReasoningEffort = value;
        PersistAiChatSelection();
    }

    partial void OnSelectedModelIndexChanged(int value)
    {
        if (value < 0 || value >= AvailableModels.Count)
            return;

        var model = AvailableModels[value];
        if (!string.Equals(SelectedModel, model, StringComparison.Ordinal))
            SelectedModel = model;
    }

    partial void OnSelectedReasoningEffortIndexChanged(int value)
    {
        if (value < 0 || value >= AvailableReasoningEfforts.Count)
            return;

        SelectedReasoningEffort = AvailableReasoningEfforts[value];
    }

    partial void OnSelectedBackendIndexChanged(int value)
    {
        if (_synchronizingBackendSelection || value < 0 || value >= AvailableBackends.Count) return;
        var backendId = _chatService.AvailableBackends[value].Id;
        _generalApplicationData.Config.AiChatBackendId = backendId;
        PersistAiChatSelection();
        _ = SwitchBackendAsync(backendId);
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCompose));
        OnPropertyChanged(nameof(CanSwitchSession));
    }

    partial void OnIsSessionChoicePendingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCompose));
        OnPropertyChanged(nameof(CanSwitchSession));
    }

    partial void OnSelectedSavedSessionChanged(ChatSession? value)
    {
        HasSelectedSavedSession = value is not null;
        if (_synchronizingSessionSelection)
            return;
        if (value is null || value.SessionId == CurrentSession.SessionId || IsStreaming)
            return;
        OpenSavedSession(value);
    }

    private void PersistAiChatSelection()
    {
        try
        {
            _generalApplicationData.SaveConfig();
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    private string ResolveConfiguredModel(JustyBase.Common.AppOptions config)
    {
        var configuredModel = config.AiChatDefaultModel;
        if (string.IsNullOrWhiteSpace(configuredModel)
            || configuredModel.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            || configuredModel.Equals("gpt-5-mini", StringComparison.OrdinalIgnoreCase))
        {
            configuredModel = DefaultAiChatModel;
            config.AiChatDefaultModel = configuredModel;
        }

        return configuredModel;
    }

    private async Task SwitchBackendAsync(string backendId)
    {
        var switchCts = new CancellationTokenSource();
        var previousSwitchCts = Interlocked.Exchange(ref _backendSwitchCts, switchCts);
        previousSwitchCts?.Cancel();

        await _backendSwitchGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (switchCts.IsCancellationRequested)
                return;

            if (IsStreaming)
            {
                _currentStreamingCts?.Cancel();
                for (var i = 0; i < 80 && IsStreaming && !switchCts.IsCancellationRequested; i++)
                    await Task.Delay(25, switchCts.Token);
            }

            if (switchCts.IsCancellationRequested)
                return;

            StatusMessage = "Switching backend...";
            var success = await _chatService.SwitchBackendAsync(backendId);
            if (switchCts.IsCancellationRequested)
                return;

            IsCodexBackend = success && string.Equals(backendId, "codex", StringComparison.OrdinalIgnoreCase);
            if (success)
            {
                await RefreshModelsAsync();
                await RefreshReasoningEffortsAsync(SelectedModel);
                SynchronizeSelectedBackendIndex(_chatService.ActiveBackendId ?? backendId);
            }
            else
            {
                // The switch failed — never leave the previous backend's model list visible.
                AvailableModels.Clear();
                SelectedModelIndex = -1;
                AvailableReasoningEfforts.Clear();
                SelectedReasoningEffortIndex = -1;
                // Keep the dropdown on the backend the user actually picked (the failure
                // reason is in the status line). IsConnected must be false so a send retries
                // the configured backend instead of silently using the previous provider.
                IsConnected = false;
            }

            StatusMessage = success ? "Connected" : $"Failed: {_chatService.ConnectionError}";
            if (success)
            {
                IsConnected = _chatService.IsConnected;
            }
            RefreshCodexAccountState();
            // No modal — status line is enough for optional AI backends.
        }
        catch (OperationCanceledException) when (switchCts.IsCancellationRequested)
        {
            // A newer selection superseded this switch request.
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            if (!switchCts.IsCancellationRequested)
            {
                IsConnected = false;
                StatusMessage = $"Failed: {ex.Message}";
                SynchronizeSelectedBackendIndex(_chatService.ActiveBackendId);
            }
        }
        finally
        {
            _backendSwitchGate.Release();
            if (ReferenceEquals(_backendSwitchCts, switchCts))
                _backendSwitchCts = null;
            switchCts.Dispose();
        }
    }

    private void SynchronizeSelectedBackendIndex(string? backendId)
    {
        if (string.IsNullOrWhiteSpace(backendId))
            return;

        var index = -1;
        var backends = _chatService.AvailableBackends;
        for (var i = 0; i < backends.Count; i++)
        {
            if (string.Equals(backends[i].Id, backendId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index < 0 || index >= AvailableBackends.Count)
            return;

        _synchronizingBackendSelection = true;
        try
        {
            SelectedBackendIndex = index;
        }
        finally
        {
            _synchronizingBackendSelection = false;
        }
    }

    private async Task RefreshModelsAsync()
    {
        // Clear first so a failing probe can never leave the previous backend's models visible.
        AvailableModels.Clear();
        SelectedModelIndex = -1;
        try
        {
            var models = await _chatService.GetAvailableModelsAsync();
            foreach (var model in models)
            {
                AvailableModels.Add(model);
            }

            AddConfiguredCodexModelIfMissing();
            if (AvailableModels.Count > 0)
            {
                SelectConfiguredModel();
            }

            await RefreshReasoningEffortsAsync(SelectedModel);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            StatusMessage = $"Failed to load models: {ex.Message}";
        }
    }

    private async Task RefreshReasoningEffortsAsync(string? modelId)
    {
        if (!IsCodexBackend)
        {
            AvailableReasoningEfforts.Clear();
            SelectedReasoningEffortIndex = -1;
            return;
        }

        try
        {
            var efforts = await _chatService.GetAvailableReasoningEffortsAsync(modelId).ConfigureAwait(true);
            AvailableReasoningEfforts.Clear();
            foreach (var effort in efforts)
                AvailableReasoningEfforts.Add(effort);

            if (AvailableReasoningEfforts.Count == 0)
            {
                SelectedReasoningEffortIndex = -1;
                return;
            }

            var preferredIndex = Enumerable.Range(0, AvailableReasoningEfforts.Count)
                .FirstOrDefault(i => AvailableReasoningEfforts[i].Equals(SelectedReasoningEffort, StringComparison.OrdinalIgnoreCase), -1);
            if (preferredIndex < 0)
            {
                preferredIndex = Enumerable.Range(0, AvailableReasoningEfforts.Count)
                    .FirstOrDefault(i => AvailableReasoningEfforts[i].Equals(
                        string.IsNullOrWhiteSpace(_generalApplicationData.Config.AiChatDefaultReasoningEffort)
                            ? DefaultAiChatReasoningEffort
                            : _generalApplicationData.Config.AiChatDefaultReasoningEffort,
                        StringComparison.OrdinalIgnoreCase), 0);
            }

            SelectedReasoningEffortIndex = preferredIndex;
            SelectedReasoningEffort = AvailableReasoningEfforts[preferredIndex];
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            AvailableReasoningEfforts.Clear();
            SelectedReasoningEffortIndex = -1;
        }
    }

    /// <summary>
    /// Connects to a local AI backend when the user actually needs it (send / explicit switch).
    /// Never shows a blocking dialog for a failed optional probe.
    /// </summary>
    private async Task<bool> EnsureConnectedAsync()
    {
        if (IsConnected)
        {
            return true;
        }

        await InitializeAsync().ConfigureAwait(true);
        return IsConnected;
    }

    private async Task InitializeAsync()
    {
        try
        {
            AvailableBackends.Clear();
            foreach (var (id, name) in _chatService.AvailableBackends)
            {
                AvailableBackends.Add(name);
            }
            SynchronizeSelectedBackendIndex(_generalApplicationData.Config.AiChatBackendId);

            StatusMessage = "Connecting to AI provider...";

            // Prefer the configured backend (Preferences → AI Chat → Backend); fall back to the
            // first reachable backend if the configured one is unavailable.
            var configuredBackendId = _generalApplicationData.Config.AiChatBackendId;
            var availableBackends = _chatService.AvailableBackends;
            var hasConfiguredBackend = !string.IsNullOrWhiteSpace(configuredBackendId)
                && availableBackends.Any(b => b.Id.Equals(configuredBackendId, StringComparison.OrdinalIgnoreCase));

            if (hasConfiguredBackend)
            {
                IsConnected = await _chatService.SwitchBackendAsync(configuredBackendId!);
            }
            else
            {
                IsConnected = await _chatService.InitializeAsync();
            }

            StatusMessage = IsConnected
                ? "Connected"
                : $"Not connected: {_chatService.ConnectionError}";
            RefreshCodexAccountState();
            IsCodexBackend = IsConnected
                && string.Equals(_chatService.ActiveBackendId, "codex", StringComparison.OrdinalIgnoreCase);

            // match active backend index
            if (IsConnected && _chatService.ActiveBackendId is not null)
            {
                var backends = _chatService.AvailableBackends;
                for (int i = 0; i < backends.Count; i++)
                {
                    if (backends[i].Id == _chatService.ActiveBackendId)
                    {
                        _synchronizingBackendSelection = true;
                        try
                        {
                            SelectedBackendIndex = i;
                        }
                        finally
                        {
                            _synchronizingBackendSelection = false;
                        }
                        break;
                    }
                }
            }

            // Intentionally no ShowSimpleMessageBox here — connection is optional.

            if (IsConnected)
            {
                var models = await _chatService.GetAvailableModelsAsync();
                
                System.Diagnostics.Debug.WriteLine($"[AiChat] Loaded {models.Count} models: {string.Join(", ", models)}");
                
                AvailableModels.Clear();
                foreach (var model in models)
                {
                    AvailableModels.Add(model);
                }

                AddConfiguredCodexModelIfMissing();

                var defaultModel = ResolveConfiguredModel(_generalApplicationData.Config);
                int modelIndex = -1;
                string? modelToSelect = null;
                
                for (int i = 0; i < AvailableModels.Count; i++)
                {
                    if (AvailableModels[i].Equals(defaultModel, StringComparison.OrdinalIgnoreCase))
                    {
                        modelIndex = i;
                        modelToSelect = AvailableModels[i];
                        break;
                    }
                }
                
                if (modelIndex < 0 && AvailableModels.Count > 0)
                {
                    // The configured model may belong to a different backend — never select a
                    // foreign model. Prefer the built-in codex default when it is present.
                    modelIndex = Enumerable.Range(0, AvailableModels.Count)
                        .FirstOrDefault(i => AvailableModels[i].Equals(DefaultAiChatModel, StringComparison.OrdinalIgnoreCase), -1);
                    if (modelIndex < 0)
                    {
                        modelIndex = 0;
                    }
                    modelToSelect = AvailableModels[modelIndex];
                }
                
                System.Diagnostics.Debug.WriteLine($"[AiChat] Selecting model index {modelIndex}: {modelToSelect}");
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (modelIndex >= 0)
                    {
                        SelectedModelIndex = modelIndex;
                        SelectedModel = modelToSelect!;
                    }
                }, DispatcherPriority.Loaded);

                await RefreshReasoningEffortsAsync(SelectedModel);
                
            }
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void AddConfiguredCodexModelIfMissing()
    {
        if (!IsCodexBackend)
            return;

        // The configured default can be a local/embedded model (e.g. "qwen3.5-4b") left over
        // from a previous backend switch. Only ever inject the built-in codex default so the
        // codex model list can never show another backend's models.
        var configuredModel = ResolveConfiguredModel(_generalApplicationData.Config);
        if (!string.Equals(configuredModel, DefaultAiChatModel, StringComparison.OrdinalIgnoreCase))
            return;

        if (!AvailableModels.Any(model => model.Equals(configuredModel, StringComparison.OrdinalIgnoreCase)))
        {
            AvailableModels.Insert(0, configuredModel);
        }
    }

    private void SelectConfiguredModel()
    {
        var configuredModel = ResolveConfiguredModel(_generalApplicationData.Config);
        var preferredIndex = Enumerable.Range(0, AvailableModels.Count)
            .FirstOrDefault(i => AvailableModels[i].Equals(configuredModel, StringComparison.OrdinalIgnoreCase), -1);
        if (preferredIndex < 0)
        {
            // The configured model belongs to a different backend (e.g. an embedded GGUF id
            // persisted while using Embedded) — never select a foreign model. Prefer the
            // built-in codex default when it is present in this backend's list.
            preferredIndex = Enumerable.Range(0, AvailableModels.Count)
                .FirstOrDefault(i => AvailableModels[i].Equals(DefaultAiChatModel, StringComparison.OrdinalIgnoreCase), -1);
            if (preferredIndex < 0)
                preferredIndex = 0;
        }

        SelectedModelIndex = preferredIndex;
        SelectedModel = AvailableModels[preferredIndex];
    }

    [RelayCommand]
    private async Task SignInCodex()
    {
        if (_codexLoginCts is not null)
        {
            StatusMessage = "ChatGPT sign-in is already in progress.";
            return;
        }

        var loginCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _codexLoginCts = loginCts;

        try
        {
            await RefreshCodexAccountAsync(loginCts.Token);
            if (IsCodexSignedIn)
            {
                StatusMessage = "Already signed in to ChatGPT.";
                return;
            }

            StatusMessage = "Opening ChatGPT sign-in in your browser...";
            var started = await _chatService.StartCodexLoginAsync(loginCts.Token);
            if (!started)
            {
                StatusMessage = $"Codex sign-in failed: {_chatService.ConnectionError ?? "app-server unavailable"}";
                return;
            }

            StatusMessage = "Finish sign-in in the browser. Waiting for confirmation...";
            for (var attempt = 0; attempt < 120 && !loginCts.IsCancellationRequested; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), loginCts.Token);
                await RefreshCodexAccountAsync(loginCts.Token);
                if (IsCodexSignedIn)
                {
                    StatusMessage = ShowCodexEmail && !string.IsNullOrWhiteSpace(_chatService.CodexAccount?.Email)
                        ? $"Signed in to ChatGPT as {_chatService.CodexAccount.Email}."
                        : "Signed in to ChatGPT.";
                    return;
                }
            }

            StatusMessage = "Sign-in window opened. Finish sign-in, then click Sign in again to refresh the account.";
        }
        catch (OperationCanceledException) when (loginCts.IsCancellationRequested)
        {
            StatusMessage = "ChatGPT sign-in cancelled.";
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            StatusMessage = $"Codex sign-in failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_codexLoginCts, loginCts))
                _codexLoginCts = null;
            loginCts.Dispose();
        }
    }

    [RelayCommand]
    private async Task SignOutCodex()
    {
        var loggedOut = await _chatService.LogoutCodexAsync();
        ShowCodexEmail = false;
        RefreshCodexAccountState();
        StatusMessage = loggedOut ? "Signed out from Codex." : "Could not sign out from Codex.";
        if (string.Equals(_chatService.ActiveBackendId, "codex", StringComparison.OrdinalIgnoreCase))
        {
            IsConnected = false;
            IsCodexBackend = false;
            AvailableReasoningEfforts.Clear();
            SelectedReasoningEffortIndex = -1;
        }
    }

    private void RefreshCodexAccountState()
    {
        var account = _chatService.CodexAccount;
        IsCodexSignedIn = account?.IsAuthenticated == true;
        CodexAccountLabel = account?.IsAuthenticated == true
            ? ShowCodexEmail && !string.IsNullOrWhiteSpace(account.Email)
                ? account.Email
                : $"Signed in ({account.Plan ?? "ChatGPT"})"
            : "Not signed in";
    }

    private async Task RefreshCodexAccountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _chatService.ReadCodexAccountAsync(cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(RefreshCodexAccountState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsSessionChoicePending
            || (string.IsNullOrWhiteSpace(InputText) && PendingAttachments.Count == 0)
            || IsStreaming)
            return;

        if (!await EnsureConnectedAsync())
        {
            var reason = string.IsNullOrWhiteSpace(_chatService.ConnectionError)
                ? "The selected AI provider is not connected."
                : $"The selected AI provider is not connected.\n\n{_chatService.ConnectionError}";
            _messageForUserTools.ShowSimpleMessageBoxInstance(reason, "AI provider not connected");
            return;
        }

        var normalizedPrompt = string.IsNullOrWhiteSpace(InputText)
            ? "Analyze attached references."
            : InputText.Trim();

        var messageAttachments = PendingAttachments
            .Select(x => x.Clone())
            .ToList();

        var userMessage = new ChatMessage
        {
            Content = normalizedPrompt,
            Role = "user",
            Timestamp = DateTime.Now,
            Attachments = messageAttachments
        };

        Messages.Add(userMessage);
        CurrentSession.Messages.Add(userMessage);
        CurrentSession.LastActivityAt = DateTime.Now;
        if (string.IsNullOrWhiteSpace(CurrentSession.Title) || CurrentSession.Title == "New Chat")
        {
            CurrentSession.Title = GenerateSessionTitle(normalizedPrompt);
        }
        _chatService.SetCodexThreadId(CurrentSession.CodexThreadId);
        PendingAttachments.Clear();
        HasPendingAttachments = false;

        InputText = string.Empty;

        // Add assistant message placeholder
        var assistantMessage = new ChatMessage
        {
            Content = string.Empty,
            Role = "assistant",
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);
        _activeAssistantMessage = assistantMessage;
        CurrentThinkingContent = string.Empty;

        IsStreaming = true;
        _currentStreamingCts = new CancellationTokenSource();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await foreach (var chunk in _chatService.SendMessageAsync(
                Messages.ToList(),
                SelectedModel,
                IsCodexBackend ? SelectedReasoningEffort : null,
                _currentStreamingCts.Token))
            {
                assistantMessage.Content += chunk;
            }

            CurrentSession.CodexThreadId = _chatService.GetCodexThreadId();

            assistantMessage.IsStreaming = false;
            stopwatch.Stop();
            assistantMessage.GenerationTimeMs = stopwatch.ElapsedMilliseconds;
            CurrentSession.Messages.Add(assistantMessage);
            CurrentSession.LastActivityAt = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(assistantMessage);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            assistantMessage.Content = $"Error: {ex.Message}";
            assistantMessage.IsStreaming = false;
        }
        finally
        {
            IsStreaming = false;
            _activeAssistantMessage = null;
            _currentStreamingCts?.Dispose();
            _currentStreamingCts = null;
            // Persist even when the turn failed or was cancelled so the user message is
            // not lost when the app closes (SaveChatHistory was previously success-only).
            if (CurrentSession.Messages.Count > 0)
            {
                SaveChatHistory();
            }
        }
    }

    [RelayCommand]
    private async Task CancelStreaming()
    {
        if (!IsStreaming)
            return;

        StatusMessage = "Stopping…";
        _currentStreamingCts?.Cancel();
        await _chatService.CancelCurrentRequestAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void NewChat()
    {
        if (IsStreaming)
            return;

        if (Messages.Count > 0)
        {
            SaveChatHistory();
        }

        CurrentSession = new ChatSession();
        _chatService.SetCodexThreadId(null);
        Messages.Clear();
        PendingAttachments.Clear();
        HasPendingAttachments = false;
        IsSessionChoicePending = false;
        LoadSavedSessions(preselectMostRecent: false);
        StatusMessage = "New chat — ready";
    }

    [RelayCommand]
    private void StartNewChat()
        => NewChat();

    [RelayCommand]
    private void OpenSavedSession(ChatSession? session)
    {
        if (session is null || IsStreaming)
            return;
        if (session.SessionId == CurrentSession.SessionId && Messages.Count > 0)
            return;

        if (Messages.Count > 0 && session.SessionId != CurrentSession.SessionId)
        {
            SaveChatHistory();
        }

        Messages.Clear();
        CurrentSession = session;
        foreach (var message in session.Messages)
            Messages.Add(message);

        PendingAttachments.Clear();
        HasPendingAttachments = false;
        _chatService.SetCodexThreadId(session.CodexThreadId);
        IsSessionChoicePending = false;
        LoadSavedSessions(preselectMostRecent: false);
        StatusMessage = "Conversation restored — ready";
    }

    [RelayCommand]
    private void DeleteSavedSession(ChatSession? session)
    {
        if (session is null || IsStreaming)
            return;

        var wasActive = session.SessionId == CurrentSession.SessionId;
        _generalApplicationData.Config.ChatSessions.RemoveAll(s => s.SessionId == session.SessionId);

        _synchronizingSessionSelection = true;
        try
        {
            SavedSessions.Remove(session);
            HasSavedSessions = SavedSessions.Count > 0;
            if (SelectedSavedSession?.SessionId == session.SessionId)
            {
                SelectedSavedSession = null;
            }
        }
        finally
        {
            _synchronizingSessionSelection = false;
        }

        if (wasActive)
        {
            Messages.Clear();
            CurrentSession = new ChatSession();
            _chatService.SetCodexThreadId(null);
            IsSessionChoicePending = false;
            StatusMessage = "Current conversation deleted — new chat ready";
        }

        _generalApplicationData.SaveConfig();
    }

    [RelayCommand]
    private void ClearChat()
    {
        if (IsStreaming)
            return;

        _generalApplicationData.Config.ChatSessions.RemoveAll(s => s.SessionId == CurrentSession.SessionId);
        Messages.Clear();
        CurrentSession = new ChatSession();
        _chatService.SetCodexThreadId(null);
        PendingAttachments.Clear();
        HasPendingAttachments = false;
        LoadSavedSessions(preselectMostRecent: false);
        _generalApplicationData.SaveConfig();
        StatusMessage = "Chat cleared";
    }

    [RelayCommand]
    private async Task AddFileReference()
    {
        var storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            StatusMessage = "File picker is unavailable.";
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to attach as references",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All]
        });

        foreach (var file in files)
        {
            TryAddAttachment(file.Path.LocalPath, isDirectory: false);
        }
    }

    public async Task SendToAiChatAsync()
    {
        if (!_generalApplicationData.Config.EnableAiChat)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(
                "AI Chat is disabled. Enable it in Preferences → AI Chat.",
                "AI Chat");
            return;
        }

        _messageForUserTools.DispatcherActionInstance(async () =>
        {
            if (IsStreaming) return;

            NewChat();

            for (var i = 0; i < AvailableModes.Count; i++)
            {
                if (AvailableModes[i].Mode == ChatMode.SqlFix)
                {
                    SelectedModeIndex = i;
                    break;
                }
            }

            InputText = "Fix current SQL";
            await Task.Delay(50);
            _ = SendMessageCommand.ExecuteAsync(null);
        });
    }

    [RelayCommand]
    private async Task AddFolderReference()
    {
        var storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            StatusMessage = "Folder picker is unavailable.";
            return;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder to attach as reference",
            AllowMultiple = true
        });

        foreach (var folder in folders)
        {
            TryAddAttachment(folder.Path.LocalPath, isDirectory: true);
        }
    }

    [RelayCommand]
    private void RemovePendingAttachment(ChatAttachment? attachment)
    {
        if (attachment is null)
        {
            return;
        }

        PendingAttachments.Remove(attachment);
    }

    private void TryAddAttachment(string? rawPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(rawPath);
        }
        catch
        {
            return;
        }

        bool exists = isDirectory ? System.IO.Directory.Exists(fullPath) : System.IO.File.Exists(fullPath);
        if (!exists)
        {
            StatusMessage = $"Path not found: {fullPath}";
            return;
        }

        if (PendingAttachments.Any(x => x.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        PendingAttachments.Add(new ChatAttachment
        {
            Path = fullPath,
            DisplayName = isDirectory ? new System.IO.DirectoryInfo(fullPath).Name : System.IO.Path.GetFileName(fullPath),
            IsDirectory = isDirectory
        });
    }

    [RelayCommand]
    private async Task CopyMessage(ChatMessage message)
    {
        try
        {
            await _clipboardService.SetTextAsync(message.Content);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    private void LoadChatHistory()
    {
        try
        {
            LoadSavedSessions(preselectMostRecent: true);
            IsSessionChoicePending = true;
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            IsSessionChoicePending = true;
        }
    }

    private void LoadSavedSessions(bool preselectMostRecent)
    {
        var sessions = _generalApplicationData.Config.ChatSessions?
            .OrderByDescending(s => s.LastActivityAt)
            .ToList() ?? [];

        var unchanged = sessions.Count == SavedSessions.Count;
        if (unchanged)
        {
            for (var i = 0; i < sessions.Count; i++)
            {
                if (!ReferenceEquals(sessions[i], SavedSessions[i]))
                {
                    unchanged = false;
                    break;
                }
            }
        }

        _synchronizingSessionSelection = true;
        try
        {
            if (!unchanged)
            {
                SavedSessions.Clear();
                foreach (var session in sessions)
                {
                    SavedSessions.Add(session);
                }
            }

            HasSavedSessions = SavedSessions.Count > 0;
            SelectedSavedSession = SavedSessions.FirstOrDefault(s => s.SessionId == CurrentSession.SessionId)
                ?? (preselectMostRecent ? SavedSessions.FirstOrDefault() : null);
        }
        finally
        {
            _synchronizingSessionSelection = false;
        }
    }

    private static string GenerateSessionTitle(string? firstUserMessage)
    {
        if (string.IsNullOrWhiteSpace(firstUserMessage))
            return "New Chat";

        var text = firstUserMessage.Replace('\r', ' ').Replace('\n', ' ');
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[`*_#>|\[\]()]|^[-=]{2,}", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0)
            return "New Chat";

        var sentenceEnd = text.IndexOfAny(['.', '?', '!']);
        if (sentenceEnd > 0)
            text = text[..sentenceEnd].Trim();
        if (text.Length == 0)
            return "New Chat";

        const int maxTitleLength = 50;
        return text.Length <= maxTitleLength ? text : text[..maxTitleLength].TrimEnd() + "…";
    }

    private void SaveChatHistory()
    {
        try
        {
            if (_generalApplicationData.Config.ChatSessions == null)
            {
                _generalApplicationData.Config.ChatSessions = [];
            }

            // Remove old session if exists
            _generalApplicationData.Config.ChatSessions.RemoveAll(s => s.SessionId == CurrentSession.SessionId);

            // Add current session
            if (Messages.Count > 0)
            {
                CurrentSession.Messages = Messages.ToList();
                _generalApplicationData.Config.ChatSessions.Add(CurrentSession);

                // Keep only the configured number of sessions
                var historyLimit = Math.Clamp(_generalApplicationData.Config.AiChatHistoryLimit, 1, 100);
                if (_generalApplicationData.Config.ChatSessions.Count > historyLimit)
                {
                    _generalApplicationData.Config.ChatSessions = _generalApplicationData.Config.ChatSessions
                        .OrderByDescending(s => s.LastActivityAt)
                        .Take(historyLimit)
                        .ToList();
                }

                _generalApplicationData.SaveConfig();
            }

            LoadSavedSessions(preselectMostRecent: false);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    [RelayCommand]
    private void SwitchMode(string modeSlug)
    {
        var mode = ChatModeExtensions.FromSlug(modeSlug);
        CurrentMode = mode;
        CurrentModeDisplayName = mode.ToDisplayName();
        StatusMessage = $"Switched to {CurrentModeDisplayName} mode";
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        if (value >= 0 && value < AvailableModes.Count)
        {
            var mode = AvailableModes[value].Mode;
            if (mode != CurrentMode)
            {
                CurrentMode = mode;
                CurrentModeDisplayName = mode.ToDisplayName();
                StatusMessage = $"Switched to {CurrentModeDisplayName} mode";
            }
        }
    }

    [RelayCommand]
    private void ToggleTodoPanel()
    {
        ShowTodoPanel = !ShowTodoPanel;
    }

    [RelayCommand]
    private void ToggleSlashCommandMenu()
    {
        ShowSlashCommandMenu = !ShowSlashCommandMenu;
    }

    partial void OnInputTextChanged(string value)
    {
        if (SlashCommand.IsSlashCommand(value))
        {
            var filter = value.TrimStart('/');
            SlashCommandFilter = filter;
            ShowSlashCommandMenu = true;
            ShowMentionMenu = false;
            UpdateFilteredSlashCommands(filter);
        }
        else if (ContainsMentionTrigger(value))
        {
            var mentionFilter = ExtractMentionFilter(value);
            ShowSlashCommandMenu = false;
            ShowMentionMenu = true;
            _ = SearchMentionsAsync(mentionFilter);
        }
        else
        {
            ShowSlashCommandMenu = false;
            ShowMentionMenu = false;
        }
    }

    private static bool ContainsMentionTrigger(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var atIndex = text.LastIndexOf('@');
        if (atIndex < 0)
            return false;

        var afterAt = text[(atIndex + 1)..];
        if (afterAt.Contains(' ', StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string ExtractMentionFilter(string text)
    {
        var atIndex = text.LastIndexOf('@');
        if (atIndex < 0)
            return string.Empty;

        return text[(atIndex + 1)..];
    }

    private async Task SearchMentionsAsync(string filter)
    {
        try
        {
            var suggestions = new List<MentionItem>();

            suggestions.Add(new MentionItem { Name = "current-sql", Type = MentionType.SqlEditor, Description = "Current SQL editor content" });
            suggestions.Add(new MentionItem { Name = "results", Type = MentionType.Results, Description = "Current query results" });

            var context = GetActiveSqlContext();
            if (context.HasValue)
            {
                var (connectionName, databaseName) = context.Value;

                var connectionItem = new MentionItem
                {
                    Name = connectionName,
                    Type = MentionType.Connection,
                    Description = $"Active connection"
                };
                suggestions.Add(connectionItem);

                if (!string.IsNullOrWhiteSpace(databaseName))
                {
                    var dbItem = new MentionItem
                    {
                        Name = databaseName,
                        Type = MentionType.Database,
                        Description = $"Database in {connectionName}"
                    };
                    suggestions.Add(dbItem);
                }

                await Task.Run(() =>
                {
                    var dbService = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
                    if (dbService is null)
                    {
                        return;
                    }

                    var db = string.IsNullOrWhiteSpace(databaseName) ? dbService.Database : databaseName;

                    try
                    {
                        var schemas = dbService.GetSchemas(db, "").Take(20);
                                foreach (var schema in schemas)
                                {
                                    if (!string.IsNullOrWhiteSpace(filter) &&
                                        !schema.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    suggestions.Add(new MentionItem
                                    {
                                        Name = schema,
                                        Type = MentionType.Schema,
                                        Database = db,
                                        Description = $"Schema in {db}"
                                    });
                                }

                                foreach (var schema in dbService.GetSchemas(db, "").Take(5))
                                {
                                    var tables = dbService.GetDbObjects(db, schema, filter, TypeInDatabaseEnum.Table).Take(10);
                                    foreach (var table in tables)
                                    {
                                        suggestions.Add(new MentionItem
                                        {
                                            Name = table.Name,
                                            FullName = $"{db}.{schema}.{table.Name}",
                                            Type = MentionType.Table,
                                            Schema = schema,
                                            Database = db,
                                            Description = table.Desc
                                        });
                                    }

                                    var views = dbService.GetDbObjects(db, schema, filter, TypeInDatabaseEnum.View).Take(5);
                                    foreach (var view in views)
                                    {
                                        suggestions.Add(new MentionItem
                                        {
                                            Name = view.Name,
                                            FullName = $"{db}.{schema}.{view.Name}",
                                            Type = MentionType.View,
                                            Schema = schema,
                                            Database = db,
                                            Description = view.Desc
                                        });
                                    }

                                    var procs = dbService.GetDbObjects(db, schema, filter, TypeInDatabaseEnum.Procedure).Take(5);
                                    foreach (var proc in procs)
                                    {
                                        suggestions.Add(new MentionItem
                                        {
                                            Name = proc.Name,
                                            FullName = $"{db}.{schema}.{proc.Name}",
                                            Type = MentionType.Procedure,
                                            Schema = schema,
                                            Database = db,
                                            Description = proc.Desc
                                        });
                                    }
                                }
                    }
                    catch (Exception ex)
                    {
                        _logger.TrackError(ex, isCrash: false);
                    }
                });
            }

            var filtered = string.IsNullOrWhiteSpace(filter)
                ? suggestions
                : suggestions.Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                        (m.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MentionSuggestions.Clear();
                foreach (var item in filtered.Take(20))
                {
                    MentionSuggestions.Add(item);
                }
                ShowMentionMenu = MentionSuggestions.Count > 0;
            });
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    [RelayCommand]
    public void InsertMentionItem(MentionItem item)
    {
        var atIndex = InputText.LastIndexOf('@');
        if (atIndex >= 0)
        {
            var beforeAt = InputText[..atIndex];
            var afterMention = string.Empty;
            
            var spaceAfter = InputText.IndexOf(' ', atIndex);
            if (spaceAfter > atIndex)
            {
                afterMention = InputText[spaceAfter..];
            }

            InputText = $"{beforeAt}@{item.InsertText} {afterMention}".TrimEnd();
        }

        ShowMentionMenu = false;
    }

    private void UpdateFilteredSlashCommands(string filter)
    {
        AvailableSlashCommands.Clear();
        var commands = string.IsNullOrWhiteSpace(filter)
            ? SlashCommand.BuiltInCommands
            : SlashCommand.GetMatchingCommands("/" + filter);
        
        foreach (var cmd in commands)
        {
            AvailableSlashCommands.Add(cmd);
        }
    }

    [RelayCommand]
    public void ExecuteSlashCommand(SlashCommand command)
    {
        if (command.TargetMode.HasValue)
        {
            SwitchMode(command.TargetMode.Value.ToSlug());
        }

        if (command.Action == "clear")
        {
            NewChat();
        }

        if (!string.IsNullOrWhiteSpace(command.AutoContext))
        {
            InputText = command.AutoContext switch
            {
                "schema" => "Search schema objects: ",
                "history" => "Search SQL history for: ",
                "analyze" => "Analyze current SQL for Netezza optimization",
                "explain" => "Explain current SQL query",
                _ => string.Empty
            };
        }

        ShowSlashCommandMenu = false;
    }

    public void InsertMention(string mention)
    {
        var cursorPos = InputText.Length;
        var beforeCursor = InputText;
        var afterCursor = string.Empty;
        
        var lastAtIndex = beforeCursor.LastIndexOf('@');
        if (lastAtIndex >= 0 && cursorPos >= lastAtIndex)
        {
            beforeCursor = InputText[..lastAtIndex];
        }

        InputText = $"{beforeCursor}@{mention} {afterCursor}".TrimStart();
    }

    public void UpdateTodoListFromJson(string todosJson)
    {
        CurrentTodoList.UpdateFromJson(todosJson);
        HasTodoItems = CurrentTodoList.TotalCount > 0;
    }

    partial void OnCurrentModeChanged(ChatMode value)
    {
        CurrentModeDisplayName = value.ToDisplayName();
        _chatService.SetMode(value);
        for (var i = 0; i < AvailableModes.Count; i++)
        {
            if (AvailableModes[i].Mode == value)
            {
                SelectedModeIndex = i;
                break;
            }
        }
    }

    partial void OnShowCodexEmailChanged(bool value)
    {
        RefreshCodexAccountState();
    }

    [RelayCommand]
    private void ToggleShowCodexEmail()
    {
        ShowCodexEmail = !ShowCodexEmail;
    }
}

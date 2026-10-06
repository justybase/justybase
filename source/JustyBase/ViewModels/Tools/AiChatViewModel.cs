using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Ai.Models;
using JustyBase.Common.Models;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Ai.Services;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools.Converters;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;

public sealed partial class AiChatViewModel : Tool, IDisposable
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
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
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
    public partial bool IsInitializing { get; set; }

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

    public bool CanCompose => !IsStreaming && !IsSessionChoicePending && !IsInitializing;

    public bool CanSwitchSession => !IsStreaming && !IsSessionChoicePending && !IsInitializing;

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
    public partial bool IsEmbeddedBackend { get; set; }

    /// <summary>Reasoning effort is available for Codex and the embedded llama-server (Qwen3-style thinking).</summary>
    public bool ShowReasoningEffort => IsCodexBackend || IsEmbeddedBackend;

    partial void OnIsCodexBackendChanged(bool value) => OnPropertyChanged(nameof(ShowReasoningEffort));

    partial void OnIsEmbeddedBackendChanged(bool value) => OnPropertyChanged(nameof(ShowReasoningEffort));

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
        _chatService.ReasoningChunkReceived += reasoningChunk =>
        {
            // Stream the thinking text live while the local model generates.
            Dispatcher.UIThread.Post(() => CurrentThinkingContent += reasoningChunk);
        };

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
        IsEmbeddedBackend = string.Equals(config.AiChatBackendId, "embedded", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(SelectedModel))
        {
            AvailableModels.Add(SelectedModel);
            SelectedModelIndex = 0;
        }
        if (ShowReasoningEffort && !string.IsNullOrWhiteSpace(SelectedReasoningEffort))
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

        // The embedded model list is a pure disk probe — populate the dropdown right
        // away so downloaded models are visible even before the backend connects.
        _ = RefreshModelsAsync();
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsSessionChoicePending
            || (string.IsNullOrWhiteSpace(InputText) && PendingAttachments.Count == 0)
            || IsStreaming
            || IsInitializing)
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
                ShowReasoningEffort ? SelectedReasoningEffort : null,
                _currentStreamingCts.Token))
            {
                assistantMessage.Content += chunk;
            }

            CurrentSession.CodexThreadId = _chatService.GetCodexThreadId();

            assistantMessage.IsStreaming = false;
            stopwatch.Stop();
            assistantMessage.GenerationTimeMs = stopwatch.ElapsedMilliseconds;
            var reasoning = _chatService.LastReasoningContent;
            if (!string.IsNullOrWhiteSpace(reasoning) && !string.IsNullOrWhiteSpace(assistantMessage.Content))
            {
                // The visible answer is the thinking text only when the model answered
                // entirely inside its reasoning phase (the client flushes it into Content).
                assistantMessage.ThinkingContent = reasoning;
            }
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

    public void Dispose()
    {
        _currentStreamingCts?.Cancel();
        _currentStreamingCts?.Dispose();
        _currentStreamingCts = null;

        _codexLoginCts?.Cancel();
        _codexLoginCts?.Dispose();
        _codexLoginCts = null;

        _backendSwitchCts?.Cancel();
        _backendSwitchCts?.Dispose();
        _backendSwitchCts = null;

        _backendSwitchGate.Dispose();
        _initializeGate.Dispose();
        GC.SuppressFinalize(this);
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

        _generalApplicationData.SaveAppConfig();
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
        _generalApplicationData.SaveAppConfig();
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

                _generalApplicationData.SaveAppConfig();
            }

            LoadSavedSessions(preselectMostRecent: false);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }
}

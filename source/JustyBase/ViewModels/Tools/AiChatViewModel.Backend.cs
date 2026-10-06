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

public sealed partial class AiChatViewModel
{
    partial void OnSelectedModelChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _generalApplicationData.Config.AiChatDefaultModel = value;
            PersistAiChatSelection();
        }

        if (ShowReasoningEffort && !IsStreaming)
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
        OnPropertyChanged(nameof(HasLiveThinking));
    }

    partial void OnCurrentThinkingContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasLiveThinking));
    }

    /// <summary>True while the local model streams its thinking and there is text to show.</summary>
    public bool HasLiveThinking => IsStreaming && !string.IsNullOrWhiteSpace(CurrentThinkingContent);

    partial void OnIsInitializingChanged(bool value)
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
            _generalApplicationData.SaveAppConfig();
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    private static string ResolveConfiguredModel(JustyBase.Common.AppOptions config)
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
            IsEmbeddedBackend = success && string.Equals(backendId, "embedded", StringComparison.OrdinalIgnoreCase);
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
        if (!ShowReasoningEffort)
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

        // Serialize lazy initialization so repeated sends during a slow first launch
        // (e.g. starting the embedded llama-server) do not spawn parallel connects.
        await _initializeGate.WaitAsync();
        try
        {
            if (IsConnected)
            {
                return true;
            }

            await InitializeAsync().ConfigureAwait(true);
            return IsConnected;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task InitializeAsync()
    {
        IsInitializing = true;
        StatusMessage = "Preparing the AI provider — first launch of the embedded model may take a few seconds…";
        try
        {
            AvailableBackends.Clear();
            foreach (var (id, name) in _chatService.AvailableBackends)
            {
                AvailableBackends.Add(name);
            }
            SynchronizeSelectedBackendIndex(_generalApplicationData.Config.AiChatBackendId);

            StatusMessage = "Preparing the AI provider — first launch of the embedded model may take a few seconds…";

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
            IsEmbeddedBackend = IsConnected
                && string.Equals(_chatService.ActiveBackendId, "embedded", StringComparison.OrdinalIgnoreCase);

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
        finally
        {
            IsInitializing = false;
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
            // A newer sign-in attempt or disposal cancelled this refresh; nothing to report.
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }
}

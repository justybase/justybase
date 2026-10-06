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
    // === Embedded chat (llama-server) settings ====
    // ============================================================

    /// <summary>Master On/Off switch for the Embedded (local) AI chat backend. Default off.</summary>
    public bool EnableEmbeddedChatAi
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.EnableEmbeddedChatAi = value;

            if (value && !_suppressFimSideEffects)
            {
                if (!_embeddedChatStore.IsModelPresent)
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance(
                        "Embedded Chat is enabled, but the selected chat model is not downloaded yet. " +
                        "Download the model below in the Embedded AI (Chat) settings before switching to it in AI Chat.",
                        "Chat model required");
                }
            }
        }
    }

    public IReadOnlyList<FimModelChoiceItem> EmbeddedChatModelChoices { get; }

    public FimModelChoiceItem? SelectedEmbeddedChatModel
    {
        get;
        set
        {
            if (value is null)
            {
                return;
            }

            if (!_suppressFimSideEffects
                && NeedsEmbeddedChatLicenseAcceptance(value))
            {
                _ = SelectEmbeddedChatModelWithLicenseAsync(value);
                return;
            }

            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedChatModelId = value.Id;
            OnPropertyChanged(nameof(SelectedEmbeddedChatModelNotes));
            if (!_suppressFimSideEffects)
            {
                RefreshEmbeddedChatDiskStatus();
            }
        }
    }

    public string SelectedEmbeddedChatModelNotes =>
        SelectedEmbeddedChatModel?.Notes ?? "Select a model to see details.";

    private bool NeedsEmbeddedChatLicenseAcceptance(FimModelChoiceItem model)
    {
        if (!model.RequiresLicenseAcceptance)
        {
            return false;
        }

        var accepted = _generalApplicationData.Config.EmbeddedChatAcceptedLicenseModelIds;
        return accepted is null
            || !accepted.Any(id => string.Equals(id, model.Id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SelectEmbeddedChatModelWithLicenseAsync(FimModelChoiceItem value)
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
            OnPropertyChanged(nameof(SelectedEmbeddedChatModel));
            return;
        }

        var list = _generalApplicationData.Config.EmbeddedChatAcceptedLicenseModelIds
            ??= [];
        if (!list.Any(id => string.Equals(id, value.Id, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(value.Id);
        }

        SelectedEmbeddedChatModel = value;
    }

    public string ChatModelsDirectory => _embeddedChatStore.ModelsDirectory;

    public string ChatModelDiskStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Model status unknown.";

    public bool ChatModelPresentOnDisk
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string ChatPrepareStatusMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Idle — use Download / prepare when needed.";

    public double ChatPrepareProgressValue
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string ChatPrepareProgressPercentText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    public bool ChatPrepareIsIndeterminate
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool ChatPrepareInProgress
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanDeleteEmbeddedChatModel));
                PrepareEmbeddedChatModelCommand.NotifyCanExecuteChanged();
                CancelEmbeddedChatPrepareCommand.NotifyCanExecuteChanged();
                DeleteEmbeddedChatModelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanDeleteEmbeddedChatModel => ChatModelPresentOnDisk && !ChatPrepareInProgress;

    public int EmbeddedChatGpuLayers
    {
        get;
        set
        {
            var clamped = Math.Clamp(value < 0 ? 99 : value, 0, 999);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedChatGpuLayers = clamped;
            OnPropertyChanged(nameof(EmbeddedChatGpuLayersLabel));
        }
    }

    public bool EmbeddedChatGpuLayersEnabled => EmbeddedFimPreferVulkan;

    public string EmbeddedChatGpuLayersLabel =>
        EmbeddedChatGpuLayers <= 0
            ? "0 (CPU compute)"
            : EmbeddedChatGpuLayers >= 99
                ? ChatAutoGpuLayersLabel
                : $"{EmbeddedChatGpuLayers} layers";

    private string ChatAutoGpuLayersLabel
    {
        get
        {
            var layers = GgufBlockCountReader.Read(_embeddedChatStore.LocalModelPath);
            return layers is > 0
                ? $"Auto — all {layers} layers"
                : "Auto (as many layers as fit in VRAM)";
        }
    }

    public int EmbeddedChatCtxSize
    {
        get;
        set
        {
            var clamped = Math.Clamp(value <= 0 ? 4096 : value, 512, 131_072);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.EmbeddedChatCtxSize = clamped;
        }
    }

    private void RefreshEmbeddedChatDiskStatus()
    {
        ChatModelPresentOnDisk = _embeddedChatStore.IsModelPresent;
        var model = _embeddedChatStore.CurrentModel;
        ChatModelDiskStatus = _embeddedChatStore.IsModelPresent
            ? $"{model.DisplayName}: on disk ({_embeddedChatStore.LocalModelSizeBytes / (1024d * 1024d):0.#} MB)."
            : $"{model.DisplayName}: not downloaded.";
        OnPropertyChanged(nameof(CanDeleteEmbeddedChatModel));
        DeleteEmbeddedChatModelCommand.NotifyCanExecuteChanged();
    }

    private void ReportChatPrepareProgress(double fraction, string message, bool isIndeterminate = false, bool force = false)
    {
        ChatPrepareStatusMessage = message;
        ChatPrepareIsIndeterminate = isIndeterminate;
        ChatPrepareProgressValue = Math.Clamp(fraction, 0, 1);
        ChatPrepareProgressPercentText = isIndeterminate
            ? "…"
            : $"{ChatPrepareProgressValue * 100:0.#}%";
    }

    [RelayCommand]
    private void ShowEmbeddedChatModelInFolder()
    {
        try
        {
            var dir = _embeddedChatStore.EnsureModelsDirectory();
            var path = _embeddedChatStore.LocalModelPath;
            if (File.Exists(path))
            {
                _messageForUserTools.ShowOrShowInExplorerHelper(path);
                return;
            }

            _messageForUserTools.OpenInExplorerHelper(dir);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ChatPrepareStatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenSelectedEmbeddedChatModelPage()
    {
        var url = SelectedEmbeddedChatModel?.SourceUrl;
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
            ChatPrepareStatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPrepareEmbeddedChatModel))]
    private Task PrepareEmbeddedChatModel() => BootstrapChatModelAsync();

    private bool CanPrepareEmbeddedChatModel() => !ChatPrepareInProgress;

    [RelayCommand(CanExecute = nameof(ChatPrepareInProgress))]
    private void CancelEmbeddedChatPrepare()
    {
        try
        {
            _chatPrepareCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        ChatPrepareStatusMessage = "Cancelling…";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteEmbeddedChatModel))]
    private async Task DeleteEmbeddedChatModel()
    {
        var model = SelectedEmbeddedChatModel?.DisplayName ?? "selected model";
        var confirm = await _messageForUserTools.ShowConfirmationDialogAsync(
            $"Delete local GGUF for {model} from disk?\n\n{_embeddedChatStore.LocalModelPath}",
            "Delete chat model").ConfigureAwait(true);
        if (!confirm)
        {
            return;
        }

        try
        {
            // The llama-server keeps the GGUF open — stop it first or File.Delete will fail.
            if (_llamaServerManager is not null)
            {
                await _llamaServerManager.StopServerAsync(LlamaServerRole.Chat).ConfigureAwait(true);
            }

            if (!_embeddedChatStore.TryDeleteCurrentModel())
            {
                ChatPrepareStatusMessage = "Model file could not be deleted (it may be in use). Close the AI Chat panel and try again.";
                RefreshEmbeddedChatDiskStatus();
                return;
            }

            ChatPrepareStatusMessage = "Model deleted from disk.";
            ChatPrepareProgressValue = 0;
            ChatPrepareProgressPercentText = "";
            ChatPrepareIsIndeterminate = false;
            RefreshEmbeddedChatDiskStatus();
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ChatPrepareStatusMessage = ex.Message;
            RefreshEmbeddedChatDiskStatus();
        }
    }

    private async Task BootstrapChatModelAsync()
    {
        if (_chatPrepareInFlight)
        {
            return;
        }

        _chatPrepareInFlight = true;
        ChatPrepareInProgress = true;
        _chatPrepareCts?.Dispose();
        _chatPrepareCts = new CancellationTokenSource();
        var ct = _chatPrepareCts.Token;
        ReportChatPrepareProgress(0, "Starting download…");

        try
        {
            var progress = new Progress<FimModelProgress>(p =>
                ReportChatPrepareProgress(p.Fraction, p.Message, p.IsIndeterminate));

            try
            {
                await _embeddedChatStore.EnsureModelAsync(progress, ct).ConfigureAwait(true);
                if (!ct.IsCancellationRequested)
                {
                    ReportChatPrepareProgress(1.0, "Model ready.");
                }
                else
                {
                    ReportChatPrepareProgress(0, "Download cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                ReportChatPrepareProgress(0, "Download cancelled.", force: true);
            }
            catch (Exception ex)
            {
                ReportChatPrepareProgress(0, ex.Message, force: true);
            }

            RefreshEmbeddedChatDiskStatus();
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReportChatPrepareProgress(0, ex.Message, force: true);
        }
        finally
        {
            ChatPrepareInProgress = false;
            _chatPrepareInFlight = false;
            _chatPrepareCts?.Dispose();
            _chatPrepareCts = null;
        }
    }
}

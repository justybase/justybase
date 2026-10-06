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
            _generalApplicationData.Config.FimDebounceMs = snapped;
        }
    }

    private static int ResolveEmbeddedFimDebounceMs(AppOptions config)
    {
        if (config.FimDebounceMs > 0)
        {
            return InlineCompletionController.SnapDebounceMs(config.FimDebounceMs);
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

            _generalApplicationData.Config.EnableFimServer = value;

            if (value && !_suppressFimSideEffects)
            {
                if (!_fimBootstrap.IsSelectedModelPresent)
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance(
                        "Embedded FIM is enabled, but the selected model is not downloaded yet. " +
                        "Download the model below in the Embedded AI (FIM) settings. " +
                        "After the download, the current SQL tab will start using FIM without restarting the application.",
                        "FIM model required");
                }
            }
            else if (!value && !_suppressFimSideEffects)
            {
                // Release the model + GPU memory when FIM is switched off.
                _ = StopFimServerOnDisableAsync();
            }
        }
    }

    private async Task StopFimServerOnDisableAsync()
    {
        try
        {
            await _fimBootstrap.StopServerAsync().ConfigureAwait(true);
            RefreshEmbeddedFimDiskStatus();
            FimPrepareStatusMessage = "FIM server stopped.";
        }
        catch (Exception ex)
        {
            FimPrepareStatusMessage = ex.Message;
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

            _generalApplicationData.Config.FimMaxTokens = clamped;
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

            _generalApplicationData.Config.FimMaxPromptTokens = clamped;
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

            _generalApplicationData.Config.FimPrefixPercentage = clamped;
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

            _generalApplicationData.Config.FimSuffixPercentage = clamped;
            MarkPresetCustom();
        }
    }

    public bool EmbeddedFimSchemaContext
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.FimSchemaContext = value;
        }
    }

    public int EmbeddedFimSchemaContextMaxTokens
    {
        get;
        set
        {
            var clamped = ClampEmbeddedFimSchemaContextMaxTokens(value);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.FimSchemaContextMaxTokens = clamped;
            OnPropertyChanged(nameof(EmbeddedFimSchemaContextMaxTokensLabel));
        }
    }

    public string EmbeddedFimSchemaContextMaxTokensLabel => $"{EmbeddedFimSchemaContextMaxTokens} tokens";

    public int EmbeddedFimCtxSize
    {
        get;
        set
        {
            var clamped = Math.Clamp(value < 512 ? 4096 : value, 512, 131_072);
            if (!SetProperty(ref field, clamped))
            {
                return;
            }

            _generalApplicationData.Config.FimCtxSize = clamped;
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

            _generalApplicationData.Config.FimPreset = value.Id;
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

    public bool EmbeddedFimPreferVulkan
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            _generalApplicationData.Config.LlamaServerPreferVulkan = value;
            OnPropertyChanged(nameof(EmbeddedFimGpuLayersEnabled));
            if (!_suppressFimSideEffects)
            {
                FimPrepareStatusMessage = value
                    ? "Vulkan preferred — reload the model (Prepare) for the new binary variant to take effect."
                    : "CPU (avx2) binary preferred — reload the model (Prepare) for the new binary variant to take effect.";
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

            _generalApplicationData.Config.FimGpuLayers = clamped;
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
                ? FimAutoGpuLayersLabel
                : $"{EmbeddedFimGpuLayers} layers";

    private string FimAutoGpuLayersLabel
    {
        get
        {
            var layers = GgufBlockCountReader.Read(_fimBootstrap.SelectedModelLocalPath);
            return layers is > 0
                ? $"Auto — all {layers} layers"
                : "Auto (as many layers as fit in VRAM)";
        }
    }

    private async Task ReloadFimModelAfterGpuChangeAsync()
    {
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

    private static int ClampEmbeddedFimSchemaContextMaxTokens(int value) =>
        Math.Clamp(value <= 0 ? 256 : value, 64, 1024);

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
        var preset = cfg.FimPreset;
        var known = string.Equals(preset, "Small", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "Medium", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "Large", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(preset) || !known)
        {
            cfg.FimPreset = FimPresets.Normalize(preset);
        }
    }

    private void ApplyPreset(string presetId)
    {
        if (string.Equals(presetId, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            SelectedEmbeddedFimPreset = EmbeddedFimPresetChoices.First(c => c.Id == "Custom");
            return;
        }

        var def = FimPresets.Get(presetId);

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
            var model = FimModelChoices.FirstOrDefault(m =>
                string.Equals(m.Id, def.ModelId, StringComparison.OrdinalIgnoreCase));
            if (model is not null)
            {
                SelectedEmbeddedFimModel = model;
            }

            _generalApplicationData.Config.FimPreset = def.Id;
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

        if (string.Equals(_generalApplicationData.Config.FimPreset, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _generalApplicationData.Config.FimPreset = "Custom";
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
        ApplyPreset("Medium");
    }

    public IReadOnlyList<FimModelChoiceItem> FimModelChoices { get; }

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

            _generalApplicationData.Config.FimModelId = value.Id;
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

        var accepted = _generalApplicationData.Config.FimAcceptedLicenseModelIds;
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

        var list = _generalApplicationData.Config.FimAcceptedLicenseModelIds
            ??= [];
        if (!list.Any(id => string.Equals(id, value.Id, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(value.Id);
        }

        SelectedEmbeddedFimModel = value;
    }
    public string FimModelsDirectory => _fimBootstrap.ModelsDirectory;

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
        FimModelPresentOnDisk = _fimBootstrap.IsSelectedModelPresent;
        FimModelDiskStatus = _fimBootstrap.SelectedModelDiskStatus;
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
            var dir = _fimBootstrap.EnsureModelsDirectory();
            var path = _fimBootstrap.SelectedModelLocalPath;
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
            var progress = new Progress<FimModelProgress>(p =>
                ReportFimPrepareProgress(p.Fraction, p.Message, p.IsIndeterminate, force: p.IsIndeterminate || p.Fraction >= 1.0));

            try
            {
                var report = await _fimBootstrap.RunSpeedTestAsync(
                    EmbeddedFimMaxTokens,
                    EmbeddedFimMaxPromptTokens,
                    EmbeddedFimPrefixPercentage,
                    EmbeddedFimSuffixPercentage,
                    progress,
                    ct).ConfigureAwait(true);

                FimBenchmarkResult = FormatSpeedTestReport(report);
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

    private static string FormatSpeedTestReport(FimSpeedTestReport report)
    {
        if (!report.Succeeded)
        {
            return $"{report.ModelName}: generation returned no output ({report.ElapsedMs} ms).";
        }

        return $"{report.ModelName}: {report.TokensPerSecond:0.0} tokens/s (approx), {report.ElapsedMs} ms elapsed.";
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
}

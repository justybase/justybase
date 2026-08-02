using AvaloniaEdit;
using JustyBase.Ai.Fim.Abstractions;
using JustyBase.Ai.Fim.Benchmark;
using JustyBase.Ai.Fim.Download;
using JustyBase.Ai.Fim.LlamaSharp;
using JustyBase.Ai.Fim.Prompting;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.Editor.InlineCompletion;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace JustyBase.Services.Fim;

public static class FimServiceCollectionExtensions
{
    /// <summary>
    /// Registers embedded LLamaSharp FIM provider + bridge. Call only when EnableEmbeddedFim=true.
    /// </summary>
    public static IServiceCollection AddEmbeddedFimCompletion(this IServiceCollection collection)
    {
        collection.AddSingleton<IFimModelCatalog, FimModelCatalog>();
        collection.AddSingleton<IFimModelStore>(sp =>
        {
            var catalog = sp.GetRequiredService<IFimModelCatalog>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new HuggingFaceFimModelStore(catalog, () => appData.Config.EmbeddedFimModelId);
        });
        collection.AddSingleton<IFimPromptBuilder>(sp =>
        {
            var catalog = sp.GetRequiredService<IFimModelCatalog>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new CatalogFimPromptBuilder(catalog, () => appData.Config.EmbeddedFimModelId);
        });
        collection.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<IFimModelStore>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            LlamaSharpModelHost.ConfigureNativeBackend(appData.Config.EmbeddedFimPreferVulkan);
            return new LlamaSharpModelHost(
                store,
                getGpuLayerCount: () => ResolveGpuLayers(appData.Config));
        });
        collection.AddSingleton<LlamaSharpCompletionProvider>(sp =>
        {
            var host = sp.GetRequiredService<LlamaSharpModelHost>();
            var builder = sp.GetRequiredService<IFimPromptBuilder>();
            return new LlamaSharpCompletionProvider(host, builder);
        });
        collection.AddSingleton<ICompletionProvider>(sp => sp.GetRequiredService<LlamaSharpCompletionProvider>());
        collection.AddSingleton(sp =>
        {
            var provider = sp.GetRequiredService<ICompletionProvider>();
            var appData = sp.GetRequiredService<IGeneralApplicationData>();
            return new FimInlineCompletionBridge(
                provider,
                () => appData.Config.EnableEmbeddedFimAi,
                () => new FimPromptBudget(
                    appData.Config.EmbeddedFimMaxPromptTokens,
                    appData.Config.EmbeddedFimPrefixPercentage,
                    appData.Config.EmbeddedFimSuffixPercentage,
                    appData.Config.EmbeddedFimMaxTokens));
        });
        collection.AddSingleton<IFimModelBootstrapService, FimModelBootstrapService>();
        return collection;
    }

    private static int ResolveGpuLayers(JustyBase.Common.AppOptions config)
    {
        if (!config.EmbeddedFimPreferVulkan)
        {
            return 0;
        }

        var layers = config.EmbeddedFimGpuLayers;
        if (layers < 0)
        {
            return 99;
        }

        return Math.Clamp(layers, 0, 999);
    }
}

/// <summary>Starts on-demand GGUF download / model load with progress callbacks.</summary>
public interface IFimModelBootstrapService
{
    string ModelsDirectory { get; }
    bool IsSelectedModelPresent { get; }
    string SelectedModelLocalPath { get; }
    string SelectedModelDiskStatus { get; }
    string EnsureModelsDirectory();
    Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default);
    Task DeleteSelectedModelAsync(CancellationToken cancellationToken = default);
    Task ReloadModelAsync(CancellationToken cancellationToken = default);
    Task<FimBenchmarkComparisonReport> RunSpeedBenchmarkAsync(
        int maxTokens,
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage,
        int debounceMs,
        int configuredGpuLayers,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class FimModelBootstrapService : IFimModelBootstrapService
{
    private readonly LlamaSharpCompletionProvider _provider;
    private readonly IFimModelStore _store;
    private readonly LlamaSharpModelHost _host;
    private readonly FimInlineCompletionBridge _bridge;
    private int _busy;

    public FimModelBootstrapService(
        LlamaSharpCompletionProvider provider,
        IFimModelStore store,
        LlamaSharpModelHost host,
        FimInlineCompletionBridge bridge)
    {
        _provider = provider;
        _store = store;
        _host = host;
        _bridge = bridge;
    }

    public string ModelsDirectory => _store.ModelsDirectory;
    public bool IsSelectedModelPresent => _store.IsModelPresent;
    public string SelectedModelLocalPath => _store.LocalModelPath;
    public string EnsureModelsDirectory() => _store.EnsureModelsDirectory();

    public string SelectedModelDiskStatus
    {
        get
        {
            var model = _store.CurrentModel;
            if (!_store.IsModelPresent)
            {
                return $"{model.DisplayName}: not downloaded.";
            }

            try
            {
                var mb = new FileInfo(_store.LocalModelPath).Length / (1024d * 1024d);
                var layers = _host.IsLoaded ? _host.LoadedGpuLayerCount : _host.EffectiveGpuLayerCount;
                return $"{model.DisplayName}: on disk ({mb:0.#} MB), gpu_layers={layers}.";
            }
#pragma warning disable CA1031
            catch
#pragma warning restore CA1031
            {
                return $"{model.DisplayName}: on disk.";
            }
        }
    }

    public async Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await EnsureReadyCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task DeleteSelectedModelAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await _host.UnloadAsync(cancellationToken).ConfigureAwait(false);
            if (!_store.TryDeleteCurrentModel())
            {
                throw new InvalidOperationException("No local model file to delete for the selected model.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task ReloadModelAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            await _host.UnloadAsync(cancellationToken).ConfigureAwait(false);
            if (_store.IsModelPresent)
            {
                await EnsureReadyCoreAsync(progress: null, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public async Task<FimBenchmarkComparisonReport> RunSpeedBenchmarkAsync(
        int maxTokens,
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage,
        int debounceMs,
        int configuredGpuLayers,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_store.IsModelPresent)
        {
            throw new InvalidOperationException("Download / prepare the selected model before running the speed test.");
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A FIM model download/load is already in progress.");
        }

        try
        {
            return await FimSpeedBenchmark.RunComparisonAsync(
                _provider,
                _host,
                _store.CurrentModel.DisplayName,
                maxPromptTokens,
                prefixPercentage,
                suffixPercentage,
                maxTokens,
                debounceMs,
                configuredGpuLayers,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task EnsureReadyCoreAsync(IProgress<FimModelProgress>? progress, CancellationToken cancellationToken)
    {
        _store.EnsureModelsDirectory();
        var model = _store.CurrentModel;
        IProgress<FimModelProgress> combined = new Progress<FimModelProgress>(p =>
        {
            Debug.WriteLine($"[FIM:{model.Id}] {p.Message}");
            progress?.Report(p);
        });

        await _provider.EnsureReadyAsync(combined, cancellationToken).ConfigureAwait(false);
        _bridge.NotifyModelReady();
    }
}

/// <summary>Attaches / detaches <see cref="InlineCompletionController"/> for a SQL editor.</summary>
public sealed class FimEditorAttachment : IDisposable
{
    private readonly FimInlineCompletionBridge? _bridge;
    private InlineCompletionController? _controller;

    public FimEditorAttachment(FimInlineCompletionBridge? bridge)
    {
        _bridge = bridge;
        if (_bridge is not null)
        {
            _bridge.ModelReady += OnModelReady;
        }
    }

    public void Attach(TextEditor editor, Func<bool> isEnabled, Func<int>? getDebounceMs = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(isEnabled);
        Detach();
        if (_bridge is null)
        {
            return;
        }

        _controller = new InlineCompletionController(
            editor,
            (ctx, ct) => _bridge.CompleteAsync(ctx, ct),
            getDebounceMs: getDebounceMs,
            getIsEnabled: isEnabled,
            completionHost: editor as CodeTextEditor);
        _controller.Attach();
    }

    public void SyncEnabled(bool enabled)
    {
        if (_controller is not null)
        {
            _controller.IsEnabled = enabled;
        }
    }

    public void Detach()
    {
        _controller?.Dispose();
        _controller = null;
    }

    private void OnModelReady(object? sender, EventArgs e) => _controller?.RequestCompletion();

    public void Dispose()
    {
        Detach();
        if (_bridge is not null)
        {
            _bridge.ModelReady -= OnModelReady;
        }
    }
}

using JustyBase.Ai.Fim.Abstractions;
using JustyBase.Ai.Fim.Download;
using JustyBase.Ai.Fim.Prompting;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using System.Diagnostics;
using System.Text;

namespace JustyBase.Ai.Fim.LlamaSharp;

/// <summary>
/// Process-lifetime host for a GGUF FIM model (load once, reuse StatelessExecutor).
/// </summary>
public sealed class LlamaSharpModelHost : IAsyncDisposable
{
    private readonly IFimModelStore _modelStore;
    private readonly Func<int> _getGpuLayerCount;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _nativeUseGate = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _parameters;
    private StatelessExecutor? _executor;
    private string? _loadedModelPath;
    private int _loadedGpuLayerCount = int.MinValue;
    private int? _gpuLayerOverride;
    private bool _disposed;
    private static int _nativeConfigured;

    public LlamaSharpModelHost(
        IFimModelStore modelStore,
        Func<int>? getGpuLayerCount = null,
        uint contextSize = 4096)
    {
        _modelStore = modelStore;
        _getGpuLayerCount = getGpuLayerCount ?? (() => 0);
        ContextSize = contextSize;
    }

    public uint ContextSize { get; }
    public int EffectiveGpuLayerCount => _gpuLayerOverride ?? Math.Clamp(_getGpuLayerCount(), 0, 999);
    public bool IsLoaded => _executor is not null;
    public int LoadedGpuLayerCount => _loadedGpuLayerCount;

    /// <summary>Call once before first native load. PreferVulkan selects Vulkan backend when the package is present.</summary>
    public static void ConfigureNativeBackend(bool preferVulkan)
    {
        if (Interlocked.Exchange(ref _nativeConfigured, 1) != 0)
        {
            return;
        }

        NativeLibraryConfig.All
            .WithCuda(false)
            .WithVulkan(preferVulkan)
            .WithAutoFallback(true);
    }

    public void SetGpuLayerCountOverride(int? gpuLayerCount) => _gpuLayerOverride = gpuLayerCount;

    public async Task EnsureLoadedAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _nativeUseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await EnsureLoadedCoreAsync(allowDownload: true, progress, cancellationToken).ConfigureAwait(false); }
        finally { _nativeUseGate.Release(); }
    }

    /// <summary>
    /// Loads the GGUF only when it is already on disk. Used by editor autocomplete so typing never starts a multi-GB download.
    /// </summary>
    public async Task<bool> TryEnsureLoadedIfPresentAsync(CancellationToken cancellationToken = default)
    {
        await _nativeUseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await TryEnsureLoadedIfPresentCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _nativeUseGate.Release(); }
    }

    private async Task<bool> TryEnsureLoadedIfPresentCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var layers = EffectiveGpuLayerCount;
        if (IsLoaded
            && string.Equals(_loadedModelPath, _modelStore.LocalModelPath, StringComparison.OrdinalIgnoreCase)
            && _loadedGpuLayerCount == layers)
            return true;
        if (!_modelStore.IsModelPresent)
            return false;

        await EnsureLoadedCoreAsync(allowDownload: false, progress: null, cancellationToken).ConfigureAwait(false);
        return IsLoaded;
    }

    private async Task EnsureLoadedCoreAsync(
        bool allowDownload,
        IProgress<FimModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var desiredPath = _modelStore.LocalModelPath;
        var layers = EffectiveGpuLayerCount;
        if (_executor is not null
            && string.Equals(_loadedModelPath, desiredPath, StringComparison.OrdinalIgnoreCase)
            && _loadedGpuLayerCount == layers)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            desiredPath = _modelStore.LocalModelPath;
            layers = EffectiveGpuLayerCount;
            if (_executor is not null
                && string.Equals(_loadedModelPath, desiredPath, StringComparison.OrdinalIgnoreCase)
                && _loadedGpuLayerCount == layers)
            {
                return;
            }

            UnloadUnlocked();

            if (allowDownload)
            {
                await _modelStore.EnsureModelAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            else if (!_modelStore.IsModelPresent)
            {
                return;
            }

            progress?.Report(new FimModelProgress(
                0.99,
                $"Loading {_modelStore.CurrentModel.DisplayName} (gpu_layers={layers})…"));

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = _modelStore.LocalModelPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    throw new FileNotFoundException("FIM model file not found.", path);
                }

                var parameters = new ModelParams(path)
                {
                    ContextSize = ContextSize,
                    GpuLayerCount = layers,
                };
                var weights = LLamaWeights.LoadFromFile(parameters);
                var executor = new StatelessExecutor(weights, parameters);
                _parameters = parameters;
                _weights = weights;
                _loadedModelPath = path;
                _loadedGpuLayerCount = layers;
                Volatile.Write(ref _executor, executor);
            }, cancellationToken).ConfigureAwait(false);

            progress?.Report(new FimModelProgress(1.0, "FIM model ready."));
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>Releases loaded weights so the GGUF file can be deleted or replaced.</summary>
    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _nativeUseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { UnloadUnlocked(); }
            finally { _initGate.Release(); }
        }
        finally { _nativeUseGate.Release(); }
    }

    private void UnloadUnlocked()
    {
        _executor = null;
        _weights?.Dispose();
        _weights = null;
        _parameters = null;
        _loadedModelPath = null;
        _loadedGpuLayerCount = int.MinValue;
    }

    public async Task<string> InferAsync(
        string prompt,
        IReadOnlyList<string> antiPrompts,
        int maxTokens,
        float temperature,
        float topP,
        CancellationToken cancellationToken)
    {
        var timed = await InferTimedAsync(prompt, antiPrompts, maxTokens, temperature, topP, cancellationToken)
            .ConfigureAwait(false);
        return timed.Text;
    }

    public async Task<FimInferTiming> InferTimedAsync(
        string prompt,
        IReadOnlyList<string> antiPrompts,
        int maxTokens,
        float temperature,
        float topP,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(prompt);
        ArgumentNullException.ThrowIfNull(antiPrompts);

        await _nativeUseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryEnsureLoadedIfPresentCoreAsync(cancellationToken).ConfigureAwait(false))
                return new FimInferTiming(string.Empty, 0, 0, 0);

            var executor = _executor ?? throw new InvalidOperationException("FIM executor not initialized.");

        using var samplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = temperature,
            TopP = topP,
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = antiPrompts.Where(static s => !string.IsNullOrEmpty(s)).ToList(),
            SamplingPipeline = samplingPipeline,
        };

        return await Task.Run(async () =>
        {
            var sb = new StringBuilder();
            var yields = 0;
            var sw = Stopwatch.StartNew();
            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (token is not null)
                {
                    yields++;
                    sb.Append(token);
                }
            }

            sw.Stop();
            var raw = sb.ToString();
            var trimmed = TrimAtAntiPrompts(raw, antiPrompts);
            return new FimInferTiming(trimmed, yields, raw.Length, sw.ElapsedMilliseconds);
        }, cancellationToken).ConfigureAwait(false);
        }
        finally { _nativeUseGate.Release(); }
    }

    private static string TrimAtAntiPrompts(string text, IReadOnlyList<string> antiPrompts)
    {
        var result = text;
        foreach (var stop in antiPrompts)
        {
            var idx = result.IndexOf(stop, StringComparison.Ordinal);
            if (idx >= 0)
            {
                result = result[..idx];
            }
        }

        return result.TrimEnd('\0', '\r', '\n');
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _executor = null;
        _weights?.Dispose();
        _weights = null;
        _parameters = null;
        _loadedModelPath = null;
        _initGate.Dispose();
        _nativeUseGate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public readonly record struct FimInferTiming(string Text, int YieldCount, int RawChars, long ElapsedMs);

/// <summary>ICompletionProvider backed by embedded LLamaSharp FIM inference.</summary>
public sealed class LlamaSharpCompletionProvider : ICompletionProvider, IAsyncDisposable
{
    private readonly LlamaSharpModelHost _host;
    private readonly IFimPromptBuilder _promptBuilder;

    public LlamaSharpCompletionProvider(
        LlamaSharpModelHost host,
        IFimPromptBuilder? promptBuilder = null)
    {
        _host = host;
        _promptBuilder = promptBuilder ?? new QwenFimPromptBuilder();
    }

    public string Id => "llamasharp-fim";
    public string DisplayName => "Embedded FIM (LLamaSharp)";
    public bool IsAvailable => true;

    public Task EnsureReadyAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default) =>
        _host.EnsureLoadedAsync(progress, cancellationToken);

    public async Task<CompletionSuggestion?> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prompt = _promptBuilder.Build(request.Prefix, request.Suffix);
        var text = await _host.InferAsync(
            prompt,
            _promptBuilder.StopSequences,
            request.MaxTokens,
            request.Temperature,
            request.TopP,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = SanitizeCompletion(text);
        return string.IsNullOrEmpty(cleaned) ? null : new CompletionSuggestion(cleaned);
    }

    public async Task<FimInferTiming> CompleteTimedAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prompt = _promptBuilder.Build(request.Prefix, request.Suffix);
        var timed = await _host.InferTimedAsync(
            prompt,
            _promptBuilder.StopSequences,
            request.MaxTokens,
            request.Temperature,
            request.TopP,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(timed.Text))
        {
            return timed;
        }

        return timed with { Text = SanitizeCompletion(timed.Text) };
    }

    private static string SanitizeCompletion(string text)
    {
        var t = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blank = t.IndexOf("\n\n", StringComparison.Ordinal);
        if (blank >= 0)
        {
            t = t[..blank];
        }

        return t.TrimEnd();
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

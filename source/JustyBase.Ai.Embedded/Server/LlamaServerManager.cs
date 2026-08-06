using JustyBase.Ai.Embedded.Abstractions;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>Chat and FIM server roles for <see cref="LlamaServerManager"/>.</summary>
public enum LlamaServerRole
{
    Chat,
    Fim,
}

/// <summary>
/// Owns up to two llama-server subprocesses (one for the AI chat model, one for the FIM model).
/// Instances are reused while the requested model/parameters match.
/// </summary>
public sealed class LlamaServerManager : IAsyncDisposable
{
    private readonly LlamaServerBinaryManager _binary;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LlamaServerInstance? _chatServer;
    private LlamaServerInstance? _fimServer;
    private string? _chatModelPath;
    private string? _fimModelPath;
    private int _chatGpuLayers;
    private uint _chatContextSize;
    private int _fimGpuLayers;
    private uint _fimContextSize;
    private string? _chatBinaryVariant;
    private string? _fimBinaryVariant;

    public LlamaServerManager(LlamaServerBinaryManager binary)
    {
        _binary = binary ?? throw new ArgumentNullException(nameof(binary));
    }

    public LlamaServerInstance? ChatServer => _chatServer;
    public LlamaServerInstance? FimServer => _fimServer;

    /// <summary>Gets (or starts) the server for the given role, downloading the binary first.</summary>
    public async Task<LlamaServerInstance> GetOrStartServerAsync(
        LlamaServerRole role,
        string modelPath,
        int gpuLayers,
        uint contextSize,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = role == LlamaServerRole.Chat ? _chatServer : _fimServer;
            var currentPath = role == LlamaServerRole.Chat ? _chatModelPath : _fimModelPath;
            var currentGpu = role == LlamaServerRole.Chat ? _chatGpuLayers : _fimGpuLayers;
            var currentCtx = role == LlamaServerRole.Chat ? _chatContextSize : _fimContextSize;
            var currentVariant = role == LlamaServerRole.Chat ? _chatBinaryVariant : _fimBinaryVariant;
            var binaryVariant = _binary.BinaryVariant;

            if (current is { IsRunning: true }
                && string.Equals(currentPath, modelPath, StringComparison.OrdinalIgnoreCase)
                && currentGpu == gpuLayers
                && currentCtx == contextSize
                && string.Equals(currentVariant, binaryVariant, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            await StopServerCoreAsync(role).ConfigureAwait(false);

            if (!_binary.IsBinaryPresent)
            {
                await _binary.EnsureBinaryAsync(progress, cancellationToken).ConfigureAwait(false);
            }

            LlamaServerInstance? instance = null;
            try
            {
#pragma warning disable CA2000 // Ownership transfers to _chatServer/_fimServer (disposed in StopServerCoreAsync/DisposeAsync).
                instance = new LlamaServerInstance(_binary.BinaryPath, modelPath, gpuLayers, contextSize);
#pragma warning restore CA2000
                var started = await instance.StartAsync(progress, cancellationToken).ConfigureAwait(false);
                if (!started)
                {
                    throw new InvalidOperationException(
                        $"llama-server failed to start: {instance.LastError ?? "unknown error"}");
                }

                if (role == LlamaServerRole.Chat)
                {
                    _chatServer = instance;
                    _chatModelPath = modelPath;
                    _chatGpuLayers = gpuLayers;
                    _chatContextSize = contextSize;
                    _chatBinaryVariant = binaryVariant;
                }
                else
                {
                    _fimServer = instance;
                    _fimModelPath = modelPath;
                    _fimGpuLayers = gpuLayers;
                    _fimContextSize = contextSize;
                    _fimBinaryVariant = binaryVariant;
                }

                instance = null;
                return role == LlamaServerRole.Chat ? _chatServer! : _fimServer!;
            }
            finally
            {
                if (instance is not null)
                {
                    await instance.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopServerAsync(LlamaServerRole role, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopServerCoreAsync(role).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopServerCoreAsync(LlamaServerRole role)
    {
        if (role == LlamaServerRole.Chat)
        {
            if (_chatServer is not null)
            {
                await _chatServer.DisposeAsync().ConfigureAwait(false);
                _chatServer = null;
            }

            _chatModelPath = null;
            _chatGpuLayers = 0;
            _chatContextSize = 0;
            _chatBinaryVariant = null;
        }
        else
        {
            if (_fimServer is not null)
            {
                await _fimServer.DisposeAsync().ConfigureAwait(false);
                _fimServer = null;
            }

            _fimModelPath = null;
            _fimGpuLayers = 0;
            _fimContextSize = 0;
            _fimBinaryVariant = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_chatServer is not null)
            {
                await _chatServer.DisposeAsync().ConfigureAwait(false);
                _chatServer = null;
            }

            if (_fimServer is not null)
            {
                await _fimServer.DisposeAsync().ConfigureAwait(false);
                _fimServer = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}

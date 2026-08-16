using JustyBase.Ai.Embedded.Abstractions;
using JustyBase.Ai.Embedded.Server;

namespace JustyBase.Tests;

public sealed class LlamaServerManagerTests
{
    [Fact]
    public async Task GetOrStart_FirstCall_CreatesAndAssigns()
    {
        var (manager, factory) = CreateManager();
        var a = factory.Add();

        var instance = await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);

        Assert.Same(a, instance);
        Assert.Same(a, manager.FimServer);
    }

    [Fact]
    public async Task GetOrStart_SameParams_ReusesInstance()
    {
        var (manager, factory) = CreateManager();
        var a = factory.Add();

        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);
        var second = await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);

        Assert.Same(a, second);
        Assert.Equal(1, factory.TotalAdded);
    }

    [Fact]
    public async Task GetOrStart_NewParams_StartFailure_KeepsOldRunning()
    {
        var (manager, factory) = CreateManager();
        var a = factory.Add();
        var b = factory.Add(startResult: false, lastError: "boom");

        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-b", 0, 4096));

        Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
        Assert.True(b.Disposed);
        Assert.Same(a, manager.FimServer);
        Assert.False(a.Disposed);
        Assert.True(a.IsRunning);
    }

    [Fact]
    public async Task GetOrStart_NewParams_StartSuccess_SwapsAndDisposesOld()
    {
        var (manager, factory) = CreateManager();
        var a = factory.Add();
        var b = factory.Add();

        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);
        var instance = await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-b", 0, 4096);

        Assert.Same(b, instance);
        Assert.Same(b, manager.FimServer);
        Assert.True(a.Disposed);
    }

    [Fact]
    public async Task GetOrStart_CancelledDuringStart_DisposesNewKeepsOld()
    {
        var (manager, factory) = CreateManager();
        var a = factory.Add();
        var b = factory.Add(cancelStart: true);

        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);
        using var cts = new CancellationTokenSource();
        var startTask = manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-b", 0, 4096, cancellationToken: cts.Token);
        await b.StartedSignal.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);

        Assert.True(b.Disposed);
        Assert.Same(a, manager.FimServer);
        Assert.False(a.Disposed);
    }

    [Fact]
    public async Task StopServer_DisposesAndClears()
    {
        var (manager, factory) = CreateManager();
        var a = factory.Add();

        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "model-a", 0, 4096);
        await manager.StopServerAsync(LlamaServerRole.Fim);

        Assert.Null(manager.FimServer);
        Assert.True(a.Disposed);
    }

    [Fact]
    public async Task Dispose_DisposesBothRoles()
    {
        var (manager, factory) = CreateManager();
        var chat = factory.Add();
        var fim = factory.Add();

        await manager.GetOrStartServerAsync(LlamaServerRole.Chat, "chat-a", 0, 4096);
        await manager.GetOrStartServerAsync(LlamaServerRole.Fim, "fim-a", 0, 4096);

        await manager.DisposeAsync();

        Assert.True(chat.Disposed);
        Assert.True(fim.Disposed);
    }

    private static (LlamaServerManager Manager, FakeInstanceFactory Factory) CreateManager()
    {
        var binary = new FakeBinary();
        var factory = new FakeInstanceFactory();
        return (new LlamaServerManager(binary, (_, _, _, _) => factory.Create()), factory);
    }

    private sealed class FakeInstanceFactory
    {
        private readonly Queue<FakeInstance> _pending = new();
        public int TotalAdded { get; private set; }

        public FakeInstance Add(bool startResult = true, string? lastError = null, bool cancelStart = false)
        {
            var instance = new FakeInstance
            {
                StartResult = startResult,
                LastError = lastError,
                CancelStart = cancelStart
            };
            _pending.Enqueue(instance);
            TotalAdded++;
            return instance;
        }

        public ILlamaServerInstance Create() => _pending.Dequeue();
    }

    private sealed class FakeBinary : ILlamaServerBinary
    {
        public string BinaryPath => @"C:\fake\llama-server.exe";
        public bool IsBinaryPresent => true;
        public string BinaryVariant => "vulkan";

        public Task EnsureBinaryAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeInstance : ILlamaServerInstance
    {
        public bool StartResult { get; init; } = true;
        public string? LastError { get; init; }
        public bool CancelStart { get; init; }
        public bool Disposed { get; private set; }
        public bool IsRunning { get; private set; } = true;
        public string LogFilePath => string.Empty;
        public int Port => 12345;
        public Uri Endpoint => new("http://127.0.0.1:12345");
        public TaskCompletionSource StartedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> StartAsync(IProgress<FimModelProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            StartedSignal.TrySetResult();
            if (CancelStart)
            {
                // Simulate a cancellation observed while the start is in flight (after the gate).
                return WaitForCancellationAsync(cancellationToken);
            }

            if (!StartResult)
            {
                IsRunning = false;
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private static async Task<bool> WaitForCancellationAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => tcs.TrySetException(new OperationCanceledException(ct)));
            await tcs.Task.ConfigureAwait(false);
            return true;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}

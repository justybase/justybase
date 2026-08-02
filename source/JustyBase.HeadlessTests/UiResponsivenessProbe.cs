using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Xunit.Abstractions;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Measures Avalonia dispatcher stall while an async operation runs.
/// Must be invoked on the UI thread (e.g. inside <see cref="HeadlessSessionTestBase.RunOnUi"/>).
/// </summary>
/// <remarks>
/// Uses pump-loop sampling (RunJobs duration + inter-iteration gaps) instead of DispatcherTimer,
/// which is unreliable inside Avalonia headless <c>Dispatch</c> frames.
/// </remarks>
internal sealed class UiResponsivenessProbe
{
    public const int DefaultMaxStallMsBudget = 150;
    public const int DefaultMinTickCount = 5;
    public static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromMilliseconds(10);

    private readonly ITestOutputHelper? _output;

    public UiResponsivenessProbe(ITestOutputHelper? output = null)
    {
        _output = output;
    }

    public UiResponsivenessSnapshot RunDuring(
        string operationName,
        Func<Task> startOperation,
        TimeSpan timeout,
        int? injectedDelayMs = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("UiResponsivenessProbe.RunDuring must run on the UI thread.");
        }

        var sampleTimes = new List<long>(capacity: 512);
        var runJobsDurations = new List<long>(capacity: 512);
        var sw = Stopwatch.StartNew();

        Task operation;
        try
        {
            operation = startOperation() ?? Task.CompletedTask;
        }
        catch
        {
            throw;
        }

        while (!operation.IsCompleted && sw.Elapsed < timeout)
        {
            var before = sw.ElapsedMilliseconds;
            Dispatcher.UIThread.RunJobs();
            var after = sw.ElapsedMilliseconds;
            runJobsDurations.Add(after - before);
            sampleTimes.Add(after);
            Thread.Sleep(DefaultSampleInterval);
        }

        // Drain remaining dispatcher work after the operation completes.
        for (var i = 0; i < 5; i++)
        {
            var before = sw.ElapsedMilliseconds;
            Dispatcher.UIThread.RunJobs();
            var after = sw.ElapsedMilliseconds;
            runJobsDurations.Add(after - before);
            sampleTimes.Add(after);
            Thread.Sleep(5);
        }

        sw.Stop();

        if (!operation.IsCompleted)
        {
            throw new TimeoutException($"Operation '{operationName}' did not complete within {timeout}.");
        }

        if (operation.IsFaulted)
        {
            throw operation.Exception?.GetBaseException()
                ?? new InvalidOperationException($"Operation '{operationName}' faulted.");
        }

        long maxGap = 0;
        long gapSum = 0;
        var gapCount = 0;
        for (var i = 1; i < sampleTimes.Count; i++)
        {
            var gap = sampleTimes[i] - sampleTimes[i - 1];
            gapSum += gap;
            gapCount++;
            if (gap > maxGap)
            {
                maxGap = gap;
            }
        }

        long maxRunJobs = runJobsDurations.Count == 0 ? 0 : runJobsDurations.Max();
        var maxStall = Math.Max(maxGap, maxRunJobs);

        var snapshot = new UiResponsivenessSnapshot(
            OperationName: operationName,
            TickCount: sampleTimes.Count,
            MaxStallMs: maxStall,
            MeanTickGapMs: gapCount == 0 ? 0 : (double)gapSum / gapCount,
            ElapsedMs: sw.ElapsedMilliseconds,
            InjectedDelayMs: injectedDelayMs);

        _output?.WriteLine(
            $"RESPONSIVENESS operation={snapshot.OperationName} MaxStallMs={snapshot.MaxStallMs} " +
            $"TickCount={snapshot.TickCount} MeanTickGapMs={snapshot.MeanTickGapMs.ToString("F1", CultureInfo.InvariantCulture)} " +
            $"ElapsedMs={snapshot.ElapsedMs} InjectedDelayMs={snapshot.InjectedDelayMs?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
            $"MaxRunJobsMs={maxRunJobs}");

        return snapshot;
    }

    public static void AssertWithinBudget(
        UiResponsivenessSnapshot snapshot,
        int maxStallMs = DefaultMaxStallMsBudget,
        int minTickCount = DefaultMinTickCount)
    {
        Assert.True(
            snapshot.TickCount >= minTickCount,
            $"Expected at least {minTickCount} dispatcher samples during '{snapshot.OperationName}', got {snapshot.TickCount}.");
        Assert.True(
            snapshot.MaxStallMs <= maxStallMs,
            $"Max UI stall {snapshot.MaxStallMs}ms exceeded budget {maxStallMs}ms during '{snapshot.OperationName}' " +
            $"(samples={snapshot.TickCount}, elapsed={snapshot.ElapsedMs}ms, injectedDelay={snapshot.InjectedDelayMs?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms).");
    }
}

internal sealed record UiResponsivenessSnapshot(
    string OperationName,
    int TickCount,
    long MaxStallMs,
    double MeanTickGapMs,
    long ElapsedMs,
    int? InjectedDelayMs);

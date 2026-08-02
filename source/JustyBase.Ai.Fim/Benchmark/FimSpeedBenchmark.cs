using System.Diagnostics;
using System.Globalization;
using System.Text;
using JustyBase.Ai.Fim.Abstractions;
using JustyBase.Ai.Fim.LlamaSharp;
using JustyBase.Ai.Fim.Prompting;

namespace JustyBase.Ai.Fim.Benchmark;

/// <summary>
/// FIM speed test: prefill tok/s across context variants + generate tok/s,
/// comparing CPU (gpu_layers=0) vs GPU offload on the loaded native backend.
/// </summary>
public static class FimSpeedBenchmark
{
    public const int TimedRunCount = 2;
    public const int DecodeProbeMaxTokens = 32;

    public static CompletionRequest CreateSampleRequest(
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage,
        int maxTokens)
    {
        var (prefixLimit, suffixLimit) = FimPresets.ResolveCharBudgets(
            maxPromptTokens,
            prefixPercentage,
            suffixPercentage);
        var targetPrefix = Math.Max(64, (int)(prefixLimit * 0.85));
        var targetSuffix = Math.Max(32, (int)(suffixLimit * 0.85));

        const string corePrefix =
            """
            -- JustyBase FIM speed test (synthetic SQL around caret)
            SELECT
                c.customer_id,
                c.customer_name,
                o.order_id,
                o.order_date,

            """;

        const string coreSuffix =
            """

            FROM customers c
            INNER JOIN orders o ON o.customer_id = c.customer_id
            WHERE o.order_date >= CURRENT_DATE - INTERVAL '30' DAY
            ORDER BY o.order_date DESC;
            """;

        var prefix = PadToLength(corePrefix, targetPrefix, before: true);
        var suffix = PadToLength(coreSuffix, targetSuffix, before: false);
        return new CompletionRequest(
            prefix,
            suffix,
            MaxTokens: FimContextExtractor.ClampMaxTokens(maxTokens));
    }

    public static CompletionRequest CreateSampleRequest(string? contextWindow, int maxTokens)
    {
        var preset = FimPresets.Get(contextWindow);
        return CreateSampleRequest(
            preset.MaxPromptTokens,
            preset.PrefixPercentage,
            preset.SuffixPercentage,
            maxTokens);
    }

    public static CompletionRequest CreateDecodeProbeRequest(int maxTokens = DecodeProbeMaxTokens) =>
        new(
            "SELECT c.customer_id, ",
            " FROM customers c;",
            MaxTokens: Math.Clamp(maxTokens, 8, 64),
            Temperature: 0.1f);

    /// <summary>Builds Current + Small/Medium/Large context variants for a speed sweep.</summary>
    public static IReadOnlyList<FimBenchmarkContextVariant> BuildDefaultContextVariants(
        int currentMaxPromptTokens,
        double currentPrefixPercentage,
        double currentSuffixPercentage,
        int currentMaxTokens)
    {
        var list = new List<FimBenchmarkContextVariant>(4)
        {
            new(
                "Current",
                FimContextExtractor.ClampMaxPromptTokens(currentMaxPromptTokens),
                currentPrefixPercentage,
                currentSuffixPercentage,
                FimContextExtractor.ClampMaxTokens(currentMaxTokens)),
        };

        foreach (var preset in FimPresets.All)
        {
            list.Add(new(
                preset.Id,
                preset.MaxPromptTokens,
                preset.PrefixPercentage,
                preset.SuffixPercentage,
                preset.MaxGenerationTokens));
        }

        return list;
    }

    public static Task<FimBenchmarkComparisonReport> RunComparisonAsync(
        LlamaSharpCompletionProvider provider,
        LlamaSharpModelHost host,
        string modelDisplayName,
        string contextWindow,
        int maxTokens,
        int debounceMs,
        int configuredGpuLayers,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var preset = FimPresets.Get(contextWindow);
        return RunComparisonAsync(
            provider,
            host,
            modelDisplayName,
            BuildDefaultContextVariants(
                preset.MaxPromptTokens,
                preset.PrefixPercentage,
                preset.SuffixPercentage,
                maxTokens),
            debounceMs,
            configuredGpuLayers,
            progress,
            cancellationToken);
    }

    public static Task<FimBenchmarkComparisonReport> RunComparisonAsync(
        LlamaSharpCompletionProvider provider,
        LlamaSharpModelHost host,
        string modelDisplayName,
        int maxPromptTokens,
        double prefixPercentage,
        double suffixPercentage,
        int maxTokens,
        int debounceMs,
        int configuredGpuLayers,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunComparisonAsync(
            provider,
            host,
            modelDisplayName,
            BuildDefaultContextVariants(maxPromptTokens, prefixPercentage, suffixPercentage, maxTokens),
            debounceMs,
            configuredGpuLayers,
            progress,
            cancellationToken);

    public static async Task<FimBenchmarkComparisonReport> RunComparisonAsync(
        LlamaSharpCompletionProvider provider,
        LlamaSharpModelHost host,
        string modelDisplayName,
        IReadOnlyList<FimBenchmarkContextVariant> contexts,
        int debounceMs,
        int configuredGpuLayers,
        IProgress<FimModelProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(contexts);
        if (contexts.Count == 0)
        {
            throw new ArgumentException("At least one context variant is required.", nameof(contexts));
        }

        var profiles = new List<FimBenchmarkReport>(contexts.Count * 2);
        var layerTargets = configuredGpuLayers <= 0
            ? new[] { 0 }
            : new[] { 0, configuredGpuLayers };

        for (var i = 0; i < layerTargets.Length; i++)
        {
            var layers = layerTargets[i];
            var backendLabel = layers <= 0 ? "CPU" : "GPU";
            var baseFraction = (double)i / layerTargets.Length;
            var span = 1.0 / layerTargets.Length;

            progress?.Report(new FimModelProgress(
                baseFraction,
                $"{backendLabel}: loading…",
                IsIndeterminate: true));

            host.SetGpuLayerCountOverride(layers);
            await host.UnloadAsync(cancellationToken).ConfigureAwait(false);

            var loadSw = Stopwatch.StartNew();
            await host.EnsureLoadedAsync(progress, cancellationToken).ConfigureAwait(false);
            loadSw.Stop();

            // Generate tok/s is context-independent — measure once per backend.
            progress?.Report(new FimModelProgress(
                baseFraction + span * 0.15,
                $"{backendLabel}: generate probe…",
                IsIndeterminate: true));

            var decodeReq = CreateDecodeProbeRequest();
            _ = await provider.CompleteTimedAsync(decodeReq, cancellationToken).ConfigureAwait(false);
            var decodeRuns = new List<FimInferTiming>(TimedRunCount);
            for (var r = 0; r < TimedRunCount; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                decodeRuns.Add(await provider.CompleteTimedAsync(decodeReq, cancellationToken).ConfigureAwait(false));
            }

            var avgDecodeMs = decodeRuns.Average(static x => (double)x.ElapsedMs);
            var avgDecodeYields = decodeRuns.Average(static x => (double)x.YieldCount);
            var generateTokPerSec = avgDecodeMs > 0
                ? (avgDecodeYields > 0 ? avgDecodeYields : DecodeProbeMaxTokens) / (avgDecodeMs / 1000.0)
                : 0;

            for (var c = 0; c < contexts.Count; c++)
            {
                var ctx = contexts[c];
                var ctxFraction = baseFraction + span * (0.25 + 0.7 * ((c + 1.0) / contexts.Count));
                progress?.Report(new FimModelProgress(
                    ctxFraction,
                    $"{backendLabel} · {ctx.Label}: prefill…",
                    IsIndeterminate: true));

                profiles.Add(await RunContextPrefillAsync(
                    provider,
                    modelDisplayName,
                    backendLabel,
                    layers,
                    ctx,
                    debounceMs,
                    loadSw.ElapsedMilliseconds,
                    generateTokPerSec,
                    avgDecodeMs,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        host.SetGpuLayerCountOverride(null);
        await host.UnloadAsync(cancellationToken).ConfigureAwait(false);
        await host.EnsureLoadedAsync(progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new FimModelProgress(1.0, "Speed test finished."));
        return new FimBenchmarkComparisonReport(modelDisplayName, debounceMs, profiles);
    }

    private static async Task<FimBenchmarkReport> RunContextPrefillAsync(
        LlamaSharpCompletionProvider provider,
        string modelDisplayName,
        string backendLabel,
        int gpuLayers,
        FimBenchmarkContextVariant ctx,
        int debounceMs,
        long loadMs,
        double generateTokPerSec,
        double decodeProbeAvgMs,
        CancellationToken cancellationToken)
    {
        var tokens = FimContextExtractor.ClampMaxTokens(ctx.MaxTokens);
        var promptTokens = FimContextExtractor.ClampMaxPromptTokens(ctx.MaxPromptTokens);
        var request = CreateSampleRequest(
            promptTokens,
            ctx.PrefixPercentage,
            ctx.SuffixPercentage,
            tokens);
        var (prefixLimit, suffixLimit) = FimPresets.ResolveCharBudgets(
            promptTokens,
            ctx.PrefixPercentage,
            ctx.SuffixPercentage);
        var windowLabel =
            $"{ctx.Label} · {promptTokens} tok ({(int)(ctx.PrefixPercentage * 100)}%/{(int)(ctx.SuffixPercentage * 100)}%)";

        var prefillReq = request with { MaxTokens = 1 };
        _ = await provider.CompleteTimedAsync(prefillReq, cancellationToken).ConfigureAwait(false);

        var promptTokenEstimate = Math.Max(
            1,
            (request.Prefix.Length + request.Suffix.Length) / FimPresets.ApproxCharsPerToken);
        var prefillRuns = new List<FimInferTiming>(TimedRunCount);
        for (var i = 0; i < TimedRunCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            prefillRuns.Add(await provider.CompleteTimedAsync(prefillReq, cancellationToken).ConfigureAwait(false));
        }

        var avgPrefillMs = prefillRuns.Average(static r => (double)r.ElapsedMs);
        var prefillTokPerSec = avgPrefillMs > 0
            ? promptTokenEstimate / (avgPrefillMs / 1000.0)
            : 0;

        var e2e = await provider.CompleteTimedAsync(request, cancellationToken).ConfigureAwait(false);
        var runs = new List<FimBenchmarkRun>
        {
            new(1, e2e.ElapsedMs, e2e.Text.Length, e2e.YieldCount, TrimPreview(e2e.Text)),
        };

        return new FimBenchmarkReport(
            ModelDisplayName: modelDisplayName,
            ProfileLabel: $"{backendLabel} · {ctx.Label}",
            BackendLabel: backendLabel,
            ContextLabel: ctx.Label,
            GpuLayers: gpuLayers,
            ContextWindow: windowLabel,
            PrefixChars: request.Prefix.Length,
            SuffixChars: request.Suffix.Length,
            PrefixLimit: prefixLimit,
            SuffixLimit: suffixLimit,
            MaxTokens: tokens,
            DebounceMs: Math.Clamp(debounceMs <= 0 ? 600 : debounceMs, 250, 3000),
            LoadMs: loadMs,
            WarmupMs: 0,
            WarmupPreview: TrimPreview(e2e.Text),
            Runs: runs,
            AverageE2eMs: e2e.ElapsedMs,
            MinE2eMs: e2e.ElapsedMs,
            MaxE2eMs: e2e.ElapsedMs,
            PrefillTokPerSec: prefillTokPerSec,
            GenerateTokPerSec: generateTokPerSec,
            PromptTokenEstimate: promptTokenEstimate,
            DecodeProbeAvgMs: decodeProbeAvgMs,
            DecodeProbeTokPerSec: generateTokPerSec,
            Verdict: EvaluateLatency(
                e2e.ElapsedMs,
                debounceMs,
                ctx.Label,
                gpuLayers > 0));
    }

    public static string FormatComparison(FimBenchmarkComparisonReport comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var profiles = comparison.Profiles;
        if (profiles.Count == 0)
        {
            return "No benchmark profiles.";
        }

        var contextLabels = profiles
            .Select(static p => p.ContextLabel)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var backends = profiles
            .Select(static p => p.BackendLabel)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        sb.AppendLine(inv, $"Model: {comparison.ModelDisplayName}");
        sb.AppendLine(inv, $"Suggestion delay: {comparison.DebounceMs} ms");
        sb.AppendLine();

        sb.AppendLine("PREFILL tok/s  (higher is better — depends on context size)");
        AppendMatrix(sb, profiles, contextLabels, backends, static p => p.PrefillTokPerSec, inv);
        sb.AppendLine();

        sb.AppendLine("GENERATE tok/s  (higher is better — decode; same for all contexts)");
        sb.AppendLine(inv, $"{"Backend",-8} {"tok/s",10}");
        foreach (var backend in backends)
        {
            var sample = profiles.First(p => p.BackendLabel == backend);
            sb.AppendLine(inv, $"{backend,-8} {sample.GenerateTokPerSec,10:0.0}");
        }

        if (backends.Length == 2)
        {
            var cpu = profiles.First(static p => p.BackendLabel == "CPU");
            var gpu = profiles.First(static p => p.BackendLabel == "GPU");
            var genX = cpu.GenerateTokPerSec > 0 && gpu.GenerateTokPerSec > 0
                ? gpu.GenerateTokPerSec / cpu.GenerateTokPerSec
                : 0;
            sb.AppendLine(inv, $"GPU vs CPU generate: {genX:0.00}×");
            if (genX is > 0 and < 1.15)
            {
                sb.AppendLine("  (GPU offload did not meaningfully speed up decode on this run.)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("E2E latency (ms)  (lower is better — full MaxTokens sample; Tab accept earlier feels faster)");
        AppendMatrix(sb, profiles, contextLabels, backends, static p => p.AverageE2eMs, inv, "0");

        sb.AppendLine();
        sb.AppendLine("Notes");
        foreach (var backend in backends)
        {
            var current = profiles.FirstOrDefault(p =>
                p.BackendLabel == backend
                && string.Equals(p.ContextLabel, "Current", StringComparison.Ordinal));
            if (current is not null)
            {
                sb.AppendLine(inv, $"  {backend} @ Current: ~{current.PromptTokenEstimate} tok · {current.Verdict}");
            }
        }

        var small = profiles.FirstOrDefault(static p =>
            string.Equals(p.ContextLabel, "Small", StringComparison.Ordinal)
            && p.BackendLabel == "GPU")
            ?? profiles.FirstOrDefault(static p =>
                string.Equals(p.ContextLabel, "Small", StringComparison.Ordinal));
        var currentAny = profiles.FirstOrDefault(static p =>
            string.Equals(p.ContextLabel, "Current", StringComparison.Ordinal));
        if (small is not null
            && currentAny is not null
            && currentAny.AverageE2eMs > 1500
            && small.AverageE2eMs < currentAny.AverageE2eMs * 0.7)
        {
            sb.AppendLine(inv, $"  Tip: Small context e2e ~{small.AverageE2eMs:0} ms vs Current ~{currentAny.AverageE2eMs:0} ms.");
        }

        var sampleOut = profiles.Select(p => p.WarmupPreview).FirstOrDefault(static s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(sampleOut))
        {
            sb.AppendLine(inv, $"  Sample: {sampleOut}");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendMatrix(
        StringBuilder sb,
        IReadOnlyList<FimBenchmarkReport> profiles,
        IReadOnlyList<string> contextLabels,
        IReadOnlyList<string> backends,
        Func<FimBenchmarkReport, double> selector,
        CultureInfo inv,
        string format = "0.0")
    {
        const int col = 10;
        sb.Append(inv, $"{"Backend",-8}");
        foreach (var label in contextLabels)
        {
            var header = label.Length <= col ? label : label[..(col - 1)] + "…";
            sb.Append(inv, $" {header,col}");
        }

        sb.AppendLine();

        foreach (var backend in backends)
        {
            sb.Append(inv, $"{backend,-8}");
            foreach (var label in contextLabels)
            {
                var report = profiles.FirstOrDefault(p =>
                    p.BackendLabel == backend
                    && string.Equals(p.ContextLabel, label, StringComparison.Ordinal));
                if (report is null)
                {
                    sb.Append(inv, $" {"—",col}");
                    continue;
                }

                var value = selector(report);
                sb.Append(inv, $" {value.ToString(format, inv),col}");
            }

            sb.AppendLine();
        }
    }

    public static string EvaluateLatency(
        double averageMs,
        int debounceMs,
        string? contextLabel = null,
        bool alreadyOnGpu = false)
    {
        // E2E is a worst-case full MaxTokens completion after the suggestion delay.
        // Perceived wait ≈ debounce + e2e; thresholds are intentionally lenient.
        _ = debounceMs;

        var isSmall = string.Equals(contextLabel, "Small", StringComparison.OrdinalIgnoreCase);

        if (averageMs <= 500)
        {
            return "Excellent for inline ghost text.";
        }

        if (averageMs <= 1200)
        {
            return "Good — feels responsive after the suggestion delay.";
        }

        if (averageMs <= 2500)
        {
            return "Usable — short wait after typing pause; fine for occasional suggestions.";
        }

        if (averageMs <= 5000)
        {
            if (isSmall)
            {
                return alreadyOnGpu
                    ? "Noticeable delay even on Small — lower max generation tokens."
                    : "Noticeable delay even on Small — enable GPU offload or lower max tokens.";
            }

            return alreadyOnGpu
                ? "Noticeable delay — prefer Small context or fewer generation tokens."
                : "Noticeable delay — try GPU offload, Small context, or fewer generation tokens.";
        }

        if (isSmall)
        {
            return "Too slow for comfortable ghost text on Small — keep FIM off or cut max tokens hard.";
        }

        return "Too slow for comfortable ghost text at this context size — use Small or keep FIM off.";
    }

    private static string PadToLength(string core, int targetLength, bool before)
    {
        if (core.Length >= targetLength)
        {
            return before ? core[^targetLength..] : core[..targetLength];
        }

        var sb = new StringBuilder(targetLength);
        if (before)
        {
            var n = 1;
            while (sb.Length + core.Length < targetLength)
            {
                var line = $"-- pad {n++} synthetic context line for FIM speed test{Environment.NewLine}";
                var remaining = targetLength - core.Length - sb.Length;
                if (line.Length > remaining)
                {
                    sb.Append(line.AsSpan(0, remaining));
                    break;
                }

                sb.Append(line);
            }

            sb.Append(core);
        }
        else
        {
            sb.Append(core);
            var n = 1;
            while (sb.Length < targetLength)
            {
                var line = $"{Environment.NewLine}-- pad {n++} trailing context";
                var remaining = targetLength - sb.Length;
                if (line.Length > remaining)
                {
                    sb.Append(line.AsSpan(0, remaining));
                    break;
                }

                sb.Append(line);
            }
        }

        return sb.ToString();
    }

    private static string? TrimPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 80 ? oneLine : oneLine[..77] + "…";
    }
}

public sealed record FimBenchmarkContextVariant(
    string Label,
    int MaxPromptTokens,
    double PrefixPercentage,
    double SuffixPercentage,
    int MaxTokens);

public sealed record FimBenchmarkRun(
    int Index,
    long ElapsedMs,
    int OutputChars,
    int YieldCount,
    string? Preview);

public sealed record FimBenchmarkReport(
    string ModelDisplayName,
    string ProfileLabel,
    string BackendLabel,
    string ContextLabel,
    int GpuLayers,
    string ContextWindow,
    int PrefixChars,
    int SuffixChars,
    int PrefixLimit,
    int SuffixLimit,
    int MaxTokens,
    int DebounceMs,
    long LoadMs,
    long WarmupMs,
    string? WarmupPreview,
    IReadOnlyList<FimBenchmarkRun> Runs,
    double AverageE2eMs,
    long MinE2eMs,
    long MaxE2eMs,
    double PrefillTokPerSec,
    double GenerateTokPerSec,
    int PromptTokenEstimate,
    double DecodeProbeAvgMs,
    double DecodeProbeTokPerSec,
    string Verdict);

public sealed record FimBenchmarkComparisonReport(
    string ModelDisplayName,
    int DebounceMs,
    IReadOnlyList<FimBenchmarkReport> Profiles)
{
    public FimBenchmarkComparisonReport(IReadOnlyList<FimBenchmarkReport> profiles)
        : this(
            profiles.FirstOrDefault()?.ModelDisplayName ?? "",
            profiles.FirstOrDefault()?.DebounceMs ?? 600,
            profiles)
    {
    }
}

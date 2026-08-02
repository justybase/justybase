using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.HeadlessTests;

internal static class ResponsivenessMetricsWriter
{
    private static readonly object Gate = new();

    public static string ResolvePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RESPONSIVENESS_METRICS_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "responsiveness-metrics.jsonl");
    }

    public static void Append(string testName, UiResponsivenessSnapshot snapshot)
    {
        var path = ResolvePath();
        var metric = new ResponsivenessMetric
        {
            Test = testName,
            Operation = snapshot.OperationName,
            MaxStallMs = snapshot.MaxStallMs,
            TickCount = snapshot.TickCount,
            MeanTickGapMs = Math.Round(snapshot.MeanTickGapMs, 2),
            ElapsedMs = snapshot.ElapsedMs,
            InjectedDelayMs = snapshot.InjectedDelayMs,
            Timestamp = DateTimeOffset.UtcNow
        };
        var line = JsonSerializer.Serialize(metric, ResponsivenessJsonContext.Default.ResponsivenessMetric);

        lock (Gate)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}

internal sealed class ResponsivenessMetric
{
    public required string Test { get; init; }
    public required string Operation { get; init; }
    public long MaxStallMs { get; init; }
    public int TickCount { get; init; }
    public double MeanTickGapMs { get; init; }
    public long ElapsedMs { get; init; }
    public int? InjectedDelayMs { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(ResponsivenessMetric))]
internal partial class ResponsivenessJsonContext : JsonSerializerContext
{
}

using JustyBase.Ai.Fim.Benchmark;
using JustyBase.Ai.Fim.Download;
using JustyBase.Ai.Fim.Prompting;

namespace JustyBase.Tests;

public sealed class FimPromptBuilderTests
{
    [Fact]
    public void Qwen_Build_UsesOfficialFimTokens()
    {
        var builder = new QwenFimPromptBuilder();
        var prompt = builder.Build("SELECT ", " FROM t");
        Assert.Equal("<|fim_prefix|>SELECT <|fim_suffix|> FROM t<|fim_middle|>", prompt);
        Assert.Contains("<|endoftext|>", builder.StopSequences);
    }

    [Fact]
    public void DeepSeek_Build_UsesOfficialFimTokens()
    {
        var builder = new DeepSeekFimPromptBuilder();
        var prompt = builder.Build("SELECT ", " FROM t");
        Assert.Equal("<｜fim▁begin｜>SELECT <｜fim▁hole｜> FROM t<｜fim▁end｜>", prompt);
    }

    [Fact]
    public void ContextExtractor_RespectsLimitsAndCaret()
    {
        var text = new string('a', 5000) + "|" + new string('b', 2000);
        var caret = 5000;
        var (prefix, suffix) = FimContextExtractor.Extract(text, caret, prefixLimit: 100, suffixLimit: 50);
        Assert.Equal(100, prefix.Length);
        Assert.Equal(50, suffix.Length);
        Assert.Equal(new string('a', 100), prefix);
        Assert.Equal("|" + new string('b', 49), suffix);
    }

    [Theory]
    [InlineData("Small", 1229, 819)]
    [InlineData("medium", 3994, 2150)]
    [InlineData("LARGE", 11469, 4915)]
    [InlineData(null, 3994, 2150)]
    public void ContextExtractor_ResolveWindowLimits(string? window, int prefix, int suffix)
    {
        var (p, s) = FimContextExtractor.ResolveWindowLimits(window);
        Assert.Equal(prefix, p);
        Assert.Equal(suffix, s);
    }

    [Theory]
    [InlineData(512, 0.60, 0.40, 1229, 819)]
    [InlineData(1536, 0.65, 0.35, 3994, 2150)]
    [InlineData(4096, 0.70, 0.30, 11469, 4915)]
    public void FimPresets_ResolveCharBudgets(
        int maxPromptTokens,
        double prefixPct,
        double suffixPct,
        int expectedPrefix,
        int expectedSuffix)
    {
        var (p, s) = FimPresets.ResolveCharBudgets(maxPromptTokens, prefixPct, suffixPct);
        Assert.Equal(expectedPrefix, p);
        Assert.Equal(expectedSuffix, s);
    }

    [Theory]
    [InlineData(FimGpuClass.None, "Small")]
    [InlineData(FimGpuClass.Integrated, "Medium")]
    [InlineData(FimGpuClass.Discrete, "Large")]
    public void FimHardwareProfiler_SuggestPresetId(FimGpuClass gpuClass, string expected)
    {
        Assert.Equal(expected, FimHardwareProfiler.SuggestPresetId(gpuClass));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(47, 50)]
    [InlineData(200, 200)]
    [InlineData(999, 200)]
    public void ContextExtractor_ClampMaxTokens(int input, int expected)
    {
        Assert.Equal(expected, FimContextExtractor.ClampMaxTokens(input));
    }

    [Theory]
    [InlineData(350, 600, "Excellent")]
    [InlineData(700, 600, "Good")]
    [InlineData(1800, 600, "Usable")]
    [InlineData(3500, 600, "Noticeable")]
    [InlineData(7000, 600, "Too slow")]
    public void SpeedBenchmark_EvaluateLatency_HasGuidance(double avgMs, int debounceMs, string expectedFragment)
    {
        Assert.Contains(expectedFragment, FimSpeedBenchmark.EvaluateLatency(avgMs, debounceMs), StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedBenchmark_CreateSampleRequest_RespectsWindowAndTokens()
    {
        var request = FimSpeedBenchmark.CreateSampleRequest("Large", 80);
        Assert.Equal(80, request.MaxTokens);
        // Large = 4096 tok × 4 chars × 0.70/0.30, sample uses ~85% of limits
        Assert.InRange(request.Prefix.Length, 9000, 11469);
        Assert.InRange(request.Suffix.Length, 3500, 4915);
        Assert.Contains("SELECT", request.Prefix, StringComparison.Ordinal);
        Assert.Contains("FROM customers", request.Suffix, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedBenchmark_CreateSampleRequest_UsesExplicitBudgets()
    {
        var request = FimSpeedBenchmark.CreateSampleRequest(512, 0.60, 0.40, 30);
        Assert.Equal(30, request.MaxTokens);
        Assert.InRange(request.Prefix.Length, 800, 1229);
        Assert.InRange(request.Suffix.Length, 500, 819);
    }

    [Fact]
    public void Catalog_ResolvesDefaultAndLicenseModels()
    {
        var catalog = new FimModelCatalog();
        Assert.Equal(FimModelIds.Qwen25Coder3B, catalog.Resolve(null).Id);
        Assert.Equal(FimModelIds.Qwen25Coder15B, catalog.Resolve(FimModelIds.Qwen25Coder15B).Id);
        Assert.Equal(FimModelIds.Qwen25Coder3B, catalog.Resolve(FimModelIds.Qwen25Coder3B).Id);
        Assert.Equal(FimModelIds.Qwen25Coder7B, catalog.Resolve(FimModelIds.Qwen25Coder7B).Id);
        Assert.Contains("1.5B", catalog.Resolve(FimModelIds.Qwen25Coder15B).FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3B", catalog.Resolve(FimModelIds.Qwen25Coder3B).FileName, StringComparison.OrdinalIgnoreCase);

        var codestral = catalog.Resolve(FimModelIds.Codestral22B);
        Assert.True(codestral.RequiresLicenseAcceptance);
        Assert.Contains("MNPL", codestral.LicenseName, StringComparison.OrdinalIgnoreCase);

        var gemma = catalog.Resolve(FimModelIds.CodeGemma2B);
        Assert.True(gemma.RequiresLicenseAcceptance);
    }

    [Fact]
    public void SpeedBenchmark_FormatComparison_ShowsContextMatrix()
    {
        var profiles = new[]
        {
            MakeReport("CPU", "Current", 10, 40, 800),
            MakeReport("CPU", "Small", 50, 40, 200),
            MakeReport("GPU", "Current", 80, 120, 300),
            MakeReport("GPU", "Small", 200, 120, 80),
        };
        var text = FimSpeedBenchmark.FormatComparison(new FimBenchmarkComparisonReport("TestModel", 600, profiles));
        Assert.Contains("PREFILL tok/s", text, StringComparison.Ordinal);
        Assert.Contains("GENERATE tok/s", text, StringComparison.Ordinal);
        Assert.Contains("Current", text, StringComparison.Ordinal);
        Assert.Contains("Small", text, StringComparison.Ordinal);
        Assert.Contains("CPU", text, StringComparison.Ordinal);
        Assert.Contains("GPU", text, StringComparison.Ordinal);
        Assert.Contains("TestModel", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedBenchmark_BuildDefaultContextVariants_IncludesCurrentAndPresets()
    {
        var variants = FimSpeedBenchmark.BuildDefaultContextVariants(1536, 0.65, 0.35, 50);
        Assert.Equal("Current", variants[0].Label);
        Assert.Contains(variants, v => v.Label == "Small");
        Assert.Contains(variants, v => v.Label == "Medium");
        Assert.Contains(variants, v => v.Label == "Large");
        Assert.Equal(1536, variants[0].MaxPromptTokens);
    }

    private static FimBenchmarkReport MakeReport(
        string backend,
        string context,
        double prefill,
        double generate,
        double e2e) =>
        new(
            ModelDisplayName: "TestModel",
            ProfileLabel: $"{backend} · {context}",
            BackendLabel: backend,
            ContextLabel: context,
            GpuLayers: backend == "CPU" ? 0 : 99,
            ContextWindow: context,
            PrefixChars: 100,
            SuffixChars: 50,
            PrefixLimit: 100,
            SuffixLimit: 50,
            MaxTokens: 50,
            DebounceMs: 600,
            LoadMs: 1,
            WarmupMs: 0,
            WarmupPreview: "preview",
            Runs: [],
            AverageE2eMs: e2e,
            MinE2eMs: (long)e2e,
            MaxE2eMs: (long)e2e,
            PrefillTokPerSec: prefill,
            GenerateTokPerSec: generate,
            PromptTokenEstimate: 100,
            DecodeProbeAvgMs: 10,
            DecodeProbeTokPerSec: generate,
            Verdict: "Good");
}

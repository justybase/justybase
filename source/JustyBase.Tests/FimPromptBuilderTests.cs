using JustyBase.Ai.Embedded.Download;
using JustyBase.Ai.Embedded.Prompting;

namespace JustyBase.Tests;

public sealed class FimPromptBuilderTests
{
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
    [InlineData(0, 50)]
    [InlineData(47, 50)]
    [InlineData(200, 200)]
    [InlineData(999, 200)]
    public void ContextExtractor_ClampMaxTokens(int input, int expected)
    {
        Assert.Equal(expected, FimContextExtractor.ClampMaxTokens(input));
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
        Assert.Equal("Qwen2.5-Coder-7B.Q4_K_M.gguf", catalog.Resolve(FimModelIds.Qwen25Coder7B).FileName);
        Assert.StartsWith("https://huggingface.co/QuantFactory/", catalog.Resolve(FimModelIds.Qwen25Coder7B).DownloadUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

        var codestral = catalog.Resolve(FimModelIds.Codestral22B);
        Assert.True(codestral.RequiresLicenseAcceptance);
        Assert.Contains("MNPL", codestral.LicenseName, StringComparison.OrdinalIgnoreCase);

        var gemma = catalog.Resolve(FimModelIds.CodeGemma2B);
        Assert.True(gemma.RequiresLicenseAcceptance);
    }

    [Fact]
    public void EmbeddedChatCatalog_ContainsRequestedModels()
    {
        var catalog = new EmbeddedChatModelCatalog();
        var ids = catalog.Models.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(EmbeddedChatModelIds.Qwen35_4B, catalog.Resolve(null).Id);
        Assert.Contains(EmbeddedChatModelIds.Qwen35_4B, ids);
        Assert.Contains(EmbeddedChatModelIds.Qwen35_9B, ids);
        Assert.Contains(EmbeddedChatModelIds.Qwen36_27B, ids);
        Assert.Contains(EmbeddedChatModelIds.Qwen36_35BA3B, ids);
        Assert.Contains(EmbeddedChatModelIds.Gemma4_12B, ids);
        Assert.Contains(EmbeddedChatModelIds.Gemma4_26BA4B, ids);
        Assert.Contains(EmbeddedChatModelIds.Gemma4_31B, ids);
        Assert.Contains(EmbeddedChatModelIds.Devstral2_22B, ids);
    }

    [Fact]
    public void EmbeddedChatCatalog_AllLinksAreTrustedQ4Sources()
    {
        var catalog = new EmbeddedChatModelCatalog();
        Assert.NotEmpty(catalog.Models);

        foreach (var model in catalog.Models)
        {
            Assert.True(model.DownloadUri.IsAbsoluteUri, $"{model.Id}: DownloadUri must be absolute");
            Assert.Equal("huggingface.co", model.DownloadUri.Host);
            var path = model.DownloadUri.AbsolutePath;
            Assert.True(
                path.Contains("/unsloth/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/google/", StringComparison.OrdinalIgnoreCase),
                $"{model.Id}: DownloadUri must point at unsloth or the official provider (got {path})");
            Assert.EndsWith(".gguf", model.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(model.FileName, model.DownloadUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EmbeddedChatCatalog_LicenseModelsRequireAcceptance()
    {
        var catalog = new EmbeddedChatModelCatalog();
        Assert.True(catalog.Resolve(EmbeddedChatModelIds.Gemma4_12B).RequiresLicenseAcceptance);
        Assert.True(catalog.Resolve(EmbeddedChatModelIds.Gemma4_31B).RequiresLicenseAcceptance);
        Assert.True(catalog.Resolve(EmbeddedChatModelIds.Gemma4_26BA4B).RequiresLicenseAcceptance);
        Assert.True(catalog.Resolve(EmbeddedChatModelIds.Devstral2_22B).RequiresLicenseAcceptance);
        Assert.False(catalog.Resolve(EmbeddedChatModelIds.Qwen35_4B).RequiresLicenseAcceptance);
    }
}

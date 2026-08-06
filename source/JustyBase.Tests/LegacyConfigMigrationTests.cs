using JustyBase.Common;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class LegacyConfigMigrationTests
{
    [Fact]
    public void Migrate_MapsLegacyFimKeysOntoNewSchema()
    {
        const string raw = """
            {
              "EnableEmbeddedFimAi": true,
              "EmbeddedFimModelId": "qwen2.5-coder-1.5b",
              "EmbeddedFimDebounceMs": 1000,
              "EmbeddedFimMaxTokens": 80,
              "EmbeddedFimPreset": "Large",
              "EmbeddedFimGpuLayers": 42,
              "EmbeddedFimPreferVulkan": false,
              "EmbeddedFimAcceptedLicenseModelIds": ["codestral-22b"]
            }
            """;

        var config = new AppOptions();
        LegacyConfigMigration.Migrate(config, raw);

        Assert.True(config.EnableFimServer);
        Assert.Equal("qwen2.5-coder-1.5b", config.FimModelId);
        Assert.Equal(1000, config.FimDebounceMs);
        Assert.Equal(80, config.FimMaxTokens);
        Assert.Equal("Large", config.FimPreset);
        Assert.Equal(42, config.FimGpuLayers);
        Assert.False(config.LlamaServerPreferVulkan);
        Assert.Contains("codestral-22b", config.FimAcceptedLicenseModelIds);
    }

    [Theory]
    [InlineData("\"ollama\"", "http://localhost:11434/v1")]
    [InlineData("\"lmstudio\"", "http://localhost:1234/v1")]
    public void Migrate_ConsolidatesLegacyBackendIds(string legacyId, string expectedEndpoint)
    {
        const string raw = """{ "AiChatBackendId": %ID% }""";

        var config = new AppOptions { AiChatOpenAiCompatibleEndpoint = string.Empty };
        LegacyConfigMigration.Migrate(config, raw.Replace("%ID%", legacyId));

        Assert.Equal("openai-compatible", config.AiChatBackendId);
        Assert.Equal(expectedEndpoint, config.AiChatOpenAiCompatibleEndpoint);
    }

    [Fact]
    public void Migrate_KeepsNewSchemaValuesWhenPresent()
    {
        const string raw = """
            {
              "EnableFimServer": false,
              "EnableEmbeddedFimAi": true,
              "FimModelId": "qwen2.5-coder-7b",
              "EmbeddedFimModelId": "qwen2.5-coder-1.5b"
            }
            """;

        // Like production: deserialize onto the new schema first, then migrate legacy keys.
        var config = System.Text.Json.JsonSerializer.Deserialize(
            raw,
            JustyBase.Common.MyJsonContextAppOptions.Default.AppOptions) ?? new AppOptions();
        LegacyConfigMigration.Migrate(config, raw);

        // New-schema keys win over legacy ones.
        Assert.False(config.EnableFimServer);
        Assert.Equal("qwen2.5-coder-7b", config.FimModelId);
    }

    [Fact]
    public void Migrate_IgnoresEmptyOrInvalidJson()
    {
        var config = new AppOptions();
        LegacyConfigMigration.Migrate(config, null);
        LegacyConfigMigration.Migrate(config, "");
        LegacyConfigMigration.Migrate(config, "not json");
        Assert.Equal("qwen2.5-coder-3b", config.FimModelId);
    }
}

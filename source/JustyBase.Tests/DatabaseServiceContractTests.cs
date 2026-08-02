using JustyBase.PluginCommon.Enums;

namespace JustyBase.Tests;

public sealed class DatabaseServiceContractTests
{
    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void QuoteNameIfNeeded_ShouldQuoteUnsafeNames_AcrossAvailablePlugins(Type pluginType)
    {
        var service = PluginTestDiscovery.CreateInstance(pluginType);
        var quotedName = service.QuoteNameIfNeeded("bad name");
        var escapedQuotesName = service.QuoteNameIfNeeded("A\"B");

        Assert.StartsWith("\"", quotedName, StringComparison.Ordinal);
        Assert.EndsWith("\"", quotedName, StringComparison.Ordinal);
        Assert.Contains("\"\"", escapedQuotesName, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void CoreContractMethods_ShouldBeStable_AcrossAvailablePlugins(Type pluginType)
    {
        var service = PluginTestDiscovery.CreateInstance(pluginType);

        service.ClearCachedData();
        service.ClearCachedData();

        Assert.Empty(service.GetDatabases(string.Empty));
        Assert.Equal("MixedCase", service.CleanSqlWord("\"MixedCase\"", CurrentAutoCompletDatabaseMode.MakeUpperCase));
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void CoreReadMethods_ShouldReturnEmpty_WhenCacheIsNotInitialized(Type pluginType)
    {
        var service = PluginTestDiscovery.CreateInstance(pluginType);

        Assert.Empty(service.GetSchemas(null!, string.Empty));
        Assert.Empty(service.GetDbObjects(null!, null!, string.Empty, TypeInDatabaseEnum.Table));
        Assert.Empty(service.GetColumns(null, null, null, string.Empty));
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void QuoteNameIfNeeded_ShouldPreserveSafeNames_AcrossAvailablePlugins(Type pluginType)
    {
        var service = PluginTestDiscovery.CreateInstance(pluginType);
        var result = service.QuoteNameIfNeeded("SAFE_NAME_123");

        Assert.Equal("SAFE_NAME_123", result.Trim('"'));
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void CleanSqlWord_ShouldNormalizeQuotedAndUnquotedWords(Type pluginType)
    {
        var service = PluginTestDiscovery.CreateInstance(pluginType);

        var quoted = service.CleanSqlWord("\"CamelCase\"", CurrentAutoCompletDatabaseMode.MakeUpperCase);
        var unquoted = service.CleanSqlWord("camelCase", CurrentAutoCompletDatabaseMode.MakeUpperCase);

        Assert.Equal("CamelCase", quoted);
        Assert.Equal("CAMELCASE", unquoted);
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void ExtendedTemplateMethods_ShouldReturnNonEmptyText(Type pluginType)
    {
        var service = PluginTestDiscovery.CreateInstance(pluginType);

        var indexTemplate = service.GetCreateIndexPatternText("database", "public", "sample_table");
        var partitionTemplate = service.GetCreatePartitionPatternText("database", "public", "sample_table");
        var maintenancePack = service.GetPostgresMaintenanceCommandPack("database", "public", "sample_table");
        var overview = service.GetPostgresIndexPartitionOverview("database", "public", "sample_table");

        Assert.False(string.IsNullOrWhiteSpace(indexTemplate));
        Assert.False(string.IsNullOrWhiteSpace(partitionTemplate));
        Assert.False(string.IsNullOrWhiteSpace(maintenancePack));
        Assert.False(string.IsNullOrWhiteSpace(overview));
    }
}

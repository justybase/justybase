using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Tests;

public sealed class PluginConventionTests
{
    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteCoreDatabaseTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void CoreDatabaseDrivers_ShouldExposeDatabaseContract(Type databaseType)
    {
        var instance = PluginTestDiscovery.CreateInstance(databaseType);
        Assert.Equal(DatabaseTypeEnum.Sqlite, instance.DatabaseType);
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void ConcreteDatabasePlugins_ShouldExposeDatabaseType(Type pluginType)
    {
        var instance = PluginTestDiscovery.CreateInstance(pluginType);
        Assert.NotEqual(DatabaseTypeEnum.NotSupportedDatabase, instance.DatabaseType);
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void ConcreteDatabasePlugins_ShouldExposeExpectedConstructorSignature(Type pluginType)
    {
        var constructor = pluginType.GetConstructor([typeof(JustyBase.PluginCommon.Models.DbConnectionOptions)]);

        Assert.NotNull(constructor);
    }

}

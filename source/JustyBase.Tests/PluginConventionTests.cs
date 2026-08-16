using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace JustyBase.Tests;

public sealed class PluginConventionTests
{
    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteCoreDatabaseTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void CoreDatabaseDrivers_ShouldExposeDatabaseContract(Type databaseType)
    {
        var whoIAmConstField = databaseType.GetField(nameof(IDatabaseService.WHO_I_AM_CONST), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(whoIAmConstField);
        Assert.Equal(typeof(DatabaseTypeEnum), whoIAmConstField.FieldType);

        var instance = PluginTestDiscovery.CreateInstance(databaseType);
        Assert.Equal(DatabaseTypeEnum.Sqlite, instance.DatabaseType);
        Assert.Equal(DatabaseTypeEnum.Sqlite, PluginTestDiscovery.GetWhoIAmConstValue(whoIAmConstField));
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void ConcreteDatabasePlugins_ShouldExposeWhoIAmConst_AndAssignDatabaseType(Type pluginType)
    {
        var whoIAmConstField = pluginType.GetField(nameof(IDatabaseService.WHO_I_AM_CONST), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(whoIAmConstField);
        Assert.Equal(typeof(DatabaseTypeEnum), whoIAmConstField.FieldType);

        var expectedDatabaseType = PluginTestDiscovery.GetWhoIAmConstValue(whoIAmConstField);
        var instance = PluginTestDiscovery.CreateInstance(pluginType);

        Assert.Equal(expectedDatabaseType, instance.DatabaseType);
    }

    [Theory]
    [MemberData(nameof(PluginTestDiscovery.GetConcreteDatabasePluginTypeCases), MemberType = typeof(PluginTestDiscovery))]
    public void ConcreteDatabasePlugins_ShouldExposeExpectedConstructorSignature(Type pluginType)
    {
        var constructor = pluginType.GetConstructor([
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(int)
        ]);

        Assert.NotNull(constructor);
    }

    [Fact]
    public void InstallSpecificDatabaseService_ShouldRegisterConcreteDatabasePlugins()
    {
        var pluginAssemblies = PluginTestDiscovery.GetPluginAssembliesFromOutput();
        Assert.NotEmpty(pluginAssemblies);

        foreach (var pluginAssembly in pluginAssemblies)
        {
            PluginLoadHelper.InstallSpecificDatabaseService(pluginAssembly);
        }

        var registeredImplementations = PluginTestDiscovery.GetRegisteredImplementations();
        var expectedDatabaseTypes = new HashSet<DatabaseTypeEnum>();
        foreach (var pluginType in PluginTestDiscovery.GetConcreteDatabasePluginTypes(pluginAssemblies))
        {
            var whoIAmConstField = pluginType.GetField(nameof(IDatabaseService.WHO_I_AM_CONST), BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(whoIAmConstField);
            var expectedDatabaseType = PluginTestDiscovery.GetWhoIAmConstValue(whoIAmConstField);
            expectedDatabaseTypes.Add(expectedDatabaseType);

            Assert.True(
                registeredImplementations.ContainsKey(expectedDatabaseType),
                $"{pluginType.FullName} ({expectedDatabaseType}) was not registered by PluginLoadHelper.");
        }

        Assert.True(registeredImplementations.Count >= expectedDatabaseTypes.Count);
    }

    [Fact]
    public void InstallSpecificDatabaseService_ShouldBeAnnotatedForTrimAwareness()
    {
        var method = typeof(PluginLoadHelper).GetMethod(
            nameof(PluginLoadHelper.InstallSpecificDatabaseService),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
    }
}

using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using System.Reflection;

namespace JustyBase.Tests;

internal static class PluginTestDiscovery
{
    public static IEnumerable<object[]> GetConcreteDatabasePluginTypeCases()
    {
        foreach (var pluginType in GetConcreteDatabasePluginTypes())
        {
            yield return [pluginType];
        }
    }

    public static IEnumerable<object[]> GetConcreteCoreDatabaseTypeCases()
    {
        yield return [typeof(JustyBase.SqliteDriver.Sqlite)];
    }

    public static List<Assembly> GetPluginAssembliesFromOutput()
    {
        var pluginPaths = Directory.GetFiles(AppContext.BaseDirectory, "*Plugin.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return pluginPaths.Select(LoadAssembly).ToList();
    }

    public static List<Type> GetConcreteDatabasePluginTypes()
    {
        return GetConcreteDatabasePluginTypes(GetPluginAssembliesFromOutput());
    }

    public static List<Type> GetConcreteDatabasePluginTypes(IEnumerable<Assembly> pluginAssemblies)
    {
        return pluginAssemblies
            .SelectMany(GetLoadableTypes)
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IDatabaseService).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    public static IDatabaseService CreateInstance(Type pluginType)
    {
        var constructor = pluginType.GetConstructor([typeof(DbConnectionOptions)]);
        Assert.NotNull(constructor);

        var instance = constructor.Invoke([new DbConnectionOptions("user", "password", "5480", "127.0.0.1", "database", 1)]);
        return Assert.IsAssignableFrom<IDatabaseService>(instance);
    }

    private static Assembly LoadAssembly(string assemblyPath)
    {
        var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
        var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(x => AssemblyName.ReferenceMatchesDefinition(x.GetName(), assemblyName));

        return loadedAssembly ?? Assembly.Load(assemblyName);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
    }
}

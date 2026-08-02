using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
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
        var constructor = pluginType.GetConstructor([typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(int)]);
        Assert.NotNull(constructor);

        var instance = constructor.Invoke(new object[] { "user", "password", "5480", "127.0.0.1", "database", 1 });
        return Assert.IsAssignableFrom<IDatabaseService>(instance);
    }

    public static DatabaseTypeEnum GetWhoIAmConstValue(FieldInfo whoIAmConstField)
    {
        var value = whoIAmConstField.IsLiteral
            ? whoIAmConstField.GetRawConstantValue()
            : whoIAmConstField.GetValue(null);

        if (value is DatabaseTypeEnum databaseType)
        {
            return databaseType;
        }

        if (value is int enumInt)
        {
            return (DatabaseTypeEnum)enumInt;
        }

        throw new InvalidOperationException($"Field {whoIAmConstField.Name} is not a valid {nameof(DatabaseTypeEnum)} constant.");
    }

    public static Dictionary<DatabaseTypeEnum, Func<string, string, string, string, string, int, IDatabaseService>> GetRegisteredImplementations()
    {
        var field = typeof(DatabaseServiceRegistry).GetField("_implementations", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        var value = field.GetValue(DatabaseServiceRegistry.Shared);
        return Assert.IsType<Dictionary<DatabaseTypeEnum, Func<string, string, string, string, string, int, IDatabaseService>>>(value);
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

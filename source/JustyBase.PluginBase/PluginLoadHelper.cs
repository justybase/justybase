using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace JustyBase.PluginDatabaseBase;

public static class PluginLoadHelper
{
    [RequiresUnreferencedCode("Plugin discovery uses reflection to inspect and activate plugin types.")]
    public static void InstallSpecificDatabaseService(Assembly pluginAssembly)
    {
        foreach (Type type in pluginAssembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract || !typeof(IDatabaseService).IsAssignableFrom(type))
            {
                continue;
            }

            var whoIAmConstField = type.GetField(
                nameof(IDatabaseService.WHO_I_AM_CONST),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (whoIAmConstField is null || whoIAmConstField.FieldType != typeof(DatabaseTypeEnum))
            {
                continue;
            }

            var constructor = type.GetConstructor([
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(int)]);
            if (constructor is null)
            {
                continue;
            }

            var fieldValue = whoIAmConstField.IsLiteral
                ? whoIAmConstField.GetRawConstantValue()
                : whoIAmConstField.GetValue(null);
            DatabaseTypeEnum databaseType;
            if (fieldValue is DatabaseTypeEnum databaseTypeEnumValue)
            {
                databaseType = databaseTypeEnumValue;
            }
            else if (fieldValue is int databaseTypeIntValue && Enum.IsDefined(typeof(DatabaseTypeEnum), databaseTypeIntValue))
            {
                databaseType = (DatabaseTypeEnum)databaseTypeIntValue;
            }
            else
            {
                continue;
            }

            var activatorFunc = (string userName, string password, string port, string ip, string db, int connectionTimeout)
                => Activator.CreateInstance(type, userName, password, "5480", ip, db, connectionTimeout) as IDatabaseService;
            DatabaseServiceHelpers.AddDatabaseImplementation(databaseType, activatorFunc);
        }
    }

    public static Assembly LoadPlugin(string pluginLocation)
    {
        PluginLoadContext loadContext = new PluginLoadContext(pluginLocation);
        return loadContext.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(pluginLocation)));
    }

    private static readonly Lock _pluginLock = new Lock();

    [RequiresUnreferencedCode("Plugin discovery uses reflection to inspect and activate plugin types.")]
    public static void LoadPlugins(string pluginsLocation)
    {
        lock (_pluginLock)
        {
#if DEBUG
            string[] files = [
                //@$"{pluginsLocation}NetezzaDotnetPlugin\bin\Debug\
                //.0\NetezzaDotnetPlugin.dll",
                //@$"{pluginsLocation}OraclePlugin\bin\Debug\net10.0\OraclePlugin.dll",
                @$"{pluginsLocation}DB2Plugin\bin\Debug\net10.0\DB2Plugin.dll",
                @$"{pluginsLocation}PostgresPlugin\bin\Debug\net10.0\PostgresPlugin.dll",
                @$"{pluginsLocation}SqlitePlugin\bin\Debug\net10.0\SqlitePlugin.dll",
                @$"{pluginsLocation}DuckDBPlugin\bin\Debug\net10.0\DuckDBPlugin.dll",
                @$"{pluginsLocation}MySqlPlugin\bin\Debug\net10.0\MySqlPlugin.dll",
                ];
            foreach (var filePath in files)
            {
                var pluginAssembly = LoadPlugin(filePath);
                InstallSpecificDatabaseService(pluginAssembly);
            }
#else
            foreach (var dir in Directory.GetDirectories(pluginsLocation))
            {
                foreach (var file in Directory.GetFiles(dir, "*Plugin.dll"))
                {
                    var pluginAssembly = PluginLoadHelper.LoadPlugin(file);
                    PluginLoadHelper.InstallSpecificDatabaseService(pluginAssembly);
                }
            }
#endif
        }
    }

}

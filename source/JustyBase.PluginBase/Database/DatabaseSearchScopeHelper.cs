using System.Collections.Generic;

namespace JustyBase.PluginDatabaseBase.Database;

public static class DatabaseSearchScopeHelper
{
    public static string? ResolveDatabaseOrFirst(string? requestedDatabase, IEnumerable<string> availableDatabases)
    {
        if (requestedDatabase is not null)
        {
            return requestedDatabase;
        }

        foreach (var databaseName in availableDatabases)
        {
            return databaseName;
        }

        return null;
    }
}

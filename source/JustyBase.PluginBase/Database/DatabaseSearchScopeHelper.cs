namespace JustyBase.PluginDatabaseBase.Database;

public static class DatabaseSearchScopeHelper
{
    public static string? ResolveDatabaseOrFirst(string? requestedDatabase, IEnumerable<string> availableDatabases)
    {
        // A blank request means "unqualified": fall back to the first/active
        // database instead of filtering on an empty name that can never match.
        if (!string.IsNullOrWhiteSpace(requestedDatabase))
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

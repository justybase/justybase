using System.Collections.Generic;

namespace JustyBase.PluginDatabaseBase.Database;

/// <summary>
/// Resolves schema lists and cleans identifier names for database services.
/// Extracted from DatabaseService to keep schema-resolution logic testable.
/// </summary>
public static class DatabaseSchemaResolver
{
    /// <summary>
    /// Resolves the list of schemas to query based on the provided database and schema filters.
    /// When schema is empty, uses the default schema for the database (or all SYSTEM schemas).
    /// </summary>
    public static List<string> GetAvailableSchemas(
        string database,
        string? schema,
        Dictionary<string, string> databaseDefSchema,
        Dictionary<string, Dictionary<string, Dictionary<string, PluginCommon.Models.DatabaseObject>>> databaseSchemaTable)
    {
        List<string> schemas = [];

        if (string.IsNullOrWhiteSpace(schema))
        {
            if (database is not null && database != "SYSTEM")
            {
                if (databaseDefSchema.TryGetValue(database, out var schemaTmp) && schemaTmp is not null)
                {
                    schemas.Add(schemaTmp);
                }
            }
            else if (databaseSchemaTable.TryGetValue("SYSTEM", out var systemRes))
            {
                schemas.AddRange(systemRes.Keys);
            }
        }
        else
        {
            schemas.Add(schema);
        }

        return schemas;
    }

    /// <summary>
    /// Quotes each identifier component if needed, returning clean 3-part names.
    /// </summary>
    public static (string, string, string) GetCleanedNames(
        string database,
        string schema,
        string tableName,
        System.Func<string, string> quoteNameIfNeeded)
    {
        string cleanDatabase = database is not null ? quoteNameIfNeeded(database) : database!;
        string cleanSchema = schema is not null ? quoteNameIfNeeded(schema) : schema!;
        string cleanTable = tableName is not null ? quoteNameIfNeeded(tableName) : tableName!;
        return (cleanDatabase, cleanSchema, cleanTable);
    }

    /// <summary>
    /// Quotes each identifier component if needed, returning clean 2-part names.
    /// </summary>
    public static (string, string) GetCleanedNames(
        string schema,
        string tableName,
        System.Func<string, string> quoteNameIfNeeded)
    {
        string cleanSchema = schema is not null ? quoteNameIfNeeded(schema) : schema!;
        string cleanTable = tableName is not null ? quoteNameIfNeeded(tableName) : tableName!;
        return (cleanSchema, cleanTable);
    }
}

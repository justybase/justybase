using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using System.Text.RegularExpressions;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseSchemaQueryService
{
    Task CacheAllObjects(TypeInDatabaseEnum[] typeInDatabaseArr, string databaseName = "", string procedureName = "");
    IEnumerable<(DatabaseObject dbObject, string schema)> FindDbObject(string database, string schema, string name, bool cleanNames);
    IEnumerable<DatabaseColumn> GetColumns(string? database, string? schema, string? table, string filter);
    IEnumerable<(DatabaseColumn, DatabaseObject)> GetColumnsFromAllTablesAndSchemas(string database, string schema);
    IEnumerable<string> GetDatabases(string filter);
    IEnumerable<DatabaseObject> GetDbObjects(string database, string schema, string filter, TypeInDatabaseEnum typeInDatabase);
    /// <summary>
    /// Database-level tablespaces (Db2 SYSCAT.TABLESPACES). Engines without a
    /// tablespace concept return an empty sequence.
    /// </summary>
    IEnumerable<DatabaseObject> GetTablespaces(string database);
    ValueTask<List<ProcedureCachedInfo>> GetProceduresSignaturesFromName(string database, string schema, string procName);
    IEnumerable<string> GetSchemas(string database, string filter);
    /// <summary>
    /// Cached definition text of a procedure, view or external table (the same text
    /// <see cref="IsItemSourceContains"/> searches). Returns null when the object type
    /// carries no definition or the cache has not been loaded.
    /// </summary>
    string? GetItemSource(TypeInDatabaseEnum typeInDatabase, string database, string schema, string itemNameOrSignature, int procedureId);
    bool IsItemSourceContains(TypeInDatabaseEnum typeInDatabase, string database, string schema, string itemNameOrSignature, int procedureId, StringComparison comp, string searchWord, Regex rx);
    bool IsTypeInDatabaseSupported(TypeInDatabaseEnum tpe);
    void CacheMainDictionary();
    void ClearCachedData();
}

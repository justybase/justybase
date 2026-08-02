using JustyBase.NetezzaDdl;
using JustyBase.NetezzaDdl.Models;
using JustyBase.Netezza.Ddl;
using JustyBase.Netezza.Models;

namespace NetezzaBase;

internal static class NetezzaDdlInputMapper
{
    public static NetezzaTableDdlInput BuildTableInput(
        NetezzaCommonClass service,
        string database,
        string schema,
        string tableName,
        string? overrideTableName = null,
        string? middleCode = null,
        string? endingCode = null,
        string? tableComment = null,
        string? tableOwner = null)
    {
        List<string>? distributeColumns = null;
        if (service.DistributionDictionary.TryGetValue(database, out var distBySchema)
            && distBySchema.TryGetValue(schema, out var distByTable)
            && distByTable.TryGetValue(tableName, out var distList))
        {
            distributeColumns = distList;
        }

        List<string>? organizeColumns = null;
        if (service.OrganizeDictionary.TryGetValue(database, out var orgBySchema)
            && orgBySchema.TryGetValue(schema, out var orgByTable)
            && orgByTable.TryGetValue(tableName, out var orgList))
        {
            organizeColumns = orgList;
        }

        List<NetezzaKeyDdl>? keys = null;
        if (service.KeysDictionary.TryGetValue(database, out var keysBySchema)
            && keysBySchema.TryGetValue(schema, out var keysByTable)
            && keysByTable.TryGetValue(tableName, out var keyItems))
        {
            keys = ToKeys(keyItems);
        }

        var table = new NetezzaSchemaTable(
            tableName,
            schema,
            database,
            Columns: service.GetColumns(database, schema, tableName, "")
                .Select(c => new NetezzaSchemaColumn(c.Name, c.FullTypeName, !c.ColumnNotNull, c.Desc, c.COLDEFAULT))
                .ToArray(),
            Description: tableComment);

        return NetezzaDdlInputFactory.BuildTable(
            table,
            distributeColumns,
            organizeColumns,
            keys,
            overrideTableName,
            middleCode,
            endingCode,
            tableOwner);
    }

    public static NetezzaExternalDdlInput BuildExternalInput(
        NetezzaCommonClass service,
        string database,
        string schema,
        string tableName,
        NetezzaExternalTableCachedInfo? cached)
    {
        var table = new NetezzaSchemaTable(
            tableName,
            schema,
            database,
            Columns: service.GetColumns(database, schema, tableName, "")
                .Select(c => new NetezzaSchemaColumn(c.Name, c.FullTypeName, !c.ColumnNotNull, c.Desc, c.COLDEFAULT))
                .ToArray());

        return NetezzaDdlInputFactory.BuildExternal(
            table,
            cached is null ? new NetezzaExternalTableOptions() : NetezzaExternalOptionsMapper.ToOptions(cached));
    }

    public static NetezzaViewDdlInput BuildViewInput(
        string database,
        string schema,
        string viewName,
        string viewDefinition,
        string? viewComment)
        => new(database, schema, viewName, viewDefinition, viewComment);

    private static List<NetezzaKeyDdl> ToKeys(Dictionary<string, JustyBase.PluginDatabaseBase.Models.NetezzaKeyItem> keyItems)
    {
        var keys = new List<NetezzaKeyDdl>();
        foreach (var (keyName, keyInfo) in keyItems)
        {
            var columnNames = keyInfo.ColumnList.Select(c => c.colName).ToList();
            if (keyInfo.KeyType == 'f')
            {
                keys.Add(new NetezzaKeyDdl(
                    keyInfo.KeyType,
                    keyName,
                    columnNames,
                    keyInfo.PKDATABASE,
                    keyInfo.PKSCHEMA,
                    keyInfo.PKRELATION,
                    keyInfo.ColumnList.Select(c => c.referencedPkColName).ToList(),
                    keyInfo.DEL_TYPE,
                    keyInfo.UPDT_TYPE));
            }
            else
            {
                keys.Add(new NetezzaKeyDdl(keyInfo.KeyType, keyName, columnNames));
            }
        }

        return keys;
    }
}

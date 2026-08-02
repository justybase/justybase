using JustyBase.NetezzaDdl;
using JustyBase.PluginCommon.Models;

namespace NetezzaBase;

public class NetezzaDdlBuilder
{
    private readonly Func<string, string?, string?, string> _getQuotedName;
    private readonly Func<string, string, string, (string, string, string)> _getCleanedNames;
    private readonly Func<string?, string?, string?, string, IEnumerable<DatabaseColumn>> _getColumns;
    private readonly Func<string, string> _quoteNameIfNeeded;

    public NetezzaDdlBuilder(
        Func<string, string?, string?, string> getQuotedName,
        Func<string, string, string, (string, string, string)> getCleanedNames,
        Func<string?, string?, string?, string, IEnumerable<DatabaseColumn>> getColumns,
        Func<string, string> quoteNameIfNeeded)
    {
        _getQuotedName = getQuotedName;
        _getCleanedNames = getCleanedNames;
        _getColumns = getColumns;
        _quoteNameIfNeeded = quoteNameIfNeeded;
    }

    public string GetDeleted(string table, string database, string schema)
    {
        var cols = _getColumns(database, schema, table, "");
        var tableCl = _getQuotedName(database, schema, table);
        return NetezzaDdlTemplates.GetDeletedRecordsSql(tableCl, cols.Select(c => c.Name).ToList(), _quoteNameIfNeeded);
    }

    public string GetGrant(string database, string schema, string table)
        => NetezzaDdlTemplates.GetGrantSelectSql(_getQuotedName(database, schema, table));

    public string GetOrganize(string database, string schema, string table)
        => NetezzaDdlTemplates.GetOrganizeTemplateSql(_getQuotedName(database, schema, table));

    public string GetGroom(string database, string schema, string table)
        => NetezzaDdlTemplates.GetGroomSql(_getQuotedName(database, schema, table));

    public string GetGenerateStats(string database, string schema, string table)
        => NetezzaDdlTemplates.GetGenerateStatsSql(_getQuotedName(database, schema, table));

    public string GetAddComment(string table, string database, string schema)
        => NetezzaDdlTemplates.GetAddTableCommentTemplateSql(_getQuotedName(database, schema, table));

    public string GetCheckDistributeText(string database, string schema, string tableName)
    {
        var (cleanDatabaseName, cleanSchema, cleanTableName) = _getCleanedNames(database, schema, tableName);
        return NetezzaDdlTemplates.GetCheckDistributeSql(cleanDatabaseName, cleanSchema, cleanTableName, tableName.ToUpper());
    }

    public string GetCreateProcedurePatternText()
        => NetezzaDdlTemplates.CreateProcedurePattern;

    public string GetCreateFluidSample(string database, string schema, string tableName)
    {
        int i1 = tableName.IndexOf('(');
        tableName = tableName[..i1];
        return NetezzaDdlTemplates.GetCreateFluidSampleSql(_getQuotedName(database, schema, tableName));
    }
}

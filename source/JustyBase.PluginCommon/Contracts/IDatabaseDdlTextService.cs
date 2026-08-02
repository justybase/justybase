using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using System.Text;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseDdlTextService
{
    string GetAddComment(string table, string database, string schema);
    string GetCheckDistributeText(string database, string schema, string tableName);
    ValueTask<string> GetCreateExternalText(string database, string schema, string tableName);
    ValueTask GetCreateExternalTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName);
    string GetCreateFromCode(string fullName);
    string GetCreateProcedureCall(string database, string schema, string tableName);
    string GetCreateProcedurePatternText();
    string GetCreateIndexPatternText(string database, string schema, string tableName);
    string GetCreatePartitionPatternText(string database, string schema, string tableName);
    ValueTask<string> GetCreateProcedureText(string database, string schema, string procedureName, bool forceFreshCode = false);
    ValueTask GetCreateProcedureTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName, bool forceFreshCode = false);
    ValueTask<string> GetCreateIndexText(string database, string schema, string indexName);
    ValueTask GetCreateIndexTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string indexName);
    ValueTask<string> GetCreatePartitionText(string database, string schema, string partitionName);
    ValueTask GetCreatePartitionTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string partitionName);
    string GetCreateSequencePatternText();
    string GetCreateSynonymPatternText();
    ValueTask<string> GetCreateSynonymText(string database, string schema, string synonymName);
    ValueTask GetCreateSynonymTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string synonymName);
    ValueTask<string> GetCreateTableText(string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null);
    ValueTask GetCreateTableTextStringBuilder(StringBuilder sb, string database, string schema, string tableName, string? overrideTableName = null, string? middleCode = null, string? endingCode = null, List<string>? distOverride = null);
    ValueTask<string> GetCreateViewText(string database, string schema, string tableName);
    ValueTask GetCreateViewTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName);
    string GetDeleted(string table, string database, string schema);
    string GetDrop(string table, string database, string schema);
    string GetDuplicates(string table, string database, string schema);
    string GetEmpty(string table, string database, string schema);
    string GetCountRows(string table, string database, string schema);
    string GetExport(string table, string database, string schema);
    string GetGenerateStats(string database, string schema, string table);
    string GetGrant(string database, string schema, string table);
    string GetGroom(string database, string schema, string table);
    string GetImport(string table, string database, string schema);
    string GetKeyCodeText(string database, string schema, string tableName);
    string GetKeyUniqueCodeText(string database, string schema, string tableName);
    string GetOrganize(string database, string schema, string table);
    ValueTask<string> GetReCreateTableText(string database, string schema, string tableName);
    ValueTask GetReCreateTableTextStringBuilder(StringBuilder stringBuilder, string database, string schema, string tableName);
    string GetShortSelectCode(string fullName);
    string GetTableDropCode(string fullName);
    string GetTableRenameCode(string fullName);
    string GetTop100Select(string database, string schema, string table, bool snippetMode, bool addWhereToTextCols = false);
    string GetTop100SelectNumberFromTables(string database, string schema, IEnumerable<DatabaseObject> tables);
    string GetTop100SelectTextFromTables(string database, string schema, IEnumerable<DatabaseObject> tables);
    string GetPostgresIndexPartitionOverview(string database, string schema, string tableName);
    string GetPostgresMaintenanceCommandPack(string database, string schema, string tableName);
    (int position, int length) HandleExceptions(ReadOnlySpan<char> sqlText, Exception exception);
    string QuoteNameIfNeeded(string word);
    string CleanSqlWord(string? word, CurrentAutoCompletDatabaseMode autoCompletMode);
}

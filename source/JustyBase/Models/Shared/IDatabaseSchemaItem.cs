using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using System.Text;

namespace JustyBase.Services.Database;

internal interface IDatabaseSchemaItem
{
    string Database { get; set; }
    string CurrentSchema { get; set; }
    string Name { get; set; }
    TypeInDatabaseEnum ActualTypeInDatabase { get; set; }

    internal static void InsertDoubleClicked(IDatabaseSchemaItem schemaModel)
    {
        var editor = SqlCodeEditorHelpers.LastFocusedEditor;
        if (editor is null)
        {
            return;
        }
        string textToInsert = schemaModel.Name;
        if (schemaModel.ActualTypeInDatabase == TypeInDatabaseEnum.Table)
        {
            textToInsert = $"{schemaModel.Database}.{schemaModel.CurrentSchema}.{schemaModel.Name}";
        }
        else if (schemaModel.ActualTypeInDatabase == TypeInDatabaseEnum.View
            || schemaModel.ActualTypeInDatabase == TypeInDatabaseEnum.Partition)
        {
            textToInsert = $"{schemaModel.Database}.{schemaModel.CurrentSchema}.{schemaModel.Name}";
        }

        editor.Document.Insert(editor.TextArea.Caret.Offset, textToInsert);
        editor.Focus();
    }

    internal static async Task<string> GetCode(IDatabaseSchemaItem schemaModel, string CONNECTION_NAME, string optionName, IGeneralApplicationData generalApplicationData, ISimpleLogger simpleLogger)
    {
        string DATABASE = schemaModel.Database;
        string SCHEMA = schemaModel.CurrentSchema;
        string ITEM_NAME = schemaModel.Name;
        IDatabaseService dbService = await Task.Run(() =>
            DatabaseServiceHelpers.GetDatabaseService(generalApplicationData, CONNECTION_NAME));
        if (dbService is null)
        {
            return string.Empty;
        }
        string sql = "";
        if (optionName.StartsWith("DDL_TABLE", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreateTableText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("RECREATE_TABLE", StringComparison.Ordinal))
        {
            sql = await dbService.GetReCreateTableText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("RECREATE_ALL_TABLES", StringComparison.Ordinal))
        {
            HashSet<string> words = null;
            try
            {
                if (SqlCodeEditorHelpers.LastFocusedEditor is not null && SqlCodeEditorHelpers.LastFocusedEditor.Text.StartsWith("RECREATE_HACK", StringComparison.Ordinal))
                {
                    words = new HashSet<string>(SqlCodeEditorHelpers.LastFocusedEditor.Text.Split("\n").Select(o => o.Trim()));
                }
            }
            catch (Exception ex)
            {
                simpleLogger.TrackError(ex, isCrash: false);
            }
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Table);
            StringBuilder stringBuilder = new();
            foreach (var item in objects)
            {
                if (words is null || words.Contains(item.Name))
                {
                    await dbService.GetReCreateTableTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
                }
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("DDL_ALL_TABLES", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Table);
            StringBuilder stringBuilder = new StringBuilder();
            foreach (var item in objects)
            {
                await dbService.GetCreateTableTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("SELECT_ALL_SEARCH_TEXT", StringComparison.Ordinal))
        {
            IEnumerable<DatabaseObject> objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Table);
            sql = dbService.GetTop100SelectTextFromTables(DATABASE, SCHEMA, objects);
        }
        else if (optionName.StartsWith("SELECT_ALL_SEARCH_NUMBER", StringComparison.Ordinal))
        {
            IEnumerable<DatabaseObject> objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Table);
            sql = dbService.GetTop100SelectNumberFromTables(DATABASE, SCHEMA, objects);
        }
        else if (optionName.StartsWith("SELECT_SEARCH", StringComparison.Ordinal))
        {
            sql = dbService.GetTop100Select(DATABASE, SCHEMA, ITEM_NAME, snippetMode: false /*!!*/, addWhereToTextCols: true);
        }
        else if (optionName.StartsWith("SELECT", StringComparison.Ordinal))
        {
            if (optionName.EndsWith("CLIP", StringComparison.Ordinal))
            {
                sql = dbService.GetTop100Select(DATABASE, SCHEMA, ITEM_NAME, snippetMode: false);
            }
            else
            {
                sql = dbService.GetTop100Select(DATABASE, SCHEMA, ITEM_NAME, snippetMode: true);
            }
        }
        else if (optionName.StartsWith("DUPLICATES", StringComparison.Ordinal))
        {
            sql = dbService.GetDuplicates(ITEM_NAME, DATABASE, SCHEMA);
        }
        else if (optionName.StartsWith("DELETED", StringComparison.Ordinal))
        {
            sql = dbService.GetDeleted(ITEM_NAME, DATABASE, SCHEMA);
        }
        else if (optionName.StartsWith("GRANT", StringComparison.Ordinal))
        {
            sql = dbService.GetGrant(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("ORGANIZE", StringComparison.Ordinal))
        {
            sql = dbService.GetOrganize(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DISTRIBUTE", StringComparison.Ordinal))
        {
            sql = dbService.GetCheckDistributeText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DDL_VIEW", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreateViewText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DDL_INDEX", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreateIndexText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DDL_ALL_INDEXES", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Index);
            StringBuilder stringBuilder = new();
            foreach (var item in objects)
            {
                await dbService.GetCreateIndexTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("DDL_PARTITION", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreatePartitionText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DDL_ALL_PARTITIONS", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Partition);
            StringBuilder stringBuilder = new();
            foreach (var item in objects)
            {
                await dbService.GetCreatePartitionTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("DDL_ALL_VIEWS", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.View);
            StringBuilder stringBuilder = new StringBuilder();
            foreach (var item in objects)
            {
                await dbService.GetCreateViewTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("SELECT_VIEW", StringComparison.Ordinal))
        {
            sql = dbService.GetTop100Select(DATABASE, SCHEMA, ITEM_NAME, snippetMode: true);
        }
        else if (optionName.StartsWith("DDL_PROCEDURE", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreateProcedureText(DATABASE, SCHEMA, ITEM_NAME, forceFreshCode: true);
        }
        else if (optionName.StartsWith("DDL_ALL_PROCEDURES", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Procedure);
            StringBuilder stringBuilder = new();
            foreach (var item in objects)
            {
                await dbService.GetCreateProcedureTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("CALL_PROCEDURE", StringComparison.Ordinal))
        {
            sql = dbService.GetCreateProcedureCall(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("CREATE_PROCEDURE", StringComparison.Ordinal))
        {
            sql = dbService.GetCreateProcedurePatternText();
        }
        else if (optionName.StartsWith("FLUID_SAMPLE", StringComparison.Ordinal) && dbService is INetezza netezza)
        {
            sql = netezza.GetCreateFluidSample(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("KEY", StringComparison.Ordinal))
        {
            sql = dbService.GetKeyCodeText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("UNIQUE", StringComparison.Ordinal))
        {
            sql = dbService.GetKeyUniqueCodeText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DDL_EXTERNAL", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreateExternalText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("DDL_ALL_EXTERNALS", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.ExternalTable);
            StringBuilder stringBuilder = new();
            foreach (var item in objects)
            {
                await dbService.GetCreateExternalTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("DDL_SYNONYM", StringComparison.Ordinal))
        {
            sql = await dbService.GetCreateSynonymText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("COPY_TEXT", StringComparison.Ordinal))
        {
            sql = ITEM_NAME;
        }
        else if (optionName.StartsWith("DDL_ALL_SYNONYMS", StringComparison.Ordinal))
        {
            var objects = dbService.GetDbObjects(DATABASE, SCHEMA, "", TypeInDatabaseEnum.Synonym);
            StringBuilder stringBuilder = new();
            foreach (var item in objects)
            {
                await dbService.GetCreateSynonymTextStringBuilder(stringBuilder, DATABASE, SCHEMA, item.Name);
            }
            sql = stringBuilder.ToString();
        }
        else if (optionName.StartsWith("CREATE_SYNONYM", StringComparison.Ordinal))
        {
            sql = dbService.GetCreateSynonymPatternText();
        }
        else if (optionName.StartsWith("CREATE_SEQUENCE", StringComparison.Ordinal))
        {
            sql = dbService.GetCreateSequencePatternText();
        }
        else if (optionName.StartsWith("CREATE_INDEX", StringComparison.Ordinal))
        {
            sql = dbService.GetCreateIndexPatternText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("CREATE_PARTITION", StringComparison.Ordinal))
        {
            sql = dbService.GetCreatePartitionPatternText(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("POSTGRES_INDEX_PARTITION_OVERVIEW", StringComparison.Ordinal))
        {
            sql = dbService.GetPostgresIndexPartitionOverview(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("POSTGRES_MAINTENANCE", StringComparison.Ordinal))
        {
            sql = dbService.GetPostgresMaintenanceCommandPack(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("GROOM", StringComparison.Ordinal))
        {
            sql = dbService.GetGroom(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("STATS", StringComparison.Ordinal))
        {
            sql = dbService.GetGenerateStats(DATABASE, SCHEMA, ITEM_NAME);
        }
        else if (optionName.StartsWith("COMMENT", StringComparison.Ordinal))
        {
            sql = dbService.GetAddComment(ITEM_NAME, DATABASE, SCHEMA);
        }
        else if (optionName.StartsWith("DROP", StringComparison.Ordinal))
        {
            sql = dbService.GetDrop(ITEM_NAME, DATABASE, SCHEMA);
        }
        else if (optionName.StartsWith("EMPTY", StringComparison.Ordinal))
        {
            sql = dbService.GetEmpty(ITEM_NAME, DATABASE, SCHEMA);
        }
        else if (optionName.StartsWith("COUNT_ROWS", StringComparison.Ordinal))
        {
            sql = dbService.GetCountRows(ITEM_NAME, DATABASE, SCHEMA);
        }
        else if (optionName.StartsWith("EXPORT_DATA", StringComparison.Ordinal))
        {
            sql = dbService.GetExport(ITEM_NAME, DATABASE, SCHEMA);
        }
        // IMPORT_DATA opens ImportView via DbSchemaViewModel / IActiveDocumentManager.OpenImportDocument

        return sql;

    }

}

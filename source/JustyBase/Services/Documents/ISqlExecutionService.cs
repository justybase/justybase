using System;
using System.Threading.Tasks;
using JustyBase.Helpers.Shared;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public interface ISqlExecutionService
{
    Task ExecuteSqlAsync(
        ISqlExecutionBridge bridge,
        SqlDocumentViewModelHelper.SqlExecutionPlan executionPlan,
        int globalQueryNumber,
        int globalAbortUBound,
        string localTitle,
        string option,
        string query,
        bool localDoPooling,
        bool keepConnectionOpenLocal,
        string filePathToExport,
        IDatabaseService actualDatabaseService,
        string selectedConnectionName,
        string selectedDatabase,
        Action<string> updateSelectedDatabase,
        int currentSqlPositionInEditor
    );
}

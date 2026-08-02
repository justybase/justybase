using System.Text.RegularExpressions;
using JustyBase.Common.Contracts;
using JustyBase.Helpers.Shared;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public enum SqlRunStartValidationStatus
{
    Ready,
    MissingEditor,
    MissingConnection
}

public sealed record SqlRunStartValidationResult(
    SqlRunStartValidationStatus Status,
    string? MessageForUser)
{
    public bool CanRun => Status == SqlRunStartValidationStatus.Ready;
}

public sealed record SqlRunExecutionSettings(
    string LocalTitle,
    bool KeepConnectionOpen,
    bool DoPooling,
    bool RequiresExportPathSelection)
{
    public bool ShouldDisableRun => KeepConnectionOpen;
}

public sealed record SqlRunQueryPreparationResult(
    bool HasQuery,
    string Query,
    int CurrentSqlPositionInEditor,
    Match VariableDefineMatch,
    SqlDocumentViewModelHelper.SqlExecutionPlan? ExecutionPlan)
{
    public bool HasSessionVariableDefinition => VariableDefineMatch.Success;
}

public interface ISqlRunPreparationService
{
    SqlRunStartValidationResult ValidateRunStart(bool hasSqlEditor, int selectedConnectionIndex);
    SqlRunExecutionSettings CreateExecutionSettings(bool keepConnectionOpen, bool doPooling, string localTitle, string? option);
    SqlRunQueryPreparationResult PrepareQuery(string query, int currentSqlPositionInEditor, string? option, bool singleCommand, bool continueOnError);
    Task<IDatabaseService?> InitializeDatabaseServiceAsync(string selectedConnectionName, Func<Task> loadPluginsIfNeededAsync);
}

public sealed class SqlRunPreparationService : ISqlRunPreparationService
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    public SqlRunPreparationService(
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _databaseServiceResolver = databaseServiceResolver;
    }

    public SqlRunStartValidationResult ValidateRunStart(bool hasSqlEditor, int selectedConnectionIndex)
    {
        if (!hasSqlEditor)
        {
            return new(SqlRunStartValidationStatus.MissingEditor, null);
        }

        if (selectedConnectionIndex == -1)
        {
            return new(SqlRunStartValidationStatus.MissingConnection, "please select connection");
        }

        return new(SqlRunStartValidationStatus.Ready, null);
    }

    public SqlRunExecutionSettings CreateExecutionSettings(bool keepConnectionOpen, bool doPooling, string localTitle, string? option)
    {
        return new(
            localTitle,
            keepConnectionOpen,
            doPooling,
            SqlDocumentViewModelHelper.RequiresExportPathSelection(option));
    }

    public SqlRunQueryPreparationResult PrepareQuery(string query, int currentSqlPositionInEditor, string? option, bool singleCommand, bool continueOnError)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 7)
        {
            return new(false, query, currentSqlPositionInEditor, Match.Empty, null);
        }

        Match variableDefineMatch = SqlDocumentViewModelHelper.RxSessionVariableDefine.Match(query);
        var executionPlan = SqlDocumentViewModelHelper.BuildExecutionPlan(singleCommand, option, query, continueOnError);

        return new(true, query, currentSqlPositionInEditor, variableDefineMatch, executionPlan);
    }

    public async Task<IDatabaseService?> InitializeDatabaseServiceAsync(string selectedConnectionName, Func<Task> loadPluginsIfNeededAsync)
    {
        if (!_databaseServiceResolver.IsDriverRegistered(_generalApplicationData, selectedConnectionName))
        {
            await loadPluginsIfNeededAsync();
        }

        return await Task.Run(() => _databaseServiceResolver.GetDatabaseService(_generalApplicationData, selectedConnectionName, delayCache: false));
    }
}

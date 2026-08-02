using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Models;
using JustyBase.Editor;
using JustyBase.Helpers;
using JustyBase.Models.Tools;
using JustyBase.Services.Documents;

namespace JustyBase.ViewModels.Documents;

public sealed partial class SqlDocumentViewModel
{
    public string HowManyRunningMessage => $"{HowManyRunning} running";

    [ObservableProperty]
    public partial bool IsRunEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HowManyRunningMessage))]
    public partial int HowManyRunning { get; set; }

    public bool IsStopEnabled => TasksToAbort > 0;

    public int TasksToAbort => _executionServices.ExecutionStateService.ActiveTasksCount;

    private void ReturnPhase()
    {
        var returnPhasePlan = _executionServices.RunLifecycleService.CreateReturnPhasePlan(
            IsRunEnabled,
            ActiveDocumentManager.IsActiveDockable(this));

        if (returnPhasePlan.ShouldEnableRun)
        {
            IsRunEnabled = true;
        }

        if (returnPhasePlan.ShouldMarkRecentlyFinished)
        {
            IsRecentlyFinished = true;
        }
    }

    private void ShowProgress(long x, long y)
    {
        ProgressValue = (int)(100 * x / y);
    }

    [ObservableProperty]
    public partial int ProgressValue { get; set; } = 0;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunSqlAsync(string option)
    {
        var sqlEditor = SqlEditor;
        var startValidation = _executionServices.RunPreparationService.ValidateRunStart(sqlEditor is not null, SelectedConnectionIndex);
        if (!startValidation.CanRun)
        {
            if (!string.IsNullOrWhiteSpace(startValidation.MessageForUser))
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance(startValidation.MessageForUser);
            }
            ReturnPhase();
            return;
        }

        var executionSettings = _executionServices.RunPreparationService.CreateExecutionSettings(
            KeepConnectionOpen,
            DoPooling,
            GetTile(),
            option);

        if (executionSettings.ShouldDisableRun) // only one SQL at same time
        {
            IsRunEnabled = false;
        }

        LogMessage? currentLogMessageStart = AddLogMessage($"Started with: {option}", LogMessageType.ok, DateTime.Now, executionSettings.LocalTitle);
        string filePathToExport = string.Empty;
        if (executionSettings.RequiresExportPathSelection)
        {
            string? chosenExportPath = await ChoseExportPath(option);
            if (chosenExportPath is null)
            {
                ReturnPhase();
                return;
            }

            filePathToExport = chosenExportPath;
        }

        string selectedQuery = sqlEditor!.SelectQueryPhase(out int currentSqlPosiotionInEditor);
        var queryPreparation = _executionServices.RunPreparationService.PrepareQuery(
            selectedQuery,
            currentSqlPosiotionInEditor,
            option,
            SingleCommand,
            ContinueOnError);

        if (!queryPreparation.HasQuery)
        {
            ReturnPhase();
            return;
        }

        if (queryPreparation.HasSessionVariableDefinition)
        {
            await _sqlVariableProcessor.AddSessionVariableAsync(queryPreparation.VariableDefineMatch, null, executionSettings.LocalTitle, _databaseService, SelectedConnectionName);
            ReturnPhase();
            return;
        }

        var executionPlan = queryPreparation.ExecutionPlan;
        if (executionPlan is null)
        {
            ReturnPhase();
            return;
        }

        if (executionPlan.ContinueOnError != ContinueOnError)
        {
            ContinueOnError = executionPlan.ContinueOnError;
        }

        if (sqlEditor.ErrorWaningsPahse1())
        {
            return;
        }

        var askRes = await _sqlVariableProcessor.AskAndReplaceVariablesFromUserAsync(queryPreparation.Query);
        if (askRes.IsCancel)
        {
            ReturnPhase();
            return;
        }
        string query = askRes.Query;

        int actualqlobalQueryNum = _executionServices.ExecutionStateService.RegisterNewQuery();
        LogMessage? currentLogMessage = null;
        try
        {
            currentLogMessage = StartRunLifecycle(executionSettings.LocalTitle);
            await _executionServices.RunOrchestrationService.ExecuteAsync(
                new SqlRunOrchestrationRequest(
                    this.Id,
                    _resultManager,
                    this,
                    executionSettings,
                    executionPlan,
                    actualqlobalQueryNum,
                    option,
                    query,
                    filePathToExport,
                    SelectedConnectionName,
                    SelectedDatabase,
                    queryPreparation.CurrentSqlPositionInEditor,
                    DatabasesList.ToList(),
                    databaseName =>
                    {
                        lock (DatabasesList)
                        {
                            DatabasesList.Add(databaseName);
                        }
                    },
                    newDatabase => SelectedDatabase = newDatabase,
                    currentLogMessage),
                () => _generalApplicationData.LoadPluginsIfNeeded(PluginsDownloadInfo));
        }
        finally
        {
            CompleteRunLifecycle(actualqlobalQueryNum, currentLogMessage);
        }

        ReturnPhase();
    }

    private LogMessage StartRunLifecycle(string localTitle)
    {
        var startPlan = _executionServices.RunLifecycleService.CreateStartPlan(HowManyRunning);
        HowManyRunning = startPlan.UpdatedRunningCount;

        if (startPlan.ShouldNotifyTasksToAbort)
        {
            OnPropertyChanged(nameof(TasksToAbort));
        }

        if (startPlan.ShouldNotifyIsStopEnabled)
        {
            OnPropertyChanged(nameof(IsStopEnabled));
        }

        var logMessage = AddLogMessage(
            startPlan.LogMessage,
            startPlan.LogMessageType,
            DateTime.Now,
            localTitle);
        logMessage.AddInnerMessageInUiThread(startPlan.InnerLogMessage, DateTime.Now);
        return logMessage;
    }

    private void CompleteRunLifecycle(int actualQueryNum, LogMessage? currentLogMessage)
    {
        _executionServices.ExecutionStateService.MarkFullFinish(actualQueryNum);

        var completionPlan = _executionServices.RunLifecycleService.CreateCompletionPlan(
            HowManyRunning,
            IsRunEnabled,
            currentLogMessage?.MessageType,
            DateTime.Now);

        if (completionPlan.ShouldNotifyTasksToAbort)
        {
            OnPropertyChanged(nameof(TasksToAbort));
        }

        HowManyRunning = completionPlan.UpdatedRunningCount;

        if (currentLogMessage is not null)
        {
            currentLogMessage.AddInnerMessageInUiThread(completionPlan.InnerLogMessage, DateTime.Now);
            if (completionPlan.ShouldSetLogMessageTypeToOk)
            {
                currentLogMessage.MessageType = LogMessageType.ok;
            }

            currentLogMessage.Message = completionPlan.FinalLogMessage;
        }

        if (completionPlan.ShouldEnableRun)
        {
            IsRunEnabled = true;
        }
    }
}

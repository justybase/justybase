using JustyBase.Common.Models;

namespace JustyBase.Services.Documents;

public sealed record SqlRunStartPlan(
    int UpdatedRunningCount,
    bool ShouldNotifyTasksToAbort,
    bool ShouldNotifyIsStopEnabled,
    string LogMessage,
    LogMessageType LogMessageType,
    string InnerLogMessage);

public sealed record SqlRunMissingConnectionPlan(string InnerLogMessage);

public sealed record SqlRunCompletionPlan(
    int UpdatedRunningCount,
    bool ShouldNotifyTasksToAbort,
    bool ShouldEnableRun,
    bool ShouldSetLogMessageTypeToOk,
    string InnerLogMessage,
    string FinalLogMessage);

public sealed record SqlReturnPhasePlan(
    bool ShouldEnableRun,
    bool ShouldMarkRecentlyFinished);

public interface ISqlRunLifecycleService
{
    SqlRunStartPlan CreateStartPlan(int currentRunningCount);
    SqlRunMissingConnectionPlan CreateMissingConnectionPlan();
    SqlRunCompletionPlan CreateCompletionPlan(int currentRunningCount, bool isRunEnabled, LogMessageType? currentLogMessageType, DateTime finishedAt);
    SqlReturnPhasePlan CreateReturnPhasePlan(bool isRunEnabled, bool? isActiveDockable);
}

public sealed class SqlRunLifecycleService : ISqlRunLifecycleService
{
    public SqlRunStartPlan CreateStartPlan(int currentRunningCount)
    {
        return new(
            currentRunningCount + 1,
            ShouldNotifyTasksToAbort: true,
            ShouldNotifyIsStopEnabled: true,
            LogMessage: "Running",
            LogMessageType: LogMessageType.inProgress,
            InnerLogMessage: "Started");
    }

    public SqlRunMissingConnectionPlan CreateMissingConnectionPlan()
    {
        return new("cannot establish connection");
    }

    public SqlRunCompletionPlan CreateCompletionPlan(int currentRunningCount, bool isRunEnabled, LogMessageType? currentLogMessageType, DateTime finishedAt)
    {
        return new(
            UpdatedRunningCount: Math.Max(0, currentRunningCount - 1),
            ShouldNotifyTasksToAbort: true,
            ShouldEnableRun: !isRunEnabled,
            ShouldSetLogMessageTypeToOk: currentLogMessageType is not null && currentLogMessageType != LogMessageType.error,
            InnerLogMessage: "Finished",
            FinalLogMessage: $"Finished {finishedAt}");
    }

    public SqlReturnPhasePlan CreateReturnPhasePlan(bool isRunEnabled, bool? isActiveDockable)
    {
        return new(
            ShouldEnableRun: !isRunEnabled,
            ShouldMarkRecentlyFinished: isActiveDockable == false);
    }
}

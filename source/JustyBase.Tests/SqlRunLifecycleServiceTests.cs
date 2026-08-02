using JustyBase.Common.Models;
using JustyBase.Services.Documents;

namespace JustyBase.Tests;

public sealed class SqlRunLifecycleServiceTests
{
    [Fact]
    public void CreateStartPlan_IncrementsRunningCountAndBuildsRunningLog()
    {
        var service = new SqlRunLifecycleService();

        var plan = service.CreateStartPlan(currentRunningCount: 2);

        Assert.Equal(3, plan.UpdatedRunningCount);
        Assert.True(plan.ShouldNotifyTasksToAbort);
        Assert.True(plan.ShouldNotifyIsStopEnabled);
        Assert.Equal("Running", plan.LogMessage);
        Assert.Equal(LogMessageType.inProgress, plan.LogMessageType);
        Assert.Equal("Started", plan.InnerLogMessage);
    }

    [Fact]
    public void CreateMissingConnectionPlan_ReturnsExpectedInnerMessage()
    {
        var service = new SqlRunLifecycleService();

        var plan = service.CreateMissingConnectionPlan();

        Assert.Equal("cannot establish connection", plan.InnerLogMessage);
    }

    [Fact]
    public void CreateCompletionPlan_DecrementsRunningCountAndReenablesRun_WhenRunWasDisabled()
    {
        var service = new SqlRunLifecycleService();
        var finishedAt = new DateTime(2026, 03, 14, 19, 45, 00, DateTimeKind.Utc);

        var plan = service.CreateCompletionPlan(
            currentRunningCount: 2,
            isRunEnabled: false,
            currentLogMessageType: LogMessageType.ok,
            finishedAt: finishedAt);

        Assert.Equal(1, plan.UpdatedRunningCount);
        Assert.True(plan.ShouldNotifyTasksToAbort);
        Assert.True(plan.ShouldEnableRun);
        Assert.True(plan.ShouldSetLogMessageTypeToOk);
        Assert.Equal("Finished", plan.InnerLogMessage);
        Assert.Equal($"Finished {finishedAt}", plan.FinalLogMessage);
    }

    [Fact]
    public void CreateCompletionPlan_DoesNotForceOk_WhenCurrentLogIsError()
    {
        var service = new SqlRunLifecycleService();

        var plan = service.CreateCompletionPlan(
            currentRunningCount: 1,
            isRunEnabled: true,
            currentLogMessageType: LogMessageType.error,
            finishedAt: new DateTime(2026, 03, 14, 19, 46, 00, DateTimeKind.Utc));

        Assert.Equal(0, plan.UpdatedRunningCount);
        Assert.False(plan.ShouldEnableRun);
        Assert.False(plan.ShouldSetLogMessageTypeToOk);
    }

    [Fact]
    public void CreateCompletionPlan_ClampsRunningCountAtZero()
    {
        var service = new SqlRunLifecycleService();

        var plan = service.CreateCompletionPlan(
            currentRunningCount: 0,
            isRunEnabled: true,
            currentLogMessageType: null,
            finishedAt: new DateTime(2026, 03, 14, 19, 47, 00, DateTimeKind.Utc));

        Assert.Equal(0, plan.UpdatedRunningCount);
        Assert.False(plan.ShouldSetLogMessageTypeToOk);
    }

    [Fact]
    public void CreateReturnPhasePlan_WhenTabIsInactive_MarksRecentlyFinished()
    {
        var service = new SqlRunLifecycleService();

        var plan = service.CreateReturnPhasePlan(
            isRunEnabled: false,
            isActiveDockable: false);

        Assert.True(plan.ShouldEnableRun);
        Assert.True(plan.ShouldMarkRecentlyFinished);
    }

    [Fact]
    public void CreateReturnPhasePlan_WhenTabIsActiveAndRunEnabled_DoesNothing()
    {
        var service = new SqlRunLifecycleService();

        var plan = service.CreateReturnPhasePlan(
            isRunEnabled: true,
            isActiveDockable: true);

        Assert.False(plan.ShouldEnableRun);
        Assert.False(plan.ShouldMarkRecentlyFinished);
    }
}

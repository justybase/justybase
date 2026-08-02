using JustyBase.Services.Documents;
using JustyBase.Helpers.Shared;

namespace JustyBase.Tests;

/// <summary>
/// Tests verifying the contracts introduced by the SqlExecutionService refactoring.
/// Ensures interfaces and records are correctly defined and maintain their API surface.
/// </summary>
public class SqlExecutionServiceTests
{
    /// <summary>
    /// ISqlExecutionService must expose ExecuteSqlAsync to be callable from the ViewModel.
    /// </summary>
    [Fact]
    public void ISqlExecutionService_ExposesExecuteSqlAsync()
    {
        var method = typeof(ISqlExecutionService).GetMethod("ExecuteSqlAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    /// <summary>
    /// ISqlExecutionBridge must define all callback methods the service relies on.
    /// </summary>
    [Theory]
    [InlineData("AddLogMessage")]
    [InlineData("ShowProgress")]
    [InlineData("HandleStandardGrid")]
    [InlineData("HandleAnotherResult")]
    [InlineData("ErrorMessageToUi")]
    [InlineData("ClosePreviousResultsIfNeeded")]
    [InlineData("GetConToGo")]
    [InlineData("TryReconnectOnce")]
    [InlineData("ResetReconnectCounter")]
    [InlineData("TrackQueryState")]
    [InlineData("SelectError")]
    [InlineData("FlashWindowExIfNeeded")]
    [InlineData("AddToHistory")]
    [InlineData("RefreshDatabaseList")]
    public void ISqlExecutionBridge_DefinesExpectedMethod(string methodName)
    {
        var methods = typeof(ISqlExecutionBridge).GetMethods();
        Assert.Contains(methods, m => m.Name == methodName);
    }

    /// <summary>
    /// SqlExecutionPlan record correctly stores all its properties.
    /// </summary>
    [Fact]
    public void SqlExecutionPlan_StoresProperties()
    {
        var statements = new List<string> { "SELECT 1", "SELECT 2" };
        var plan = new SqlDocumentViewModelHelper.SqlExecutionPlan(
            SingleCommand: false,
            TabsWithRows: true,
            TimeoutOverride: false,
            ForcedTimeout: 60,
            ContinueOnError: true,
            SqlStatements: statements
        );

        Assert.False(plan.SingleCommand);
        Assert.True(plan.TabsWithRows);
        Assert.False(plan.TimeoutOverride);
        Assert.Equal(60, plan.ForcedTimeout);
        Assert.True(plan.ContinueOnError);
        Assert.Equal(2, plan.SqlStatements.Count);
        Assert.Equal("SELECT 1", plan.SqlStatements[0]);
        Assert.Equal("SELECT 2", plan.SqlStatements[1]);
    }

    /// <summary>
    /// SqlExecutionService implements ISqlExecutionService interface.
    /// </summary>
    [Fact]
    public void SqlExecutionService_ImplementsInterface()
    {
        Assert.True(typeof(ISqlExecutionService).IsAssignableFrom(typeof(SqlExecutionService)));
    }
}

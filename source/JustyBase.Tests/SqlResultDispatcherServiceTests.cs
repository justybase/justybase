using System.Data.Common;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.PluginCommon.Contracts;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlResultDispatcherServiceTests
{
    [Fact]
    public void DispatchGridResult_WhenReaderHasRows_AddsGridResult()
    {
        var service = new SqlResultDispatcherService();
        var resultManager = new RecordingResultManager();
        var dbService = new Mock<IDatabaseService>().Object;
        var reader = new Mock<DbDataReader>();
        reader.SetupGet(r => r.HasRows).Returns(true);
        var command = CreateCommand("select 1");
        int abortUpperBound = 10;

        service.DispatchGridResult(
            "doc-1",
            resultManager,
            dbService,
            "Result",
            "select 1",
            null,
            tabsWithRows: true,
            actualQueryNum: 7,
            ref abortUpperBound,
            reader.Object,
            command.Object,
            "select 1");

        var call = Assert.Single(resultManager.AddedResults);
        Assert.Same(dbService, call.Result.DbService);
        Assert.Same(reader.Object, call.Result.Reader);
        Assert.Equal(string.Empty, call.Result.ErrorMessage);
        Assert.Equal("doc-1", call.Id);
        Assert.Equal(7, call.QueryNum);
        Assert.Equal("select 1", call.Sql);
    }

    [Fact]
    public void DispatchGridResult_WhenReaderHasNoRowsAndTabsWithRowsEnabled_SkipsResult()
    {
        var service = new SqlResultDispatcherService();
        var resultManager = new RecordingResultManager();
        var dbService = new Mock<IDatabaseService>().Object;
        var reader = new Mock<DbDataReader>();
        reader.SetupGet(r => r.HasRows).Returns(false);
        var command = CreateCommand("select 1");
        int abortUpperBound = 10;

        service.DispatchGridResult(
            "doc-1",
            resultManager,
            dbService,
            "Result",
            "select 1",
            null,
            tabsWithRows: true,
            actualQueryNum: 7,
            ref abortUpperBound,
            reader.Object,
            command.Object,
            "select 1");

        Assert.Empty(resultManager.AddedResults);
    }

    [Fact]
    public void DispatchErrorResult_WhenQueryIsCancelled_DoesNotAddErrorTab()
    {
        var service = new SqlResultDispatcherService();
        var resultManager = new RecordingResultManager();
        var dbService = new Mock<IDatabaseService>().Object;
        var command = CreateCommand("select 1");
        int abortUpperBound = 3;

        service.DispatchErrorResult(
            "doc-1",
            resultManager,
            "Run",
            null,
            actualQueryNum: 9,
            ref abortUpperBound,
            dbService,
            currentSqlNumber: 0,
            sql: "select 1",
            command.Object,
            new Exception("ERROR: Query was cancelled."));

        Assert.Empty(resultManager.AddedResults);
    }

    [Fact]
    public void DispatchWarningResult_AddsConnectionWarningResult()
    {
        var service = new SqlResultDispatcherService();
        var resultManager = new RecordingResultManager();
        int abortUpperBound = 1;

        service.DispatchWarningResult(
            "doc-2",
            resultManager,
            "Run",
            actualQueryNum: 12,
            ref abortUpperBound,
            actualDatabaseService: null);

        var call = Assert.Single(resultManager.AddedResults);
        Assert.Equal("cannot establish connection", call.Result.ErrorMessage);
        Assert.Equal("Run", call.Title);
        Assert.Equal("doc-2", call.Id);
        Assert.Equal(12, call.QueryNum);
    }

    [Fact]
    public void DispatchRuntimeErrorResult_WhenExceptionIsGeneric_AddsErrorTab()
    {
        var service = new SqlResultDispatcherService();
        var resultManager = new RecordingResultManager();
        int abortUpperBound = 4;

        service.DispatchRuntimeErrorResult(
            "doc-3",
            resultManager,
            "Run",
            actualQueryNum: 13,
            ref abortUpperBound,
            new InvalidOperationException("boom"));

        var call = Assert.Single(resultManager.AddedResults);
        Assert.Equal("boom", call.Result.ErrorMessage);
        Assert.Equal("Run", call.Title);
        Assert.Equal("doc-3", call.Id);
        Assert.Equal(13, call.QueryNum);
    }

    [Fact]
    public void DispatchRuntimeErrorResult_WhenNzDotNetSlimPlatformError_SuppressesResult()
    {
        var service = new SqlResultDispatcherService();
        var resultManager = new RecordingResultManager();
        int abortUpperBound = 4;
        var exception = new PlatformNotSupportedException("Operation is not supported on this platform.")
        {
            Source = "NZdotNETSlim"
        };

        service.DispatchRuntimeErrorResult(
            "doc-3",
            resultManager,
            "Run",
            actualQueryNum: 13,
            ref abortUpperBound,
            exception);

        Assert.Empty(resultManager.AddedResults);
    }

    private static Mock<DbCommand> CreateCommand(string commandText)
    {
        var command = new Mock<DbCommand>();
        command.SetupGet(c => c.CommandText).Returns(commandText);
        return command;
    }

    private sealed class RecordingResultManager : ISqlResultManager
    {
        public List<AddedResultCall> AddedResults { get; } = [];
        public List<string> ClosedResultIds { get; } = [];

        public void ClosePrevResults(string id)
        {
            ClosedResultIds.Add(id);
        }

        public void AddNewResult((IDatabaseService? dbService, DbDataReader? rdr, string errorMessage) res, string id, int queryNum, ref int abortUbound, string? sql, DbCommand? command, string? title)
        {
            AddedResults.Add(new AddedResultCall((res.dbService, res.rdr, res.errorMessage), id, queryNum, abortUbound, sql, command, title));
        }
    }

    private sealed record AddedResultCall(
        (IDatabaseService? DbService, DbDataReader? Reader, string ErrorMessage) Result,
        string Id,
        int QueryNum,
        int AbortUpperBound,
        string? Sql,
        DbCommand? Command,
        string? Title);
}

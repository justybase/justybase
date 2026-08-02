using System.Data.Common;
using JustyBase.Common.Models;
using JustyBase.Services.Documents;
using JustyBase.PluginCommon.Contracts;
using Moq;

namespace JustyBase.Tests;

public sealed class SqlExecutionStateServiceTests
{
    [Fact]
    public void RegisterNewQuery_AssignsIncreasingIdsAndTracksActiveQueries()
    {
        var service = new SqlExecutionStateService(ISimpleLogger.EmptyLogger);

        int firstQuery = service.RegisterNewQuery();
        int secondQuery = service.RegisterNewQuery();

        Assert.Equal(1, firstQuery);
        Assert.Equal(2, secondQuery);
        Assert.Equal(2, service.GlobalQueryNumber);
        Assert.Equal(2, service.ActiveTasksCount);
    }

    [Fact]
    public void MarkFullFinish_RemovesQueryFromActiveCount()
    {
        var service = new SqlExecutionStateService(ISimpleLogger.EmptyLogger);
        int queryNumber = service.RegisterNewQuery();

        service.MarkFullFinish(queryNumber);

        Assert.Equal(0, service.ActiveTasksCount);
    }

    [Fact]
    public async Task AbortAllAsync_CancelsStartedCommandsAndClearsTrackedQueries()
    {
        var service = new SqlExecutionStateService(ISimpleLogger.EmptyLogger);
        int queryNumber = service.RegisterNewQuery();
        var command = new Mock<DbCommand>();
        command.Setup(c => c.Cancel());

        service.TrackCommandState(queryNumber, command.Object, SqlCommandState.started);

        await service.AbortAllAsync();

        command.Verify(c => c.Cancel(), Times.Exactly(2));
        Assert.Equal(0, service.ActiveTasksCount);
        Assert.True(service.GlobalAbortUpperBound > service.GlobalQueryNumber);
    }
}

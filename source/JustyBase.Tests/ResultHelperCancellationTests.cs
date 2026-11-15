using System.Data;
using JustyBase.Common.Contracts;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Results;
using Moq;

namespace JustyBase.Tests;

public sealed class ResultHelperCancellationTests
{
    [Fact]
    public async Task CreateExcelFileAsync_WhenCancelledBeforeStart_LeavesNoFile()
    {
        using var manager = new SpillStoreManager();
        using ResultSpillStore store = manager.CreateStore(new ResultSpillSchema(
            [new ResultSpillColumn("id", TypeCode.Int32, SpillColumnKind.Integral)]));
        store.BeginWriteBatch();
        store.WriteRow([1]);
        store.EndWriteBatch();

        using var reader = new SpillResultReader(store, sortKeys: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var helper = new ResultHelper(
            generalApplicationData: null,
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger);

        string path = Path.Combine(Path.GetTempPath(), $"justybase-cancel-{Guid.NewGuid():N}.xlsx");

        await helper.CreateExcelFileAsync(path, reader, "SELECT 1;", cts.Token);

        Assert.False(File.Exists(path));
    }
}

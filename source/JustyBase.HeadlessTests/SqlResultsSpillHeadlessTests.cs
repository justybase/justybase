using System.Data;
using System.Data.Common;
using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services;
using JustyBase.Services.DataGrid;
using JustyBase.Services.Results;
using JustyBase.ViewModels.Tools;
using Microsoft.Data.Sqlite;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// End-to-end coverage for the spill activation path: a large result crosses the
/// configured threshold, the grid keeps a page, and "copy all rows" streams every
/// spilled row instead of the visible page.
/// </summary>
public sealed class SqlResultsSpillHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task LoadRest_SpillsLargeResultAndCopiesEveryRow() => RunOnUiAsync(async () =>
    {
        var options = new AppOptions
        {
            ResultSpillThreshold = 10_000,
            ResultSpillPageSize = 1_000,
            ResultRowsLimit = 1_000_000
        };

        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, options);

        using var storeService = new ResultStoreService(
            memoryBudgetBytes: 256L * 1024 * 1024,
            spillQuotaBytes: 256L * 1024 * 1024);

        var vm = new SqlResultsViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IClipboardService>(),
            appData.Object,
            Mock.Of<IMessageForUserTools>(),
            ISimpleLogger.EmptyLogger,
            Mock.Of<IResultGridActionRoutingService>(),
            Mock.Of<IActiveDocumentManager>(),
            new DataGridClipboardService(),
            storeService);

        using var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));
        for (int i = 0; i < 30_000; i++)
        {
            table.Rows.Add(i, $"row-{i}");
        }

        using var reader = table.CreateDataReader();
        var db = new Mock<IDatabaseService>();
        db.Setup(x => x.GetDatabaseRowReader(It.IsAny<DbDataReader>()))
            .Returns((DbDataReader source) => new DatabaseRowReaderGeneral(source));

        vm.LoadData((db.Object, reader, ""));
        int abortUbound = -1;
        using var command = new SqliteCommand();
        vm.LoadRest(db.Object, reader, 0, ref abortUbound, command);

        Assert.True(vm.IsSpillMode);
        Assert.Equal(30_000, vm.SpillTotalRows);
        Assert.Equal(30_000, vm.SpillFilteredRows);
        Assert.Equal(1_000, vm.CurrentResultsTable.FilteredRows.Count);

        string text = await vm.BuildAllRowsClipboardTextAsync(["id", "name"]);

        Assert.Contains("id\tname", text);
        Assert.Contains("29999\trow-29999", text);
        Assert.Equal(30_001, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    });
}

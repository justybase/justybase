using System.Collections;
using System.Data;
using System.Data.Common;
using Dock.Model.Core;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Models;
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
/// The Netezza driver reports a result-set boundary between row blocks of the
/// same query. The grid must keep reading those blocks into one result instead
/// of showing a separate tab per block.
/// </summary>
public sealed class SqlResultsBlockBoundaryHeadlessTests : HeadlessSessionTestBase
{
    [Fact]
    public Task LoadRest_ContinuesAcrossSameSchemaBlockBoundaries() => RunOnUiAsync(() =>
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupProperty(x => x.Config, new AppOptions
        {
            ResultSpillThreshold = 250_000,
            ResultSpillPageSize = 20_000,
            ResultRowsLimit = 1_000_000
        });

        var messageForUserTools = new Mock<IMessageForUserTools>();
        messageForUserTools
            .Setup(x => x.DispatcherActionInstance(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        messageForUserTools
            .Setup(x => x.DispatcherActionInstance(It.IsAny<Action>(), It.IsAny<object>()))
            .Callback<Action, object>((action, _) => action());

        using var storeService = new ResultStoreService(
            memoryBudgetBytes: 256L * 1024 * 1024,
            spillQuotaBytes: 256L * 1024 * 1024);

        using var vm = new SqlResultsViewModel(
            Mock.Of<IFactory>(),
            Mock.Of<IAvaloniaSpecificHelpers>(),
            Mock.Of<IClipboardService>(),
            appData.Object,
            messageForUserTools.Object,
            ISimpleLogger.EmptyLogger,
            Mock.Of<IResultGridActionRoutingService>(),
            Mock.Of<IActiveDocumentManager>(),
            new DataGridClipboardService(),
            storeService);

        using var reader = new BlockBoundaryReader(rowCount: 1_200, blockSize: 500);
        var db = new Mock<IDatabaseService>();
        db.Setup(x => x.GetDatabaseRowReader(It.IsAny<DbDataReader>()))
            .Returns((DbDataReader source) => new DatabaseRowReaderGeneral(source));

        vm.LoadData((db.Object, reader, ""));
        Assert.Equal(500, vm.CurrentResultsTable.Rows.Count);
        Assert.Equal(0, reader.BlockIndex);
        int abortUbound = -1;
        using var command = new SqliteCommand();
        vm.LoadRest(db.Object, reader, 0, ref abortUbound, command);

        Assert.False(vm.IsSpillMode);
        Assert.Equal(2, reader.BlockIndex);
        Assert.Equal(1_200, vm.CurrentResultsTable.Rows.Count);
        Assert.Equal(1_200, vm.CurrentResultsTable.FilteredRows.Count);
        TableRow lastRow = vm.CurrentResultsTable.FilteredRows[vm.CurrentResultsTable.FilteredRows.Count - 1];
        Assert.Equal(1_199, Assert.IsType<int>(lastRow.Fields[0]));

        return Task.CompletedTask;
    });

    /// <summary>
    /// Emulates a driver that ends <see cref="Read"/> after every block and
    /// exposes the continuation through <see cref="NextResult"/>.
    /// </summary>
    private sealed class BlockBoundaryReader : DbDataReader
    {
        private readonly List<DataTableReader> _blocks;
        private int _index;

        public BlockBoundaryReader(int rowCount, int blockSize)
        {
            _blocks = [];
            for (int start = 0; start < rowCount; start += blockSize)
            {
                using var table = new DataTable();
                table.Columns.Add("id", typeof(int));
                table.Columns.Add("name", typeof(string));
                for (int i = start; i < Math.Min(start + blockSize, rowCount); i++)
                {
                    table.Rows.Add(i, $"row-{i}");
                }

                _blocks.Add(table.CreateDataReader());
            }
        }

        private DataTableReader Current => _blocks[_index];

        public int BlockIndex => _index;

        public override bool Read() => Current.Read();

        public override bool NextResult()
        {
            if (_index + 1 >= _blocks.Count)
            {
                return false;
            }

            _index++;
            return true;
        }

        public override int FieldCount => Current.FieldCount;
        public override bool HasRows => _blocks.Exists(block => block.HasRows);
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => Current[ordinal];
        public override object this[string name] => Current[name];
        public override string GetName(int ordinal) => Current.GetName(ordinal);
        public override int GetOrdinal(string name) => Current.GetOrdinal(name);
        public override object GetValue(int ordinal) => Current.GetValue(ordinal);
        public override bool IsDBNull(int ordinal) => Current.IsDBNull(ordinal);
        public override bool GetBoolean(int ordinal) => Current.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => Current.GetByte(ordinal);
        public override char GetChar(int ordinal) => Current.GetChar(ordinal);
        public override DateTime GetDateTime(int ordinal) => Current.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => Current.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => Current.GetDouble(ordinal);
        public override float GetFloat(int ordinal) => Current.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => Current.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => Current.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => Current.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => Current.GetInt64(ordinal);
        public override string GetString(int ordinal) => Current.GetString(ordinal);
        public override int GetValues(object[] values) => Current.GetValues(values);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
            => Current.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
            => Current.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override Type GetFieldType(int ordinal) => Current.GetFieldType(ordinal);
        public override string GetDataTypeName(int ordinal) => Current.GetDataTypeName(ordinal);
        public override DataTable? GetSchemaTable() => Current.GetSchemaTable();
        public override IEnumerator GetEnumerator() => Current.GetEnumerator();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (DataTableReader block in _blocks)
                {
                    block.Dispose();
                }

                _blocks.Clear();
            }

            base.Dispose(disposing);
        }
    }
}

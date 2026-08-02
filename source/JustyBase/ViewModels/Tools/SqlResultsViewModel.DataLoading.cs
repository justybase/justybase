using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Avalonia.Threading;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Results;

namespace JustyBase.ViewModels.Tools;

partial class SqlResultsViewModel
{
    private const int INITIAL_ROWS_LIMIT = 500;
    private const int EXTREME_RESULT_THRESHOLD = 1_000_000;
    private const int MIN_SPILL_PAGE_SIZE = 1_000;
    private const int MAX_SPILL_PAGE_SIZE = 50_000;
    private const int MIN_RESULT_ROWS_LIMIT = 50_000;
    private ResultSpillStore? _spillStore;

    public void LoadData((IDatabaseService dbService, DbDataReader rdr, string errorMessage) res)
    {
        DisposeSpill();

        if (!string.IsNullOrWhiteSpace(res.errorMessage))
        {
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                ErrorMessage = res.errorMessage;
            });
            return;
        }

        var reader = res.rdr;
        if (reader == null || !reader.HasRows && reader.FieldCount <= 0)
        {
            return;
        }

        var headers = new List<string>(reader.FieldCount);
        var dtnames = new List<string>(reader.FieldCount);
        var typeCodes = new List<TypeCode>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; ++i)
        {
            headers.Add(reader.GetName(i));
            string typeName = reader.GetDataTypeName(i);
            typeName ??= reader.GetFieldType(i).Name;
            dtnames.Add(typeName);
            if (typeName == "int1")
            {
                typeCodes.Add(TypeCode.Byte);
            }
            else
            {
                typeCodes.Add(Type.GetTypeCode(reader.GetFieldType(i)));
            }
        }
        var st = reader.GetSchemaTable();

        if (st is not null && st.Columns.Contains("NumericScale"))
        {
            CurrentResultsTable.NumericScales = new byte[reader.FieldCount];
            int nm = 0;
            foreach (var item in st.Rows.OfType<DataRow>())
            {
                var scale = item["NumericScale"];
                if (scale is not null && scale != DBNull.Value)
                {
                    try
                    {
                        byte byteScale = (byte)Math.Clamp(Convert.ToInt32(scale), 0, 127);
                        CurrentResultsTable.NumericScales[nm] = (byteScale == 127) ? (byte)8 : byteScale;
                    }
                    catch (Exception ex)
                    {
                        _simpleLogger.TrackError(ex, isCrash: false);
                        CurrentResultsTable.NumericScales[nm] = 0;
                    }
                }
                nm++;
            }
        }
        for (int i = headers.Count - 1; i >= 0; i--)
        {
            var ch = headers[i];
            int cnt = headers.Count(o => o == ch);
            if (cnt > 1)
            {
                headers[i] = ch + $"_{cnt}";
            }
        }

        var rows = new List<TableRow>();
        CurrentResultsTable.Headers = headers;
        CurrentResultsTable.DataTypeNames = dtnames;
        CurrentResultsTable.TypeCodes = typeCodes;
        CurrentResultsTable.Rows = rows;

        ViewBridge?.RefreshColumns();

        _messageForUserTools.DispatcherActionInstance(() =>
        {
            GridVisible = false;
            DataLoadingInProgress = true;
            LoadingPlaceholderMessage = "Loading preview…";
            RowsLoadingMessage = "Loading…";
        });

        try
        {
            int a = 0;
            lock (_lock)
            {
                var drr = res.dbService.GetDatabaseRowReader(reader);
                while (a++ < INITIAL_ROWS_LIMIT && reader.Read())
                {
                    var row = new TableRow
                    {
                        Fields = drr.ReadOneRow(),
                    };
                    rows.Add(row);
                }
            }
        }
        finally
        {
            // Preview first page immediately (single bulk notification), rest arrives in LoadRest.
            // Empty schema-only results skip LoadRest (!HasRows), so clear loading here.
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                PublishFilteredRows(rows, replace: true);
                LoadingPlaceholderMessage = "";
                GridVisible = true;
                RowsLoadingMessage = $"{CurrentResultsTable.FilteredRows.Count:N0} rows";
                if (rows.Count == 0)
                {
                    DataLoadingInProgress = false;
                }
            });
        }
    }

    public void LoadRest(IDatabaseService? dbService, DbDataReader reader, int queryNum, ref int abortUbound, DbCommand command)
    {
        List<TableRow> rowsTemp = [];
        int i1 = CurrentResultsTable.Rows.Count;
        int spillThreshold = Math.Max(EXTREME_RESULT_THRESHOLD, _generalApplicationData.Config.ResultSpillThreshold);
        int pageSize = Math.Clamp(_generalApplicationData.Config.ResultSpillPageSize, MIN_SPILL_PAGE_SIZE, MAX_SPILL_PAGE_SIZE);
        int effectiveResultRowsLimit = Math.Max(MIN_RESULT_ROWS_LIMIT, _generalApplicationData.Config.ResultRowsLimit);
        ResultSpillStore? spill = null;
        bool spillActivated = false;
        int previewRows = i1;

        // Keep preview rows visible and bound. Do not mutate FilteredRows while reading —
        // only detach briefly at the end for the bulk append (same idea as leaving the Results tab).
        // Sync Invoke: background reader must barrier on UI before continuing the DB loop.
        Dispatcher.UIThread.Invoke(() =>
        {
            DataLoadingInProgress = true;
            LoadingPlaceholderMessage = "";
            RowsLoadingMessage = $"Preview {previewRows:N0} rows · loading more…";
        }, DispatcherPriority.Background);

        try
        {
            var drr = dbService.GetDatabaseRowReader(reader);
            long startTime = Stopwatch.GetTimestamp();

            while (reader.Read() && queryNum >= abortUbound)
            {
                var fields = drr.ReadOneRow();
                i1++;

                if (!spillActivated && i1 > spillThreshold)
                {
#pragma warning disable CA2000 // Ownership is transferred to _spillStore in the finally block.
                    spill = new ResultSpillStore();
#pragma warning restore CA2000
                    spill.PageSize = pageSize;
                    spill.BeginWriteBatch();
                    foreach (var existing in CurrentResultsTable.Rows)
                    {
                        spill.WriteRow(existing.Fields);
                    }
                    foreach (var pending in rowsTemp)
                    {
                        spill.WriteRow(pending.Fields);
                    }
                    rowsTemp.Clear();
                    spillActivated = true;
                }

                if (spillActivated)
                {
                    spill!.WriteRow(fields);
                }
                else
                {
                    rowsTemp.Add(new TableRow { Fields = fields });
                }

                if (i1 == effectiveResultRowsLimit)
                {
                    command.Cancel();
                    abortUbound = queryNum + 1;
                    break;
                }
                if (i1 % 10_000 == 0 && Stopwatch.GetElapsedTime(startTime).TotalSeconds >= 1)
                {
                    startTime = Stopwatch.GetTimestamp();
                    int localI = i1;
                    int localPreview = previewRows;
                    _messageForUserTools.DispatcherActionInstance(() =>
                    {
                        RowsLoadingMessage = spillActivated
                            ? $"{localI:N0} rows (spilling…)"
                            : $"Preview {localPreview:N0} rows · loaded {localI:N0}…";
                    }, DispatcherPriority.Background);
                }
            }
        }
        finally
        {
            if (spillActivated && spill is not null)
            {
                spill.EndWriteBatch();
                DisposeSpill();
                _spillStore = spill;
                spill = null;
                IsSpillMode = true;
                SpillPageSize = pageSize;
                SpillPageIndex = 0;
                SpillTotalRows = spill.RowCount;
            }
            else
            {
                IsSpillMode = false;
                if (rowsTemp.Count > 0)
                {
                    lock (_lock)
                    {
                        CurrentResultsTable.Rows.AddRange(rowsTemp);
                    }
                }
            }

            // Sync Invoke: must finish detach → bulk publish → reattach before LoadRest returns.
            Dispatcher.UIThread.Invoke(() =>
            {
                // Detach only for the bulk append so the visible preview does not re-layout per row.
                ViewBridge?.SuspendGridBinding();

                if (IsSpillMode)
                {
                    ApplySpillPage(0);
                    RowsLoadingMessage = $"Spill {SpillTotalRows:N0} rows · page {SpillPageIndex + 1}/{SpillPageCount} (filter/group in-memory only below threshold)";
                }
                else if (rowsTemp.Count > 0)
                {
                    PublishFilteredRows(rowsTemp, replace: false, rebuildIndexMap: true);
                    rowsTemp.Clear();
                    RowsLoadingMessage = $"{CurrentResultsTable.FilteredRows.Count:N0} rows";
                }
                else
                {
                    lock (_lock)
                    {
                        CurrentResultsTable.RebuildRowIndexMap();
                    }
                }

                GridCollectionView = new Avalonia.Collections.DataGridCollectionView(CurrentResultsTable.FilteredRows);
                ViewBridge?.ResumeGridBinding();
                GridVisible = true;
                DataLoadingInProgress = false;
                LoadingPlaceholderMessage = "";
                GridEnabled = true;
                NotifySpillCommands();
            }, DispatcherPriority.Background);
        }
    }

    private void PublishFilteredRows(IReadOnlyList<TableRow> rows, bool replace, bool rebuildIndexMap = true)
    {
        lock (_lock)
        {
            if (replace)
            {
                CurrentResultsTable.FilteredRows.ReplaceAll(rows);
            }
            else
            {
                CurrentResultsTable.FilteredRows.AddRange(rows);
            }

            if (rebuildIndexMap)
            {
                CurrentResultsTable.RebuildRowIndexMap();
            }
        }
    }

    private void ApplySpillPage(int pageIndex)
    {
        if (_spillStore is null)
        {
            return;
        }

        pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, SpillPageCount - 1));
        SpillPageIndex = pageIndex;
        var page = _spillStore.ReadPage(pageIndex, SpillPageSize);
        var rows = page.Select(fields => new TableRow { Fields = fields! }).ToList();

        lock (_lock)
        {
            CurrentResultsTable.Rows.Clear();
            CurrentResultsTable.Rows.AddRange(rows);
            CurrentResultsTable.FilteredRows.ReplaceAll(rows);
            CurrentResultsTable.RebuildRowIndexMap();
        }
    }

    private void DisposeSpill()
    {
        _spillStore?.Dispose();
        _spillStore = null;
        IsSpillMode = false;
        SpillTotalRows = 0;
        SpillPageIndex = 0;
    }
}

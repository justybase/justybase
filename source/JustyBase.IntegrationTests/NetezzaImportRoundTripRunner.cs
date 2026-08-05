using System.Globalization;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.NetezzaDriver;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using Xunit.Sdk;
using NetezzaService = NetezzaDotnetPlugin.Netezza;

namespace JustyBase.IntegrationTests;

/// <summary>
/// Result of importing one CSV through the real app pipeline
/// (<see cref="ImportFromExcelFile"/> → <see cref="DbImportJob"/> → <see cref="NetezzaDotnetPlugin.Netezza.DbSpecificImportPart"/>).
/// </summary>
internal sealed class RoundTripContext : IAsyncDisposable
{
    public required NzConnection Connection { get; init; }
    public required NetezzaService Service { get; init; }
    public required string TableName { get; init; }
    public required string[] Columns { get; init; }
    public required DbTypeWithSize[] Types { get; init; }
    public required string CsvPath { get; init; }
    public required string LogDir { get; init; }
    public required IReadOnlyList<string> Progress { get; init; }

    public async ValueTask DisposeAsync()
    {
        ImportUsingOptionsContext.Current = null;
        NetezzaLiveTestHost.TryDrop(Connection, TableName);
        await Task.Yield();
        Connection.Dispose();
        NetezzaLiveTestHost.TryDeleteDirectory(LogDir);
        try
        {
            File.Delete(CsvPath);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// Drives the real JustyBase import pipeline against a live NetezzaDotnetPlugin.Netezza and returns a
/// disposable context for round-trip verification. Cleanup is the caller's disposal.
/// </summary>
internal static class NetezzaImportRoundTripRunner
{
    private static readonly CultureInfo s_invariant = CultureInfo.InvariantCulture;

    public static async Task<RoundTripContext> ImportCsvAsync(string csv, Action<DatabaseTypeChooser>? configure = null)
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"jbt_rt_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(csvPath, csv);
        string logDir = NetezzaLiveTestHost.CreateLogDirectory();
        NetezzaDotnetPlugin.Netezza service = NetezzaLiveTestHost.CreateService();
        service.TempDataDirectory = logDir;
        string table = NetezzaLiveTestHost.CreateTableName();
        ImportUsingOptionsContext.Current = ImportUsingOptions.Default;
        var progress = new List<string>();

        var import = new ImportFromExcelFile(msg => progress.Add(msg), logger: null)
        {
            FilePath = csvPath,
            StandardMessageAction = msg => progress.Add(msg)
        };
        try
        {
            Assert.True(import.InitImport(), $"InitImport failed for test CSV '{csvPath}'.");
            string sheet = import.SheetNamesToImport![0];
            DatabaseTypeChooser? chooser = await import.DetectSheetAsync(sheet, msg => progress.Add(msg));
            Assert.NotNull(chooser);
            configure?.Invoke(chooser);

            await foreach (DbImportJob job in import.ReadFileAndReturnSingleImportJobs())
            {
                Assert.NotNull(job.ColumnHeadersNames);
                await service.DbSpecificImportPart(job, table, msg => progress.Add(msg));
                AssertNoImportErrors(table, progress);

                NzConnection connection = NetezzaLiveTestHost.OpenConnection();
                return new RoundTripContext
                {
                    Connection = connection,
                    Service = service,
                    TableName = table,
                    Columns = job.ColumnHeadersNames,
                    Types = job.ColumnTypesBestMatch,
                    CsvPath = csvPath,
                    LogDir = logDir,
                    Progress = progress
                };
            }

            throw new XunitException($"No import job was produced for '{csvPath}'.");
        }
        finally
        {
            import.DoFileDispose();
        }
    }

    public static void AssertNoImportErrors(string table, IReadOnlyList<string> progress)
    {
        var errors = progress.Where(p => p.StartsWith("[ERROR]", StringComparison.Ordinal)).ToList();
        Assert.True(errors.Count == 0,
            $"Import into '{table}' reported errors: {string.Join(" | ", errors)}");
    }

    /// <summary>Projects each column to a canonical string in SQL so verification is driver-type agnostic.</summary>
    public static string ProjectColumn(DbTypeWithSize type, string quotedColumn)
        => type.DatabaseTypeSimple switch
        {
            DbSimpleType.Boolean => $"CASE WHEN {quotedColumn} THEN 'true' ELSE 'false' END",
            DbSimpleType.Date or DbSimpleType.TimeStamp => $"TO_CHAR({quotedColumn}, 'YYYY-MM-DD HH24:MI:SS')",
            DbSimpleType.Numeric => $"CAST({quotedColumn} AS VARCHAR(60))",
            DbSimpleType.Integer => $"CAST({quotedColumn} AS VARCHAR(40))",
            // NVARCHAR keeps the unicode round-trip; VARCHAR is byte-based on Netezza.
            _ => $"CAST({quotedColumn} AS NVARCHAR(4000))"
        };

    public static string? CanonicalExpected(object? value, DbSimpleType type)
    {
        if (value is null)
        {
            return null;
        }

        return type switch
        {
            DbSimpleType.Boolean => (bool)value ? "true" : "false",
            DbSimpleType.Integer => ((long)value).ToString(s_invariant),
            DbSimpleType.Numeric => ((decimal)value).ToString("0.###############################", s_invariant),
            DbSimpleType.Date or DbSimpleType.TimeStamp => ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", s_invariant),
            _ => (string)value
        };
    }

    public static string? CanonicalActual(object? value, DbSimpleType type)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        string text = Convert.ToString(value, s_invariant) ?? string.Empty;
        return type switch
        {
            DbSimpleType.Numeric when decimal.TryParse(text, NumberStyles.Number, s_invariant, out decimal parsed)
                => parsed.ToString("0.###############################", s_invariant),
            _ => text
        };
    }

    public static void VerifyRows(
        RoundTripContext ctx,
        IReadOnlyList<IReadOnlyList<object?>> expected,
        string caseName)
    {
        long count = Convert.ToInt64(NetezzaLiveTestHost.ExecuteScalar(
            ctx.Connection,
            $"SELECT COUNT(*) FROM {ctx.TableName}"),
            s_invariant);
        Assert.Equal(expected.Count, (int)count);

        string[] quoted = ctx.Columns.Select(c => ctx.Service.QuoteNameIfNeeded(c)).ToArray();
        string selectList = string.Join(", ", quoted.Select((c, i) => ProjectColumn(ctx.Types[i], c)));
        string sql = $"SELECT {selectList} FROM {ctx.TableName} ORDER BY {quoted[0]}";
        List<object?[]> rows = NetezzaLiveTestHost.ExecuteReaderRows(ctx.Connection, sql, ctx.Columns.Length);

        Assert.Equal(expected.Count, rows.Count);
        for (int r = 0; r < expected.Count; r++)
        {
            IReadOnlyList<object?> expectedRow = expected[r];
            object?[] actualRow = rows[r];
            for (int c = 0; c < ctx.Columns.Length; c++)
            {
                DbSimpleType type = ctx.Types[c].DatabaseTypeSimple;
                string? expectedValue = CanonicalExpected(c > expectedRow.Count - 1 ? null : expectedRow[c], type);
                string? actualValue = CanonicalActual(c > actualRow.Length - 1 ? null : actualRow[c], type);
                Assert.True(
                    string.Equals(expectedValue, actualValue, StringComparison.Ordinal),
                    $"Case '{caseName}' row {r} column '{ctx.Columns[c]}' ({type}): expected '{expectedValue}' actual '{actualValue}'.");
            }
        }
    }

    public static void VerifyColumnFormats(
        RoundTripContext ctx,
        IReadOnlyDictionary<string, Func<string, bool>> expectations)
    {
        string sql = $"""
            SELECT X.ATTNAME, X.FORMAT_TYPE
            FROM {NetezzaLiveTestHost.Database}.._V_RELATION_COLUMN X
            INNER JOIN {NetezzaLiveTestHost.Database}.._V_OBJECT_DATA O ON X.OBJID = O.OBJID
            WHERE UPPER(O.OBJNAME) = '{ctx.TableName.ToUpperInvariant()}'
            ORDER BY X.ATTNUM
            """;
        var rows = NetezzaLiveTestHost.ExecuteReaderRows(ctx.Connection, sql, 2);
        var actual = rows
            .Select(r => (name: Convert.ToString(r[0], s_invariant) ?? string.Empty, format: Convert.ToString(r[1], s_invariant) ?? string.Empty))
            .ToDictionary(t => t.name, t => t.format, StringComparer.OrdinalIgnoreCase);

        foreach ((string column, Func<string, bool> check) in expectations)
        {
            Assert.True(actual.TryGetValue(column, out string? format) && check(format),
                $"Column '{column}' expected a FORMAT_TYPE satisfying the check but was '{format ?? "(missing)"}'.");
        }
    }
}



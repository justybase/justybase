using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using SpreadSheetTasks;

namespace JustyBase.Common.Tools.ImportHelpers;

public sealed class DatabaseTypeChooser
{
    public string[]? NormalizedColumnHeaderNames { get; set; }
    public string[]? OriginalColumnHeaderNames { get; set; } // column headers not normalized
    /// <summary>The result of source detection. It never changes when a user selects an override.</summary>
    public DbTypeWithSize[]? DetectedColumnTypes { get; private set; }
    /// <summary>The types used by the import job. This is the per-sheet override plan.</summary>
    public DbTypeWithSize[]? ColumnTypesBestMatch { get; set; }
    public int[]? RawValueLengths { get; private set; }
    public List<ImportValidationError> ValidationErrors { get; } = [];
    public const int DEFAULT_NVARCHAR_LENGTH = 255;

    /// <summary>
    /// Populates every detection-driven field of this chooser from the shared scanner result
    /// (<see cref="TabularImportScanner.ScanSource"/>/<see cref="TabularImportScanner.ScanSheetAsync"/>),
    /// applying the <c>_#TEXT</c>/<c>_#NUMERIC</c>/<c>_#INTEGER</c>/<c>_#DATE</c>/<c>_#TIMESTAMP</c>
    /// header overrides exactly as the previous inline implementation did.
    /// </summary>
    public void ApplyScan(SheetScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        int fieldCount = scan.NormalizedHeaders.Length;

        OriginalColumnHeaderNames = scan.OriginalHeaders;
        NormalizedColumnHeaderNames = scan.NormalizedHeaders;
        RawValueLengths = scan.RawValueLengths;
        PreviewRows = scan.PreviewRows.ToList();
        RowsCount = scan.RowsCount;
        DetectedColumnTypes = new DbTypeWithSize[fieldCount];
        ColumnTypesBestMatch = new DbTypeWithSize[fieldCount];

        for (int i = 0; i < fieldCount; i++)
        {
            string name = scan.OriginalHeaders[i];
            DbTypeWithSize type = MapDetected(scan.DetectedTypes[i]);

            if (name.EndsWith("_#TEXT", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Nvarchar)
                {
                    TextLength = GetTextLength(i)
                };
            }
            else if (name.EndsWith("_#NUMERIC", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Numeric) { NumericPrecision = 20, NumericScale = 6 };
            }
            else if (name.EndsWith("_#INTEGER", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Integer);
            }
            else if (name.EndsWith("_#DATE", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Date);
            }
            else if (name.EndsWith("_#TIMESTAMP", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.TimeStamp);
            }
            else
            {
                ColumnTypesBestMatch[i] = type;
            }
        }

        DetectedColumnTypes = ColumnTypesBestMatch.Select(CloneType).ToArray();
        ValidationErrors.Clear();
    }

    public void ResetSelectedTypesToDetected()
    {
        if (DetectedColumnTypes is null)
            throw new InvalidOperationException("DetectedColumnTypes is null");

        ColumnTypesBestMatch = DetectedColumnTypes.Select(CloneType).ToArray();
        ValidationErrors.Clear();
    }

    public void SetValidationErrors(IEnumerable<ImportValidationError> errors)
    {
        ValidationErrors.Clear();
        ValidationErrors.AddRange(errors);
    }

    private int GetTextLength(int columnNumber)
    {
        int rawLength = RawValueLengths is null || columnNumber >= RawValueLengths.Length ? 0 : RawValueLengths[columnNumber];
        return Math.Max(DEFAULT_NVARCHAR_LENGTH, rawLength);
    }

    private static DbTypeWithSize CloneType(DbTypeWithSize type) => type with { };

    public Type GetNativeType(int i)
    {
        if (ColumnTypesBestMatch is null)
            throw new InvalidOperationException("ColumnTypesBestMatch is null");
        return ColumnTypesBestMatch[i].GetNativeType();
    }

    /// <summary>Maps the shared detected kind to the host <see cref="DbTypeWithSize"/>.</summary>
    private static DbTypeWithSize MapDetected(DetectedImportColumnType type) => type.Kind switch
    {
        ImportColumnKind.Integer => new DbTypeWithSize(DbSimpleType.Integer),
        ImportColumnKind.Numeric => new DbTypeWithSize(DbSimpleType.Numeric) { NumericPrecision = type.LengthOrPrecision, NumericScale = type.Scale },
        ImportColumnKind.Nvarchar => new DbTypeWithSize(DbSimpleType.Nvarchar) { TextLength = type.LengthOrPrecision },
        ImportColumnKind.Date => new DbTypeWithSize(DbSimpleType.Date),
        ImportColumnKind.TimeStamp => new DbTypeWithSize(DbSimpleType.TimeStamp),
        ImportColumnKind.Boolean => new DbTypeWithSize(DbSimpleType.Boolean),
        _ => new DbTypeWithSize(DbSimpleType.Nvarchar) { TextLength = DEFAULT_NVARCHAR_LENGTH }
    };

    /// <summary>Builds the shared column surface from the host header/type plan.</summary>
    public static IReadOnlyList<IImportColumn> ToImportColumns(IReadOnlyList<string> headers, IReadOnlyList<DbTypeWithSize> types)
    {
        var result = new IImportColumn[Math.Max(headers.Count, types.Count)];
        for (int i = 0; i < result.Length; i++)
        {
            string name = i < headers.Count ? headers[i] : $"COL{i}";
            if (i < types.Count)
            {
                DbTypeWithSize type = types[i];
                result[i] = new ImportColumn(
                    name,
                    MapKind(type.DatabaseTypeSimple),
                    type.DatabaseTypeSimple == DbSimpleType.Numeric ? type.NumericPrecision : type.TextLength,
                    type.NumericScale,
                    IsNullable: true);
            }
            else
            {
                result[i] = new ImportColumn(name, ImportColumnKind.Nvarchar, DEFAULT_NVARCHAR_LENGTH);
            }
        }

        return result;
    }

    /// <summary>Maps the host simple type onto the shared <see cref="ImportColumnKind"/>.</summary>
    public static ImportColumnKind MapKind(DbSimpleType simpleType) => simpleType switch
    {
        DbSimpleType.Integer => ImportColumnKind.Integer,
        DbSimpleType.Numeric => ImportColumnKind.Numeric,
        DbSimpleType.Date => ImportColumnKind.Date,
        DbSimpleType.TimeStamp => ImportColumnKind.TimeStamp,
        DbSimpleType.Boolean => ImportColumnKind.Boolean,
        _ => ImportColumnKind.Nvarchar
    };

    public long RowsCount = -1;
    public List<string[]> PreviewRows { get; set; } = [];

    /// <summary>
    /// Host-facing scan entry for callers that already hold a SpreadSheetTasks reader
    /// (e.g. benchmarks). Runs the shared scan and applies the result to this chooser.
    /// </summary>
    public void ExcelTypeDetection(ExcelReaderAbstract excelDataReader, string sheetName, Action<string>? messageAction = null, long timeoutInSec = -1)
    {
        ArgumentNullException.ThrowIfNull(excelDataReader);
        using var source = new SpreadSheetImportSource(excelDataReader, isExclusiveOpen: false);
        SheetScanResult scan = TabularImportScanner.ScanSource(source, sheetName, messageAction, timeoutInSec);
        ApplyScan(scan);
        messageAction?.Invoke("--" + string.Join('|', ColumnTypesBestMatch!.ToList()));
    }
}
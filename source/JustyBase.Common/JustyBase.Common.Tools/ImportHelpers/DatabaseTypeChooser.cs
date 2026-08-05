using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginCommons;
using SpreadSheetTasks;
using System.Diagnostics;
using System.Globalization;

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

    private ImportTypeAnalyzer? _analyzer;

    private int _fieldCount;
    public void InitTypes(int fieldCount)
    {
        _fieldCount = fieldCount;
        ColumnTypesBestMatch = new DbTypeWithSize[fieldCount];
        DetectedColumnTypes = new DbTypeWithSize[fieldCount];
        RawValueLengths = new int[fieldCount];
        ValidationErrors.Clear();
        NormalizedColumnHeaderNames = new string[fieldCount];
        OriginalColumnHeaderNames = new string[fieldCount];
        _analyzer = new ImportTypeAnalyzer(fieldCount, inferBoolean: true);
    }

    public void ChooseTypes(int textMargin = 5)
    {
        if (NormalizedColumnHeaderNames is null)
            throw new InvalidOperationException("ColumnHeadersNames should be not null");

        if (OriginalColumnHeaderNames is null)
            throw new InvalidOperationException("_originalColumnHeadersNames should be not null");

        if (ColumnTypesBestMatch is null)
            throw new InvalidOperationException("ColumnTypesBestMatch should be not null");

        if (_analyzer is null)
            throw new InvalidOperationException("_analyzer should be not null");

        IReadOnlyList<DetectedImportColumnType> detected = _analyzer.Choose(OriginalColumnHeaderNames);
        for (int i = 0; i < _fieldCount; i++)
        {
            if (OriginalColumnHeaderNames[i].EndsWith("_#TEXT", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Nvarchar)
                {
                    TextLength = GetTextLength(i)
                };
                continue;
            }
            else if (OriginalColumnHeaderNames[i].EndsWith("_#NUMERIC", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Numeric) { NumericPrecision = 20, NumericScale = 6 };
                continue;
            }
            else if (OriginalColumnHeaderNames[i].EndsWith("_#INTEGER", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Integer);
                continue;
            }
            else if (OriginalColumnHeaderNames[i].EndsWith("_#DATE", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.Date);
                continue;
            }
            else if (OriginalColumnHeaderNames[i].EndsWith("_#TIMESTAMP", StringComparison.Ordinal))
            {
                ColumnTypesBestMatch[i] = new DbTypeWithSize(DbSimpleType.TimeStamp);
                continue;
            }

            ColumnTypesBestMatch[i] = MapDetected(detected[i]);
        }

        DetectedColumnTypes = ColumnTypesBestMatch.Select(CloneType).ToArray();
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

    private static ImportColumnKind MapSimpleType(DbSimpleType type) => type switch
    {
        DbSimpleType.Integer => ImportColumnKind.Integer,
        DbSimpleType.Numeric => ImportColumnKind.Numeric,
        DbSimpleType.Nvarchar => ImportColumnKind.Nvarchar,
        DbSimpleType.Date => ImportColumnKind.Date,
        DbSimpleType.TimeStamp => ImportColumnKind.TimeStamp,
        DbSimpleType.Boolean => ImportColumnKind.Boolean,
        _ => ImportColumnKind.NoInfo
    };

    /// <summary>XML import path: feeds the caller-classified cell to the shared analyzer.</summary>
    public void HandleValueTextMode(ReadOnlySpan<char> val, int columnNumber, DbSimpleType dbSimpleType)
    {
        if (_analyzer is null)
            throw new InvalidOperationException("_analyzer is null");

        _analyzer.AddCell(columnNumber, MapSimpleType(dbSimpleType));
    }

    /// <summary>
    /// CSV/Excel path: converts the typed cell to a canonical string and feeds the shared
    /// chooser. CSV cells are fed as their raw token by the caller (leading zeros stay textual).
    /// </summary>
    private void HandleExcelValue(ref FieldInfo nativeVal, int columnNumber)
    {
        if (_analyzer is null)
            throw new InvalidOperationException("_analyzer is null");

        string? canonical = nativeVal.type switch
        {
            ExcelDataType.Int64 => nativeVal.int64Value.ToString(CultureInfo.InvariantCulture),
            ExcelDataType.Double => nativeVal.doubleValue.ToString("R", CultureInfo.InvariantCulture),
            ExcelDataType.DateTime => nativeVal.dtValue.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ExcelDataType.String => nativeVal.strValue,
            ExcelDataType.Boolean => nativeVal.boolValue ? "true" : "false",
            _ => null
        };
        if (canonical is not null)
            _analyzer.AddValue(columnNumber, canonical);
    }

    public long RowsCount = -1;
    public List<string[]> PreviewRows { get; set; } = [];
    public void ExcelTypeDetection(ExcelReaderAbstract excelDataReader, string sheetName, Action<string>? messageAction = null, long timeoutInSec = -1)
    {
        excelDataReader.ActualSheetName = sheetName;
        if (excelDataReader is not CsvReader)
        {
            excelDataReader.Read(); //skip headers 
        }
        int columnCount = excelDataReader.FieldCount;

        InitTypes(columnCount);
        if (NormalizedColumnHeaderNames is null)
            throw new InvalidOperationException("ColumnHeadersNames is null");
        if (OriginalColumnHeaderNames is null)
            throw new InvalidOperationException("OriginalColumnHeadersNames is null");
        if (ColumnTypesBestMatch is null)
            throw new InvalidOperationException("ColumnTypesBestMatch is null");
        if (_analyzer is null)
            throw new InvalidOperationException("_analyzer is null");

        for (int i = 0; i < _fieldCount; i++)
        {
            OriginalColumnHeaderNames[i] = excelDataReader.GetName(i);
            NormalizedColumnHeaderNames[i] = OriginalColumnHeaderNames[i].NormalizeDbColumnName();
        }

        var timestampBeforeLongLoop = Stopwatch.GetTimestamp();
        Stopwatch messageStopwatch = Stopwatch.StartNew();
        bool analyseIncomplete = false;
        if (excelDataReader is CsvReader csv)
        {
            csv.TransformValuesAutomaticly = false;
            while (csv.Read())
            {
                RowsCount++;
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    if (RawValueLengths is not null)
                        RawValueLengths[columnIndex] = Math.Max(RawValueLengths[columnIndex], csv.GetSpanLength(columnIndex));

                    csv.TransFromSpanValue(columnIndex);
                    _analyzer.AddValue(columnIndex, csv.GetString(columnIndex));

                    if (RowsCount < 5)
                    {
                        if (columnIndex == 0)
                        {
                            PreviewRows.Add(new string[columnCount]);
                        }
                        PreviewRows[(int)RowsCount][columnIndex] = csv.GetString(columnIndex);
                    }
                }
                if (RowsCount > 0 && RowsCount % 50_000 == 0 && messageStopwatch.ElapsedMilliseconds > 1_000)
                {
                    messageAction?.Invoke($"{csv.RelativePositionInStream():P1} / ({RowsCount:N0} rows) analysed");
                    if (timeoutInSec != -1 && messageStopwatch.Elapsed.Seconds > timeoutInSec && messageStopwatch.Elapsed.Seconds >= 10)
                    {
                        messageAction?.Invoke($"analysed stopped ! (timout of {timeoutInSec:N0} sec)");
                        RowsCount = -1;
                        analyseIncomplete = true;
                        break;
                    }
                    messageStopwatch.Restart();
                }
            }
            csv.TransformValuesAutomaticly = true;
        }
        else
        {
            while (excelDataReader.Read())
            {
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    if (RawValueLengths is not null)
                        RawValueLengths[columnIndex] = Math.Max(RawValueLengths[columnIndex], excelDataReader.GetString(columnIndex)?.Length ?? 0);
                    ref var nativeVal = ref excelDataReader.GetNativeValue(columnIndex);
                    HandleExcelValue(ref nativeVal, columnIndex);
                }
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(timestampBeforeLongLoop).Milliseconds;
        messageAction?.Invoke($"type analysis took {elapsed} ms");
        ChooseTypes(analyseIncomplete ? 100 : 5);
        messageAction?.Invoke("--" + string.Join('|', ColumnTypesBestMatch.ToList()));
    }
}

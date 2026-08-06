using JustyBase.ImportExport.Import;
using SpreadSheetTasks;
using System.Data;
using System.Globalization;

namespace JustyBase.Common.Tools.ImportHelpers;

/// <summary>
/// Host <see cref="IImportSource"/> over the SpreadSheetTasks <see cref="ExcelReaderAbstract"/>
/// facade (shared CSV reader or native Excel readers). Excel cells are projected to the canonical
/// string the shared analyzer consumes; the typed reader keeps the native conversion.
/// </summary>
public sealed class SpreadSheetImportSource(ExcelReaderAbstract reader, bool isExclusiveOpen) : IImportSource
{
    private readonly ExcelReaderAbstract _reader = reader;
    private readonly bool _isExclusiveOpen = isExclusiveOpen;

    public string? FilePath { get; init; }

    public bool IsCsvSource => _reader is CsvReader;

    public bool IsExclusiveOpen => _isExclusiveOpen;

    public string? ActualSheetName
    {
        get => _reader.ActualSheetName;
        set => _reader.ActualSheetName = value;
    }

    public bool TreatAllColumnsAsText
    {
        get => _reader.TreatAllColumnsAsText;
        set => _reader.TreatAllColumnsAsText = value;
    }

    public int FieldCount => _reader.FieldCount;

    public IReadOnlyList<string> GetSheetNames() => _reader.GetSheetNames();

    public string? GetName(int column) => _reader.GetName(column);

    public bool Read() => _reader.Read();

    public string? GetCellText(int column)
    {
        if (_reader is CsvReader)
        {
            return _reader.GetString(column);
        }

        ref FieldInfo nativeVal = ref _reader.GetNativeValue(column);
        return nativeVal.type switch
        {
            ExcelDataType.Int64 => nativeVal.int64Value.ToString(CultureInfo.InvariantCulture),
            ExcelDataType.Double => nativeVal.doubleValue.ToString("R", CultureInfo.InvariantCulture),
            ExcelDataType.DateTime => nativeVal.dtValue.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ExcelDataType.String => nativeVal.strValue,
            ExcelDataType.Boolean => nativeVal.boolValue ? "true" : "false",
            _ => null
        };
    }

    public int GetRawLength(int column)
    {
        if (_reader is CsvReader csv)
        {
            return csv.GetSpanLength(column);
        }

        return _reader.GetString(column)?.Length ?? 0;
    }

    public double ReadProgress => _reader.RelativePositionInStream();

    public IDataReader CreateTypedReader(IReadOnlyList<ImportColumnKind> kinds, IReadOnlyList<string> normalizedHeaders)
        => new DataReaderFromExcelReaderAbstract(_reader, kinds, normalizedHeaders);

    public void Dispose() => _reader.Dispose();
}

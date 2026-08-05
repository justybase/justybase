using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using SpreadSheetTasks;
using System.Text;

namespace JustyBase.Common.Tools.ImportHelpers;

/// <summary>
/// Host adapter over the shared <see cref="CsvRowReader"/>. Keeps the
/// <see cref="ExcelReaderAbstract"/> facade (typed <see cref="FieldInfo"/> rows,
/// progress, shared-string/update-mode-agnostic open signature).
/// </summary>
public sealed class CsvReader(CompressionEnum csvCompression = CompressionEnum.None) : ExcelReaderAbstract, IDisposable
{
    public string? FilePath { get; set; }

    private readonly CsvRowReader _inner = new(Map(csvCompression));
    private readonly CompressionEnum _csvCompression = csvCompression;
    public CompressionEnum Compression => _csvCompression;

    private decimal[]? _decimalVals;
    private bool[]? _isDecimalArray;

    private static CsvCompression Map(CompressionEnum value) => value switch
    {
        CompressionEnum.Brotli => CsvCompression.Brotli,
        CompressionEnum.Gzip => CsvCompression.Gzip,
        CompressionEnum.Zstd => CsvCompression.Zstd,
        _ => CsvCompression.None
    };

    public override void Open(string path, bool readSharedStrings = true, bool updateMode = false, Encoding? encoding = null)
    {
        _inner.TreatAllColumnsAsText = TreatAllColumnsAsText;
        _inner.Open(path);
        FilePath = path;

        FieldCount = _inner.FieldCount;
        innerRow = new FieldInfo[FieldCount];
        _decimalVals = new decimal[FieldCount];
        _isDecimalArray = new bool[FieldCount];
        for (int i = 0; i < FieldCount; i++)
        {
            innerRow[i].type = ExcelDataType.String;
            innerRow[i].strValue = _inner.GetName(i);
        }
    }

    public override string[] GetSheetNames()
    {
        return [Path.GetFileName(FilePath ?? string.Empty).Replace('.', '_')];
    }

    public bool TransformValuesAutomaticly { get; set; } = true;

    public override bool Read()
    {
        bool innerReaderRead = _inner.Read();
        if (innerReaderRead && TransformValuesAutomaticly)
        {
            for (int i = 0; i < _inner.FieldCount; i++)
            {
                TransFromSpanValue(i);
            }
        }
        return innerReaderRead;
    }

    public void TransFromSpanValue(int i)
    {
        CsvCell cell = _inner.InferCell(i);
        ref var w = ref innerRow[i];
        switch (cell.Kind)
        {
            case CsvCellKind.Null:
                w.type = ExcelDataType.Null;
                break;
            case CsvCellKind.String:
                w.type = ExcelDataType.String;
                w.strValue = cell.StringValue;
                break;
            case CsvCellKind.Double:
                w.type = ExcelDataType.Double;
                w.doubleValue = (double)cell.DecimalValue;
                _isDecimalArray![i] = true;
                _decimalVals![i] = cell.DecimalValue;
                break;
            case CsvCellKind.Int64:
                w.type = ExcelDataType.Int64;
                w.int64Value = cell.Int64Value;
                break;
            case CsvCellKind.DateTime:
                w.type = ExcelDataType.DateTime;
                w.dtValue = cell.DateTimeValue;
                break;
            case CsvCellKind.Boolean:
                w.type = ExcelDataType.Boolean;
                w.boolValue = cell.BooleanValue;
                break;
        }
    }

    public override string GetString(int i)
    {
        ref var w = ref innerRow[i];
        if (w.type == ExcelDataType.String)
        {
            return w.strValue;
        }
        else
        {
            return _inner.GetFieldString(i);
        }
    }

    public int GetSpanLength(int i) => _inner.GetFieldLength(i);
    public decimal GetDecimal(int i) => _decimalVals![i];
    public bool IsDecimal(int i) => _isDecimalArray![i] == true;

    public override void Dispose() => _inner.Dispose();

    public override double RelativePositionInStream() => _inner.Position;
}
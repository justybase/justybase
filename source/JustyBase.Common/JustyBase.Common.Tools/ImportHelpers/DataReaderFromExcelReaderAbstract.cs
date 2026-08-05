using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Contracts;
using SpreadSheetTasks;
using System.Data;
using System.Globalization;

namespace JustyBase.Common.Tools.ImportHelpers;

public sealed class DataReaderFromExcelReaderAbstract : IDataReader
{
    private readonly ExcelReaderAbstract _excelAbstractReader;
    private readonly DatabaseTypeChooser _databaseTypeChooser;
    private readonly bool _isCsvReader;
    private readonly CsvReader? _csvReader;//special case becouse of that excel cannot store decimals but Csv can..
    public DataReaderFromExcelReaderAbstract(ExcelReaderAbstract excelReader, DatabaseTypeChooser databaseTypeChooser)
    {
        ArgumentNullException.ThrowIfNull(excelReader, nameof(excelReader));
        ArgumentNullException.ThrowIfNull(databaseTypeChooser, nameof(databaseTypeChooser));
        _excelAbstractReader = excelReader;
        _isCsvReader = _excelAbstractReader is CsvReader;
        if (_isCsvReader)
        {
            _csvReader = _excelAbstractReader as CsvReader;
        }
        _databaseTypeChooser = databaseTypeChooser;
    }

    public object this[int i] => _excelAbstractReader.GetValue(i);

    public object this[string name] => throw new NotImplementedException();

    public int Depth => throw new NotImplementedException();

    private bool _isClosed;
    public bool IsClosed => _isClosed;

    public int RecordsAffected => throw new NotImplementedException();

    public int FieldCount => _excelAbstractReader.FieldCount;

    public void Close()
    {
        _isClosed = true;
    }

    public void Dispose()
    {
        Close();
    }

    public bool GetBoolean(int i)
    {
        ref var w = ref _excelAbstractReader.GetNativeValue(i);
        if (w.type == ExcelDataType.Boolean)
        {
            return w.boolValue;
        }
        else if (w.type == ExcelDataType.Int64)
        {
            return w.int64Value switch
            {
                0 => false,
                1 => true,
                _ => throw new FormatException($"'{GetString(i)}' is not a supported boolean value.")
            };
        }
        else if (w.type == ExcelDataType.Int32)
        {
            return w.int32Value switch
            {
                0 => false,
                1 => true,
                _ => throw new FormatException($"'{GetString(i)}' is not a supported boolean value.")
            };
        }
        else
        {
            if (bool.TryParse(GetString(i), out bool parsed))
                return parsed;
            throw new FormatException($"'{GetString(i)}' is not a supported boolean value.");
        }
    }

    public byte GetByte(int i)
    {
        return Convert.ToByte(_excelAbstractReader.GetValue(i));
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        throw new NotImplementedException();
    }

    public char GetChar(int i)
    {
        return Convert.ToChar(_excelAbstractReader.GetValue(i));
    }

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
    {
        throw new NotImplementedException();
    }

    public IDataReader GetData(int i)
    {
        throw new NotImplementedException();
    }

    public string GetDataTypeName(int i)
    {
        return _databaseTypeChooser.GetNativeType(i).ToString();
    }

    public DateTime GetDateTime(int i)
    {
        ref var value = ref _excelAbstractReader.GetNativeValue(i);
        if (value.type == ExcelDataType.DateTime)
            return _excelAbstractReader.GetDateTime(i);

        string raw = GetString(i);
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime currentCulture))
            return currentCulture;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime invariant))
            return invariant;
        throw new FormatException($"'{raw}' is not a valid date or timestamp.");
    }

    public decimal GetDecimal(int i)
    {
        if (_isCsvReader)//special case becouse of that excel connot store decimals but Csv can..
        {
            if (_csvReader!.IsDecimal(i))
                return _csvReader.GetDecimal(i);

            return ParseDecimal(GetString(i));
        }
        else
        {
            ref var value = ref _excelAbstractReader.GetNativeValue(i);
            return value.type switch
            {
                ExcelDataType.Int64 => value.int64Value,
                ExcelDataType.Int32 => value.int32Value,
                ExcelDataType.Double => (decimal)value.doubleValue,
                _ => ParseDecimal(GetString(i))
            };
        }
    }

    public double GetDouble(int i)
    {
        return _excelAbstractReader.GetDouble(i);
    }

    public Type GetFieldType(int i)
    {
        return _databaseTypeChooser.GetNativeType(i);
    }

    public float GetFloat(int i)
    {
        return (float)_excelAbstractReader.GetDouble(i);
    }

    public Guid GetGuid(int i)
    {
        throw new NotImplementedException();
    }

    public short GetInt16(int i)
    {
        return (short)_excelAbstractReader.GetInt32(i);
    }

    public int GetInt32(int i)
    {
        return _excelAbstractReader.GetInt32(i);
    }

    public long GetInt64(int i)
    {
        ref var value = ref _excelAbstractReader.GetNativeValue(i);
        if (_isCsvReader && _csvReader!.IsDecimal(i))
            return ToInt64(_csvReader.GetDecimal(i), GetString(i));

        return value.type switch
        {
            ExcelDataType.Int64 => value.int64Value,
            ExcelDataType.Int32 => value.int32Value,
            ExcelDataType.Double => ToInt64(value.doubleValue, GetString(i)),
            _ => ParseInt64(GetString(i))
        };
    }

    public string GetName(int i)
    {
        return _databaseTypeChooser!.NormalizedColumnHeaderNames![i];
    }

    public int GetOrdinal(string name)
    {
        return Array.IndexOf(_databaseTypeChooser!.NormalizedColumnHeaderNames!, name);
    }

    public DataTable? GetSchemaTable()
    {
        return null;
    }

    public string GetString(int i)
    {
        return _excelAbstractReader.GetString(i);
    }

    public object GetValue(int i)
    {
        if (IsDBNull(i))
            return DBNull.Value;

        return _databaseTypeChooser!.ColumnTypesBestMatch![i].DatabaseTypeSimple switch
        {
            DbSimpleType.Integer => GetInt64(i),
            DbSimpleType.Numeric => GetDecimal(i),
            DbSimpleType.Nvarchar => GetString(i),
            DbSimpleType.Date => GetDateTime(i).Date,
            DbSimpleType.TimeStamp => GetDateTime(i),
            DbSimpleType.NoInfo => GetString(i),
            DbSimpleType.Boolean => GetBoolean(i),
            _ => GetString(i),
        };
    }

    public int GetValues(object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = GetValue(i);
        }
        return values.Length;
    }

    public bool IsDBNull(int i)
    {
        ref var valTmp = ref _excelAbstractReader.GetNativeValue(i);
        return valTmp.type == ExcelDataType.Null;
    }

    private static decimal ParseDecimal(string raw)
    {
        if (decimal.TryParse(raw, ImportEssentials.NumberExcelStyle, CultureInfo.CurrentCulture, out decimal currentCulture))
            return currentCulture;
        if (decimal.TryParse(raw, ImportEssentials.NumberExcelStyle, CultureInfo.InvariantCulture, out decimal invariant))
            return invariant;
        throw new FormatException($"'{raw}' is not a valid numeric value.");
    }

    private static long ParseInt64(string raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out long currentCulture))
            return currentCulture;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long invariant))
            return invariant;
        decimal decimalValue = ParseDecimal(raw);
        return ToInt64(decimalValue, raw);
    }

    private static long ToInt64(double value, string raw)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Truncate(value)
            || value < long.MinValue || value > long.MaxValue)
            throw new FormatException($"'{raw}' is not an integer value.");
        return checked((long)value);
    }

    private static long ToInt64(decimal value, string raw)
    {
        if (decimal.Truncate(value) != value || value < long.MinValue || value > long.MaxValue)
            throw new FormatException($"'{raw}' is not an integer value.");
        return checked((long)value);
    }

    public bool NextResult()
    {
        throw new NotImplementedException();
    }

    public bool Read()
    {
        if (!_isClosed)
        {
            _isClosed = !_excelAbstractReader.Read();
        }
        return !_isClosed;
    }
}

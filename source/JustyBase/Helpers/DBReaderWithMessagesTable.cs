using JustyBase.Models;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace JustyBase.Helpers;

internal sealed class DBReaderWithMessagesTable : DbDataReader
{
    private readonly Action<int>? _action;
    private readonly IProgress<int>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly BulkObservableCollection<TableRow> _rows;
    private readonly List<string> _headers;
    private readonly int _fieldCount;
    private readonly int _rowsCnt;
    private readonly string[] _typeNames;
    private readonly TypeCode[] _typeCodes;
    public DBReaderWithMessagesTable(
        TableOfSqlResults table,
        Action<int>? action = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _rows = table.FilteredRows;
        _headers = table.Headers;

        _action = action;
        _progress = progress;
        _cancellationToken = cancellationToken;
        _fieldCount = _headers.Count;
        _rowsCnt = _rows.Count;
        if (_rows.Count > 0)
        {
            currentRow = _rows[0].Fields;
        }
        _typeNames = new string[_fieldCount];
        _typeCodes = new TypeCode[_fieldCount];

        for (int i = 0; i < _fieldCount; i++)
        {
            _typeNames[i] = table.TypeCodes[i].ToString();
            _typeCodes[i] = table.TypeCodes[i];
        }
    }

    // The mapping is resolved through the shared helper so the trimmer can
    // statically verify the DAM contract: every arm returns a statically
    // known typeof(...).

    private int currentRowNum = -1;
    private object[] currentRow;

    public override DataTable? GetSchemaTable()
    {
        return null;
    }

    public override object this[int ordinal] => _rows[currentRowNum].Fields[ordinal];

    public override object this[string name] => throw new NotImplementedException();

    public override int Depth => throw new NotImplementedException();

    public override int FieldCount => _fieldCount;

    public override bool HasRows => _rowsCnt > 0;

    public override bool IsClosed => currentRowNum > _rowsCnt;

    public override int RecordsAffected => throw new NotImplementedException();

    public override bool GetBoolean(int ordinal)
    {
        return (bool)currentRow[ordinal];
    }

    public override byte GetByte(int ordinal)
    {
        return (byte)currentRow[ordinal];
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
    }

    public override char GetChar(int ordinal)
    {
        return (char)currentRow[ordinal];
    }

    public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
    }

    public override string GetDataTypeName(int ordinal)
    {
        return _typeNames[ordinal];
    }

    public override DateTime GetDateTime(int ordinal)
    {
        return (DateTime)currentRow[ordinal];
    }

    public override decimal GetDecimal(int ordinal)
    {
        return (Decimal)currentRow[ordinal];
    }

    public override double GetDouble(int ordinal)
    {
        return (Double)currentRow[ordinal];
    }

    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
        return ResultFieldTypeMapper.FromTypeCode(_typeCodes[ordinal]);
    }

    public override float GetFloat(int ordinal)
    {
        return (float)currentRow[ordinal];
    }

    public override Guid GetGuid(int ordinal)
    {
        return (Guid)currentRow[ordinal];
    }

    public override short GetInt16(int ordinal)
    {
        return (short)currentRow[ordinal];
    }

    public override int GetInt32(int ordinal)
    {
        return (int)currentRow[ordinal];
    }

    public override long GetInt64(int ordinal)
    {
        return (long)currentRow[ordinal];
    }

    public override string GetName(int ordinal)
    {
        return _headers[ordinal];
    }

    public override int GetOrdinal(string name)
    {
        throw new NotSupportedException();
    }

    public override string GetString(int ordinal)
    {
        return (string)currentRow[ordinal];
    }

    public override object GetValue(int ordinal)
    {
        return currentRow[ordinal];
    }

    public override int GetValues(object[] values)
    {
        for (int i = 0; i < _fieldCount; i++)
        {
            values[i] = currentRow[i] ?? DBNull.Value;
        }
        return _fieldCount;
    }

    public override bool IsDBNull(int ordinal)
    {
        var val = currentRow[ordinal];
        return val == null || val == DBNull.Value;
    }

    public override bool NextResult() => false;

    private int _readedRowNumber;
    public override bool Read()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (++_readedRowNumber % 10_000 == 0)
        {
            _action?.Invoke(_readedRowNumber);
            _progress?.Report(_readedRowNumber);
        }

        ++currentRowNum;
        bool res = currentRowNum < _rowsCnt;
        if (res)
        {
            currentRow = _rows[currentRowNum].Fields;
        }

        return res;
    }

    public override IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}

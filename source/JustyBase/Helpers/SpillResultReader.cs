using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using JustyBase.Services.Results;

namespace JustyBase.Helpers;

/// <summary>
/// Streams a spilled result as a <see cref="DbDataReader"/> so the existing
/// CSV, Excel, JSON, XML, TSV, Parquet and SQL export writers can consume every
/// filtered row instead of only the page currently bound to the grid.
///
/// The reader follows the sort order and filter that were active when it was
/// created. It never owns the spill store; disposing it only closes the reader.
/// </summary>
internal sealed class SpillResultReader : DbDataReader
{
    private const int MinPageSize = 1_000;
    private const int MaxPageSize = 100_000;

    private readonly ResultSpillStore _store;
    private readonly IReadOnlyList<SpillSortKey> _sortKeys;
    private readonly SpillFilter? _filter;
    private readonly string[] _headers;
    private readonly TypeCode[] _typeCodes;
    private readonly int _fieldCount;
    private readonly int _pageSize;

    private IReadOnlyList<object?[]> _page = [];
    private int _pageIndex;
    private int _rowIndex;
    private object?[]? _currentRow;
    private bool? _hasRows;
    private bool _closed;

    public SpillResultReader(ResultSpillStore store, IReadOnlyList<SpillSortKey>? sortKeys, SpillFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _sortKeys = sortKeys ?? [];
        _filter = filter;
        _fieldCount = store.Schema.Columns.Count;
        _pageSize = Math.Clamp(store.PageSize, MinPageSize, MaxPageSize);
        _headers = new string[_fieldCount];
        _typeCodes = new TypeCode[_fieldCount];

        for (int i = 0; i < _fieldCount; i++)
        {
            ResultSpillColumn column = store.Schema.Columns[i];
            _headers[i] = column.Name;
            _typeCodes[i] = column.TypeCode;
        }
    }

    public override int Depth => 0;

    public override int FieldCount => _fieldCount;

    public override bool HasRows => !_closed && (_hasRows ??= LoadNextPage());

    public override bool IsClosed => _closed;

    public override int RecordsAffected => -1;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_closed)
        {
            return false;
        }

        if (_rowIndex >= _page.Count && !LoadNextPage())
        {
            _currentRow = null;
            return false;
        }

        _currentRow = _page[_rowIndex++];
        return true;
    }

    private bool LoadNextPage()
    {
        IReadOnlyList<object?[]> page = _store.ReadPageWithFilter(
            _pageIndex,
            _pageSize,
            _sortKeys,
            _filter);
        if (page.Count == 0)
        {
            _page = [];
            _rowIndex = 0;
            return false;
        }

        _pageIndex++;
        _page = page;
        _rowIndex = 0;
        return true;
    }

    public override bool NextResult() => false;

    public override DataTable? GetSchemaTable() => null;

    public override string GetName(int ordinal) => _headers[ordinal];

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _headers.Length; i++)
        {
            if (string.Equals(_headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentException($"Column '{name}' was not found in the spilled result.", nameof(name));
    }

    public override string GetDataTypeName(int ordinal) => _typeCodes[ordinal].ToString();

    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) => ResultFieldTypeMapper.FromTypeCode(_typeCodes[ordinal]);

    public override object GetValue(int ordinal) => _currentRow?[ordinal] ?? DBNull.Value;

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        int count = Math.Min(values.Length, _fieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal)
    {
        object? value = _currentRow?[ordinal];
        return value is null or DBNull;
    }

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

    public override char GetChar(int ordinal) => (char)GetValue(ordinal);

    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (GetValue(ordinal) is not byte[] bytes)
        {
            return 0;
        }

        if (buffer is null)
        {
            return bytes.Length;
        }

        if (dataOffset < 0 || dataOffset > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(dataOffset));
        }

        int available = bytes.Length - (int)dataOffset;
        int count = Math.Min(length, available);
        Array.Copy(bytes, dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        if (GetValue(ordinal) is not string text)
        {
            return 0;
        }

        if (buffer is null)
        {
            return text.Length;
        }

        if (dataOffset < 0 || dataOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(dataOffset));
        }

        int available = text.Length - (int)dataOffset;
        int count = Math.Min(length, available);
        text.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    protected override void Dispose(bool disposing)
    {
        _closed = true;
        _currentRow = null;
        _page = [];
        base.Dispose(disposing);
    }
}

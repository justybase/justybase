using JustyBase.Converters;

namespace JustyBase.Models;

public sealed class SearchInRows
{
    private readonly int[] _mainSearchCompatybileColumns;
    private readonly TypeCode[] _typeCodes;
    private readonly int _colCount;
    private readonly List<TableRow> _rows;
    private readonly int _rowsCount;
    private readonly TableOfSqlResults _currentResultsTable;
    private readonly Dictionary<int, AditionalOneFilter> _additionalValues;
    private readonly bool _containsGeneralSearch;
    private readonly string _searchText;
    public SearchInRows(TableOfSqlResults currentResultsTable, string searchText, Dictionary<int, AditionalOneFilter> additionalValues, bool containsGeneralSearch)
    {
        _searchText = searchText;
        _containsGeneralSearch = containsGeneralSearch;
        _additionalValues = additionalValues;
        _currentResultsTable = currentResultsTable;
        _rows = _currentResultsTable.Rows;
        _rowsCount = _currentResultsTable.Rows.Count;
        _colCount = _currentResultsTable.Headers.Count;
        _mainSearchCompatybileColumns = new int[_currentResultsTable.Headers.Count];

        _typeCodes = _currentResultsTable.TypeCodes.ToArray();

        Array.Fill(_mainSearchCompatybileColumns, 1);

        if (!Int64.TryParse(searchText, out _))
        {
            for (int index = 0; index < _mainSearchCompatybileColumns.Length; index++)
            {
                _mainSearchCompatybileColumns[index] = _currentResultsTable.TypeCodes[index] switch
                {
                    TypeCode.SByte => 0,
                    TypeCode.Byte => 0,
                    TypeCode.Int16 => 0,
                    TypeCode.Int32 => 0,
                    TypeCode.Int64 => 0,
                    _ => _mainSearchCompatybileColumns[index]
                };
            }
        }

        if (!decimal.TryParse(searchText, out _))
        {
            for (int index = 0; index < _mainSearchCompatybileColumns.Length; index++)
            {
                _mainSearchCompatybileColumns[index] = _currentResultsTable.TypeCodes[index] switch
                {
                    TypeCode.Single => 0,
                    TypeCode.Double => 0,
                    TypeCode.Decimal => 0,
                    _ => _mainSearchCompatybileColumns[index]
                };
            }
        }
    }

    private const int ParallelSearchThreadCount = 4;

    public void SearchAll()
    {
        List<TableRow> matches;
        if (_rowsCount <= 250_000) // for small number of rows its not worth (sorting issue)
        {
            matches = new List<TableRow>();
            SearchInRowsLocal(0, _rowsCount, matches);
        }
        else
        {
            int threadCount = ParallelSearchThreadCount;
            var partitions = new List<TableRow>[threadCount];
            Task[] tasks = new Task[threadCount];

            for (int num = 1; num <= threadCount; num++)
            {
                int x = num;
                int partitionIndex = x - 1;
                var partitionMatches = new List<TableRow>();
                partitions[partitionIndex] = partitionMatches;
                int rangeStart = (partitionIndex * _rowsCount) / threadCount;
                int rangeEnd = (x * _rowsCount) / threadCount;
                tasks[partitionIndex] = Task.Run(() => SearchInRowsLocal(rangeStart, rangeEnd, partitionMatches));
            }

            Task.WaitAll(tasks);

            int total = 0;
            for (int i = 0; i < threadCount; i++)
            {
                total += partitions[i].Count;
            }

            matches = new List<TableRow>(total);
            for (int i = 0; i < threadCount; i++)
            {
                matches.AddRange(partitions[i]);
            }
        }

        // Single Reset notification — callers should suspend grid binding first.
        _currentResultsTable.FilteredRows.ReplaceAll(matches);
        _currentResultsTable.RebuildRowIndexMap();
    }

    private void SearchInRowsLocal(int A, int B, ICollection<TableRow> matches)
    {
        Span<char> charBuffer = stackalloc char[50];
        for (int i = A; i < B; i++)
        {
            bool founded = false;
            var currentRow = _rows[i];
            var fields = currentRow.Fields;

            bool columnFilterResult = true;

            foreach (var (columnIndex, itemValue) in _additionalValues)
            {
                object actualValue = fields[columnIndex];

                var res = itemValue.GetComparisionResultGeneral(_currentResultsTable.TypeCodes[columnIndex], actualValue);
                if (!res)
                {
                    columnFilterResult = false;
                    break;
                }
                else
                {
                    //columnFilterResult == true;
                }

                // filterCheck == true
                if (itemValue.NotList?.Count > 0 && (itemValue.NotList.Contains(actualValue)))
                {
                    columnFilterResult = false;
                    break;
                }
                if (itemValue.InList?.Count > 0 && !itemValue.InList.Contains(actualValue))
                {
                    columnFilterResult = false;
                    break;
                }
            }

            if (columnFilterResult)
            {
                if (String.IsNullOrWhiteSpace(_searchText))
                {
                    founded = true;
                }
                else
                {
                    for (int j = 0; j < _colCount; j++)
                    {
                        if (_mainSearchCompatybileColumns[j] != 1)
                        {
                            continue;
                        }
                        var actualVal = fields[j];
                        if (actualVal is not null)
                        {
                            var t = _typeCodes[j];
                            if (t == TypeCode.Int32)
                            {
                                var inty = (int)actualVal;
                                inty.TryFormat(charBuffer, out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
                                if (_containsGeneralSearch && charBuffer[0..written].IndexOf(_searchText) >= 0)
                                {
                                    founded = true;
                                    break;
                                }

                                if (charBuffer[0..written].SequenceEqual(_searchText))
                                {
                                    founded = true;
                                    break;
                                }
                            }
                            else if (t == TypeCode.Int64)
                            {
                                var longy = (long)actualVal;
                                longy.TryFormat(charBuffer, out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
                                if (_containsGeneralSearch && (charBuffer[0..written]).IndexOf(_searchText) >= 0)
                                {
                                    founded = true;
                                    break;
                                }

                                if (charBuffer[0..written].SequenceEqual(_searchText))
                                {
                                    founded = true;
                                    break;
                                }
                            }
                            else if (t == TypeCode.Decimal)
                            {
                                var decy = (decimal)actualVal;
                                decy.TryFormat(charBuffer, out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
                                if (_containsGeneralSearch && (charBuffer[0..written]).IndexOf(_searchText) >= 0)
                                {
                                    founded = true;
                                    break;
                                }

                                if (charBuffer[0..written].SequenceEqual(_searchText))
                                {
                                    founded = true;
                                    break;
                                }
                            }
                            else if (t == TypeCode.DateTime)
                            {
                                var dt = (DateTime)actualVal;
                                dt.TryFormat(charBuffer, out int written, NullValueConverter.datetimeFormat, System.Globalization.CultureInfo.InvariantCulture);
                                if (_containsGeneralSearch && (charBuffer[0..written]).IndexOf(_searchText) >= 0)
                                {
                                    founded = true;
                                    break;
                                }

                                if (charBuffer[0..written].SequenceEqual(_searchText))
                                {
                                    founded = true;
                                    break;
                                }
                            }
                            else
                            {
                                string txt = actualVal.ToString();
                                if (_containsGeneralSearch && txt.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                                {
                                    founded = true;
                                    break;
                                }
                                if (!_containsGeneralSearch && txt.Equals(_searchText, StringComparison.OrdinalIgnoreCase))
                                {
                                    founded = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (founded)
                {
                    matches.Add(currentRow);
                }
            }
        }
    }
}

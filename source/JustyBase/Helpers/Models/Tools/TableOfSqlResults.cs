using System.Collections;
using System.ComponentModel;

namespace JustyBase.Models;
public sealed class TableOfSqlResults
{
    public List<string> Headers { get; set; }
    public List<string> DataTypeNames { get; set; }
    public List<TypeCode> TypeCodes { get; set; }

    public byte[] NumericScales = Array.Empty<byte>();

    public byte GetNumericScale(int index)
    {
        if (index < NumericScales.Length)
        {
            return NumericScales[index];
        }
        return 6;
    }

    public List<TableRow> Rows { get; set; }
    public BulkObservableCollection<TableRow> FilteredRows { get; set; }
    
    // Dictionary for fast row index lookup - O(1) instead of O(n)
    private Dictionary<TableRow, int> _rowIndexMap = new Dictionary<TableRow, int>();

    public sealed class SortInfo
    {
        public int ColNumber { get; set; }
        public ListSortDirection SortDirection { get; set; }
        public IComparer Comparer { get; set; }
    }
    public List<SortInfo> ColumnsToSort { get; set; } = [];
    public void SortFilteredRows()
    {
        var sorted = FilteredRows.ToList();
        sorted.Sort((x, y) =>
        {
            foreach (var cs in ColumnsToSort)
            {
                var resTmp = (cs.SortDirection == ListSortDirection.Descending ? -1 : 1) * cs.Comparer.Compare(x, y);
                if (resTmp != 0)
                {
                    return resTmp;
                }
            }
            return 0;
        });

        FilteredRows.ReplaceAll(sorted);
        RebuildRowIndexMap();
    }

    /// <summary>
    /// Fast O(1) lookup for row index instead of O(n) IndexOf
    /// </summary>
    public int GetRowIndex(TableRow row)
    {
        return _rowIndexMap.TryGetValue(row, out int index) ? index : -1;
    }

    /// <summary>
    /// Rebuilds the row index map for fast lookups
    /// </summary>
    public void RebuildRowIndexMap()
    {
        _rowIndexMap.Clear();
        for (int i = 0; i < FilteredRows.Count; i++)
        {
            _rowIndexMap[FilteredRows[i]] = i;
        }
    }

    public TableOfSqlResults()
    {
        Headers = [];
        Rows = [];
        FilteredRows = new BulkObservableCollection<TableRow>();
    }

    public const int FILTER_ITEMS_LIMIT = 20_000;
    public const string FIELDS_WORD = "Fields";
    public object[] GetAcualPopularValues(int columnIndex)
    {
        HashSet<object> values = [];
        int cnt = FilteredRows.Count;

        for (int i = 0; i < cnt; i++)
        {
            object colVal = FilteredRows[i].Fields[columnIndex];
            values.Add(colVal);
            if (i >= 20_000 && values.Count >= FILTER_ITEMS_LIMIT)
            {
                break;
            }
        }
        var arr = values.ToArray();
        Array.Sort(arr);
        return arr;
    }
    public void DoClear()
    {
        Rows.Clear();
        FilteredRows.Clear();
        _rowIndexMap.Clear();
    }
}

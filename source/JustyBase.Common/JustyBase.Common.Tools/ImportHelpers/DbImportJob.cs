using JustyBase.Common.Tools.ImportHelpers.XML;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginCommons;
using System.Data;
using System.Globalization;

namespace JustyBase.Common.Tools.ImportHelpers;
public class DbImportJob : IDbImportJob
{
    public DbImportJob(IDataReader rdr, DatabaseTypeChooser typeChooser)
    {
        ArgumentNullException.ThrowIfNull(rdr);
        ArgumentNullException.ThrowIfNull(typeChooser);
        AsReader = rdr;
        _databaseTypeChoser = typeChooser;
        _columnHeadersNames = _databaseTypeChoser.NormalizedColumnHeaderNames;
    }

    public DbImportJob() { }

    public long RowsCount => _databaseTypeChoser.RowsCount;

    protected string[]? _columnHeadersNames;
    public string[]? ColumnHeadersNames => _columnHeadersNames;
    public DbTypeWithSize[] ColumnTypesBestMatch => _databaseTypeChoser.ColumnTypesBestMatch ?? [];
    public List<string[]>? PreviewRows => _databaseTypeChoser.PreviewRows;

    protected static readonly CultureInfo _cultureUS = CultureInfo.CreateSpecificCulture("en-US");
    protected readonly DatabaseTypeChooser _databaseTypeChoser = new DatabaseTypeChooser();

    //results 

    protected OneCellValue[][]? _linesX;

    public IDataReader AsReader { get; set; } = null!;

    public string[] ReturnHeadersWithDataTypes(DatabaseTypeEnum databaseType = DatabaseTypeEnum.NetezzaSQL)
    {
        if (ColumnHeadersNames is not null)
        {
            StringExtension.DeDuplicate(ColumnHeadersNames);
        }
        string[] res = new string[ColumnHeadersNames?.Length ?? 0];
        for (int i = 0; i < (ColumnHeadersNames?.Length ?? 0); i++)
        {
            res[i] = ColumnHeadersNames![i] + " " + ColumnTypesBestMatch![i].ToString(databaseType);
        }
        return res;
    }
}


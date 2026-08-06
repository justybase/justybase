using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using System.Data;

namespace JustyBase.Common.Tools.ImportHelpers;

/// <summary>
/// Host adapter over the shared <see cref="IImportJob"/> contract. Keeps the host
/// <see cref="DatabaseTypeChooser"/> (and its <c>DbTypeWithSize</c> type model) for the
/// UI, while exposing the neutral <see cref="IImportColumn"/> surface to the shared
/// pipeline and the plugin seam.
/// </summary>
public class DbImportJob : IImportJob
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
    public string? SourceSheetName { get; init; }

    protected string[]? _columnHeadersNames;
    public IReadOnlyList<string> ColumnHeadersNames => _columnHeadersNames ?? [];
    public DbTypeWithSize[] ColumnTypesBestMatch => _databaseTypeChoser.ColumnTypesBestMatch ?? [];
    public IReadOnlyList<string[]>? PreviewRows => _databaseTypeChoser.PreviewRows;

    protected readonly DatabaseTypeChooser _databaseTypeChoser = new DatabaseTypeChooser();

    public IDataReader AsReader { get; set; } = null!;

    public IReadOnlyList<IImportColumn> Columns
    {
        get
        {
            var headers = ColumnHeadersNames;
            var types = ColumnTypesBestMatch;
            return DatabaseTypeChooser.ToImportColumns(headers, types);
        }
    }

    public string[] ReturnHeadersWithDataTypes(DatabaseKind databaseKind)
    {
        var headers = ColumnHeadersNames;
        var types = ColumnTypesBestMatch;
        var res = new string[headers.Count];
        for (int i = 0; i < headers.Count; i++)
        {
            res[i] = i < types.Length
                ? $"{headers[i]} {types[i].ToString(databaseKind.ToDatabaseTypeEnum())}"
                : headers[i];
        }

        return res;
    }
}

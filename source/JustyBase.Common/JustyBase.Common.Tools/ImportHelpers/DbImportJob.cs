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
            var result = new IImportColumn[Math.Max(headers.Count, types.Length)];
            for (int i = 0; i < result.Length; i++)
            {
                string name = i < headers.Count ? headers[i] : $"COL{i}";
                result[i] = i < types.Length
                    ? ToImportColumn(name, types[i])
                    : new ImportColumn(name, ImportColumnKind.Nvarchar, DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH);
            }

            return result;
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

    internal static ImportColumnKind MapKind(DbSimpleType simpleType) => simpleType switch
    {
        DbSimpleType.Integer => ImportColumnKind.Integer,
        DbSimpleType.Numeric => ImportColumnKind.Numeric,
        DbSimpleType.Date => ImportColumnKind.Date,
        DbSimpleType.TimeStamp => ImportColumnKind.TimeStamp,
        DbSimpleType.Boolean => ImportColumnKind.Boolean,
        _ => ImportColumnKind.Nvarchar
    };

    private static ImportColumn ToImportColumn(string name, DbTypeWithSize type) => new(
        name,
        MapKind(type.DatabaseTypeSimple),
        type.DatabaseTypeSimple == DbSimpleType.Numeric ? type.NumericPrecision : type.TextLength,
        type.NumericScale,
        IsNullable: true);
}

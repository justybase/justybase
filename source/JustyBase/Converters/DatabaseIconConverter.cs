using Avalonia.Media.Imaging;
using JustyBase.Editor.CompletionProviders;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using System.Globalization;

namespace JustyBase.Converters;

public sealed class DatabaseIconConverter : IValueConverter
{
    private readonly Bitmap? _tableIcon16;
    private readonly Bitmap? _columnOneIcon16;
    private readonly Bitmap? _externalGroupIcon16;
    private readonly Bitmap? _viewIcon16;
    private readonly Bitmap? _procIcon16;
    private readonly Bitmap? _synonymIcon16;
    private readonly Bitmap? _schemaIcon16;
    private readonly Bitmap? _databaseIcon16;
    private readonly Bitmap? _defaultIcon;
    private readonly Bitmap? _netezzaIcon16;
    private readonly Bitmap? _oracleIcon16;
    private readonly Bitmap? _db2Icon16;
    private readonly Bitmap? _sqliteIcon16;
    private readonly Bitmap? _duckDbIcon;
    private readonly Bitmap? _mySqlIcon;
    private readonly Bitmap? _msSqlIcon16;
    private readonly Bitmap? _postgreIcon16;
    private readonly Bitmap? _tableGroupIcon16;
    private readonly Bitmap? _viewGroupIcon16;
    private readonly Bitmap? _functuionsGroupIcon16;
    private readonly Bitmap? _synonymGroupIcon16;
    private readonly Bitmap? _procGroupIcon16;
    private readonly Bitmap? _seqGroupIcon16;
    private readonly Bitmap? _aggGroupIcon16;
    private readonly Bitmap? _columnsIcon16;
    private readonly Bitmap? _indexIcon16;
    private readonly Bitmap? _indexGroupIcon16;
    private readonly Bitmap? _partitionIcon16;
    private readonly Bitmap? _partitionGroupIcon16;
    private readonly Bitmap? _fluidGroupIcon16;
    private readonly Bitmap? _distributedIcon16;
    private readonly Bitmap? _refIcon16;

    public DatabaseIconConverter()
    {
        if (App.Current is { } app)
        {
            _databaseIcon16 = app.Resources["GeneralDbBitmap"] as Bitmap;
            _netezzaIcon16 = app.Resources["NetezzaDbBitmap"] as Bitmap ?? _databaseIcon16;
            _oracleIcon16 = app.Resources["OracleDbBitmap"] as Bitmap ?? _databaseIcon16;
            _db2Icon16 = app.Resources["Db2DbBitmap"] as Bitmap ?? _databaseIcon16;
            _sqliteIcon16 = app.Resources["SqliteDbBitmap"] as Bitmap ?? _databaseIcon16;
            _duckDbIcon = app.Resources["DuckDbBitmap"] as Bitmap ?? _databaseIcon16;
            _mySqlIcon = app.Resources["MySqlDbBitmap"] as Bitmap ?? _databaseIcon16;
            _msSqlIcon16 = app.Resources["MsSqlDbBitmap"] as Bitmap ?? _databaseIcon16;
            _postgreIcon16 = app.Resources["PostgreSqlDbBitmap"] as Bitmap ?? _databaseIcon16;

            _tableIcon16 = app.Resources["TableBitmap"] as Bitmap;
            _viewIcon16 = app.Resources["ViewBitmap"] as Bitmap;
            _tableGroupIcon16 = app.Resources["TableGroupBitmap"] as Bitmap;
            _viewGroupIcon16 = app.Resources["ViewGroupBitmap"] as Bitmap;
            _externalGroupIcon16 = app.Resources["ExternalGroupBitmap"] as Bitmap;
            _functuionsGroupIcon16 = app.Resources["FunctionsGroupBitmap"] as Bitmap;
            _synonymGroupIcon16 = app.Resources["SynonymGroupBitmap"] as Bitmap;
            _synonymIcon16 = app.Resources["SynonymBitmap"] as Bitmap;
            _procGroupIcon16 = app.Resources["ProceduresGroupBitmap"] as Bitmap;
            _procIcon16 = app.Resources["ProcedureBitmap"] as Bitmap;
            _seqGroupIcon16 = app.Resources["SequencesGroupBitmap"] as Bitmap;
            _aggGroupIcon16 = app.Resources["AggregatesGroupBitmap"] as Bitmap;
            _columnsIcon16 = app.Resources["ColumnsBitmap"] as Bitmap;
            _columnOneIcon16 = app.Resources["ColumnBitmap"] as Bitmap;
            _indexIcon16 = app.Resources["IndexBitmap"] as Bitmap;
            _indexGroupIcon16 = app.Resources["IndexGroupBitmap"] as Bitmap;
            _partitionIcon16 = app.Resources["PartitionBitmap"] as Bitmap;
            _partitionGroupIcon16 = app.Resources["PartitionGroupBitmap"] as Bitmap;
            _schemaIcon16 = app.Resources["SchemaBitmap"] as Bitmap;
            _fluidGroupIcon16 = app.Resources["FluidGroupBitmap"] as Bitmap;
            _distributedIcon16 = app.Resources["DistributedOnBitmap"] as Bitmap;
            _refIcon16 = app.Resources["ReferencesBitmap"] as Bitmap;
            _defaultIcon = app.Resources["FolderIconBitmap"] as Bitmap;


            // FIX THIS !!
            GlyphExtensions.TableBitmap = _tableIcon16;
            GlyphExtensions.ColumnBitmap = _columnOneIcon16;
            GlyphExtensions.ViewBitmap = _viewIcon16;
            GlyphExtensions.DatabaseBitmap = _databaseIcon16;
            GlyphExtensions.ProcedureBitmap = _procIcon16;
            GlyphExtensions.SynonymBitmap = _synonymIcon16;
            GlyphExtensions.SchemaBitmap = _schemaIcon16;
            GlyphExtensions.ExternalBitmap = _externalGroupIcon16;
            GlyphExtensions.FunctionBitmap = _functuionsGroupIcon16;
        }
    }

    private Bitmap? GetBitmapFromEnum(DatabaseTypeEnum databaseTypeEnum)
    {
        return databaseTypeEnum switch
        {
            DatabaseTypeEnum.NetezzaSQL => _netezzaIcon16,
            DatabaseTypeEnum.DB2 => _db2Icon16,
            DatabaseTypeEnum.Sqlite => _sqliteIcon16,
            DatabaseTypeEnum.DuckDB => _duckDbIcon,
            DatabaseTypeEnum.MySql => _mySqlIcon,
            DatabaseTypeEnum.MsSqlTrusted => _msSqlIcon16,
            DatabaseTypeEnum.PostgreSql => _postgreIcon16,
            DatabaseTypeEnum.Oracle => _oracleIcon16,
            DatabaseTypeEnum.Excel => _duckDbIcon,
            DatabaseTypeEnum.Access => _sqliteIcon16,
            DatabaseTypeEnum.NotSupportedDatabase => _defaultIcon,
            _ => _defaultIcon
        };
    }

    private Bitmap? GetBitmapFromTypeInDatabase(TypeInDatabaseEnum typeInDatabase, DatabaseTypeEnum? connectionType = null)
    {
        return typeInDatabase switch
        {
            TypeInDatabaseEnum.Connection => connectionType is { } ct
                ? GetBitmapFromEnum(ct)
                : _databaseIcon16,
            // DATABASE level always uses the generic database icon,
            // even when the vendor type is known (connection keeps vendor).
            TypeInDatabaseEnum.dbase => _databaseIcon16,
            // SCHEMA level always uses the generic schema icon.
            TypeInDatabaseEnum.Schema => _schemaIcon16,
            TypeInDatabaseEnum.Table => _tableIcon16,
            TypeInDatabaseEnum.View => _viewIcon16,
            TypeInDatabaseEnum.baseTables => _tableGroupIcon16,
            TypeInDatabaseEnum.baseViews => _viewGroupIcon16,
            TypeInDatabaseEnum.baseFluides or TypeInDatabaseEnum.Fluid => _fluidGroupIcon16,
            TypeInDatabaseEnum.baseExternals or TypeInDatabaseEnum.ExternalTable => _externalGroupIcon16,
            TypeInDatabaseEnum.baseSynonyms => _synonymGroupIcon16,
            TypeInDatabaseEnum.Synonym => _synonymIcon16,
            TypeInDatabaseEnum.baseFunctions or TypeInDatabaseEnum.Function => _functuionsGroupIcon16,
            TypeInDatabaseEnum.baseProcedures => _procGroupIcon16,
            TypeInDatabaseEnum.Procedure => _procIcon16,
            TypeInDatabaseEnum.baseSequence or TypeInDatabaseEnum.Sequence => _seqGroupIcon16,
            TypeInDatabaseEnum.baseAggregates => _aggGroupIcon16,
            TypeInDatabaseEnum.baseIndexes => _indexGroupIcon16,
            TypeInDatabaseEnum.Index => _indexIcon16,
            TypeInDatabaseEnum.baseTriggers or TypeInDatabaseEnum.Trigger => _indexIcon16,
            TypeInDatabaseEnum.basePartitions => _partitionGroupIcon16,
            TypeInDatabaseEnum.Partition => _partitionIcon16,
            TypeInDatabaseEnum.baseTablespaces => _partitionGroupIcon16,
            TypeInDatabaseEnum.Tablespace => _partitionIcon16,
            TypeInDatabaseEnum.columnInTables => _columnsIcon16,
            TypeInDatabaseEnum.distributionColumns or TypeInDatabaseEnum.thisDistributionCollumn => _distributedIcon16,
            TypeInDatabaseEnum.references or TypeInDatabaseEnum.thisReference => _refIcon16,
            TypeInDatabaseEnum.columnInThisTable
                or TypeInDatabaseEnum.columnInThisView
                or TypeInDatabaseEnum.columnInThisExternal => _columnOneIcon16,
            _ => null
        };
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        switch (value)
        {
            case Models.Tools.DbSchemaModel node:
                {
                    var bitmap = GetBitmapFromTypeInDatabase(
                        node.ActualTypeInDatabase,
                        node.ActualTypeInDatabase is TypeInDatabaseEnum.Connection
                            ? node.DatabaseTypeEnumValue
                            : null);
                    if (bitmap is not null)
                        return bitmap;
                    break;
                }
            case TypeInDatabaseEnum typeInDatabase:
                {
                    var bitmap = GetBitmapFromTypeInDatabase(typeInDatabase);
                    if (bitmap is not null)
                        return bitmap;
                    break;
                }
            case DatabaseTypeEnum typeEnum:
                return GetBitmapFromEnum(typeEnum);
            case string stringName:
                {
                    if (string.Equals(stringName, "Column", StringComparison.OrdinalIgnoreCase))
                        return _columnOneIcon16;

                    var schemaType = DatabaseServiceHelpers.FromStringEx(stringName);
                    if (schemaType != TypeInDatabaseEnum.otherNoneEntry)
                    {
                        var bitmap = GetBitmapFromTypeInDatabase(schemaType);
                        if (bitmap is not null)
                            return bitmap;
                    }

                    var enumType = DatabaseServiceHelpers.StringToDatabaseTypeEnum(stringName);
                    return GetBitmapFromEnum(enumType);
                }
        }
        return _defaultIcon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

using Avalonia.Media.Imaging;
using JustyBase.Editor.CompletionProviders;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using System.Globalization;

namespace JustyBase.Converters;

public sealed class DatabaseIconConverter : IValueConverter
{
    private readonly Bitmap _tableIcon16;
    private readonly Bitmap _columnOneIcon16;
    private readonly Bitmap _externalGroupIcon16;
    private readonly Bitmap _viewIcon16;
    private readonly Bitmap _procIcon16;
    private readonly Bitmap _synonymIcon16;
    private readonly Bitmap _schemaIcon16;
    private readonly Bitmap _databaseIcon16;
    private readonly Bitmap _defaultIcon;
    private readonly Bitmap _netezzaIcon16;
    private readonly Bitmap _oracleIcon16;
    private readonly Bitmap _db2Icon16;
    private readonly Bitmap _sqliteIcon16;
    private readonly Bitmap _duckDbIcon;
    private readonly Bitmap _mySqlIcon;
    private readonly Bitmap _msSqlIcon16;
    private readonly Bitmap _postgreIcon16;
    private readonly Bitmap _tableGroupIcon16;
    private readonly Bitmap _viewGroupIcon16;
    private readonly Bitmap _functuionsGroupIcon16;
    private readonly Bitmap _synonymGroupIcon16;
    private readonly Bitmap _procGroupIcon16;
    private readonly Bitmap _seqGroupIcon16;
    private readonly Bitmap _aggGroupIcon16;
    private readonly Bitmap _columnsIcon16;
    private readonly Bitmap _indexIcon16;
    private readonly Bitmap _indexGroupIcon16;
    private readonly Bitmap _partitionIcon16;
    private readonly Bitmap _partitionGroupIcon16;
    private readonly Bitmap _fluidGroupIcon16;
    private readonly Bitmap _distributedIcon16;
    private readonly Bitmap _refIcon16;

    public DatabaseIconConverter()
    {
        if (_databaseIcon16 is null)
        {
            _databaseIcon16 = App.Current.Resources["GeneralDbBitmap"] as Bitmap;
            _netezzaIcon16 = App.Current.Resources["NetezzaDbBitmap"] as Bitmap ?? _databaseIcon16;
            _oracleIcon16 = App.Current.Resources["OracleDbBitmap"] as Bitmap ?? _databaseIcon16;
            _db2Icon16 = App.Current.Resources["Db2DbBitmap"] as Bitmap ?? _databaseIcon16;
            _sqliteIcon16 = App.Current.Resources["SqliteDbBitmap"] as Bitmap ?? _databaseIcon16;
            _duckDbIcon = App.Current.Resources["DuckDbBitmap"] as Bitmap ?? _databaseIcon16;
            _mySqlIcon = App.Current.Resources["MySqlDbBitmap"] as Bitmap ?? _databaseIcon16;
            _msSqlIcon16 = App.Current.Resources["MsSqlDbBitmap"] as Bitmap ?? _databaseIcon16;
            _postgreIcon16 = App.Current.Resources["PostgreSqlDbBitmap"] as Bitmap ?? _databaseIcon16;

            _tableIcon16 = App.Current.Resources["TableBitmap"] as Bitmap;
            _viewIcon16 = App.Current.Resources["ViewBitmap"] as Bitmap;
            _tableGroupIcon16 = App.Current.Resources["TableGroupBitmap"] as Bitmap;
            _viewGroupIcon16 = App.Current.Resources["ViewGroupBitmap"] as Bitmap;
            _externalGroupIcon16 = App.Current.Resources["ExternalGroupBitmap"] as Bitmap;
            _functuionsGroupIcon16 = App.Current.Resources["FunctionsGroupBitmap"] as Bitmap;
            _synonymGroupIcon16 = App.Current.Resources["SynonymGroupBitmap"] as Bitmap;
            _synonymIcon16 = App.Current.Resources["SynonymBitmap"] as Bitmap;
            _procGroupIcon16 = App.Current.Resources["ProceduresGroupBitmap"] as Bitmap;
            _procIcon16 = App.Current.Resources["ProcedureBitmap"] as Bitmap;
            _seqGroupIcon16 = App.Current.Resources["SequencesGroupBitmap"] as Bitmap;
            _aggGroupIcon16 = App.Current.Resources["AggregatesGroupBitmap"] as Bitmap;
            _columnsIcon16 = App.Current.Resources["ColumnsBitmap"] as Bitmap;
            _columnOneIcon16 = App.Current.Resources["ColumnBitmap"] as Bitmap;
            _indexIcon16 = App.Current.Resources["IndexBitmap"] as Bitmap;
            _indexGroupIcon16 = App.Current.Resources["IndexGroupBitmap"] as Bitmap;
            _partitionIcon16 = App.Current.Resources["PartitionBitmap"] as Bitmap;
            _partitionGroupIcon16 = App.Current.Resources["PartitionGroupBitmap"] as Bitmap;
            _schemaIcon16 = App.Current.Resources["SchemaBitmap"] as Bitmap;
            _fluidGroupIcon16 = App.Current.Resources["FluidGroupBitmap"] as Bitmap;
            _distributedIcon16 = App.Current.Resources["DistributedOnBitmap"] as Bitmap;
            _refIcon16 = App.Current.Resources["ReferencesBitmap"] as Bitmap;
            _defaultIcon = App.Current.Resources["FolderIconBitmap"] as Bitmap;


            // FIX THIS !!
            GlyphExtensions.TableBitmap = _tableIcon16;
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

    private Bitmap GetBitmapFromEnum(DatabaseTypeEnum databaseTypeEnum)
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
            TypeInDatabaseEnum.dbase => connectionType is { } ct
                ? GetBitmapFromEnum(ct)
                : _databaseIcon16,
            TypeInDatabaseEnum.Schema => connectionType is { } ct
                ? GetBitmapFromEnum(ct)
                : _schemaIcon16,
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
            TypeInDatabaseEnum.columnInTables => _columnsIcon16,
            TypeInDatabaseEnum.distributionColumns or TypeInDatabaseEnum.thisDistributionCollumn => _distributedIcon16,
            TypeInDatabaseEnum.references or TypeInDatabaseEnum.thisReference => _refIcon16,
            TypeInDatabaseEnum.columnInThisTable
                or TypeInDatabaseEnum.columnInThisView
                or TypeInDatabaseEnum.columnInThisExternal => _columnOneIcon16,
            _ => null
        };
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        switch (value)
        {
            case Models.Tools.DbSchemaModel node:
                {
                    var bitmap = GetBitmapFromTypeInDatabase(
                        node.ActualTypeInDatabase,
                        node.ActualTypeInDatabase is TypeInDatabaseEnum.Connection
                            or TypeInDatabaseEnum.dbase
                            or TypeInDatabaseEnum.Schema
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

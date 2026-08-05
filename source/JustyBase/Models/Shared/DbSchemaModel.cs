using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services.Database;
using System.Collections.ObjectModel;


namespace JustyBase.Models.Tools;

public sealed partial class DbSchemaModel : ObservableObject, IDatabaseSchemaItem
{
    public string Name { get; set; }

    


    [ObservableProperty]
    public partial TypeInDatabaseEnum ActualTypeInDatabase { get; set; }
    public DatabaseTypeEnum DatabaseTypeEnumValue { get; set; }
    public DbSchemaModel? Parent { get; set; }
    public required string ConnectionName { get; set; }
    public string Database { get; set; }
    public string CurrentSchema { get; set; }
    public string Owner { get; set; }
    public string Comment { get; set; }
    public string ParentObjectName { get; set; }
    public string ToolTipText => $"Comment: {Comment ?? "no desc"}";
    public ObservableCollection<DbSchemaModel> LoadChildren(ObservableCollection<DbSchemaModel>? newNodeCollection = null)
    {
        DatabaseTypeEnum dbType = DatabaseTypeEnumValue;//GetDatabaseType();
        return LoadChildren(dbType, newNodeCollection);
    }

    private ObservableCollection<DbSchemaModel> LoadChildren(DatabaseTypeEnum databaseTypeEnum, ObservableCollection<DbSchemaModel>? newNodeCollection)
    {
        newNodeCollection ??= new ObservableCollection<DbSchemaModel>();
        switch (ActualTypeInDatabase)
        {
            case TypeInDatabaseEnum.Connection:
                var service = DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, Name);
                if (service is null)
                {
                    break;
                }
                var databases = service.GetDatabases("");

                foreach (var item in databases)
                {
                    newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.dbase, this.DatabaseTypeEnumValue, _generalApplicationData) { Parent = this, Name = item, Info = "database", ConnectionName = Name });
                }
                break;
            case TypeInDatabaseEnum.dbase:
                var schemaService = DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName);
                if (schemaService is null)
                {
                    break;
                }
                var schemas = schemaService.GetSchemas(Name, "");
                foreach (var item in schemas)
                {
                    newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.Schema, this.DatabaseTypeEnumValue, _generalApplicationData) { Parent = this, Name = item, Info = "schema", ConnectionName = ConnectionName, Database = Name });
                }
                break;
            case TypeInDatabaseEnum.Schema:
                var currSchema = Name;
                var itemsCollection = new List<(string name, string info, TypeInDatabaseEnum typeInDatabase)>
                {
                    ("Tables","tables", TypeInDatabaseEnum.baseTables),//0
                    ("External tables","external tables", TypeInDatabaseEnum.baseExternals),//1
                    ("Views","views", TypeInDatabaseEnum.baseViews),//2
                    ("Procedures","procedures", TypeInDatabaseEnum.baseProcedures),//3
                    ("Sequences","sequences", TypeInDatabaseEnum.baseSequence),//4
                    ("Functions","functions", TypeInDatabaseEnum.baseFunctions),//5
                    ("Synonyms","synonyms", TypeInDatabaseEnum.baseSynonyms),//6
                    ("Aggregate","aggregate",TypeInDatabaseEnum.baseAggregates),//7
                    ("Fluid Query Data Sources","fluids",TypeInDatabaseEnum.baseFluides),//8
                    ("Others","others", TypeInDatabaseEnum.otherNoneGroup)//9
                };
                if (databaseTypeEnum != DatabaseTypeEnum.NetezzaSQL && databaseTypeEnum != DatabaseTypeEnum.NetezzaSQLOdbc)
                {
                    itemsCollection.RemoveAt(8); //fluids
                }
                if (databaseTypeEnum != DatabaseTypeEnum.NetezzaSQL
                    && databaseTypeEnum != DatabaseTypeEnum.NetezzaSQLOdbc)
                {
                    itemsCollection.RemoveAt(1); //external
                }
                if (databaseTypeEnum == DatabaseTypeEnum.PostgreSql)
                {
                    itemsCollection.Insert(7, ("Indexes", "indexes", TypeInDatabaseEnum.baseIndexes));
                    itemsCollection.Insert(8, ("Partitions", "partitions", TypeInDatabaseEnum.basePartitions));
                }

                foreach (var item in itemsCollection)
                {
                    newNodeCollection.Add(new DbSchemaModel(item.typeInDatabase, this.DatabaseTypeEnumValue, _generalApplicationData)
                    {
                        Parent = this,
                        Name = item.name,
                        Info = item.info,
                        ConnectionName = ConnectionName,
                        Database = Database,
                        CurrentSchema = currSchema
                    });
                }
                break;
            case TypeInDatabaseEnum.baseTables:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Table, "table", setOwner: true);
                break;
            case TypeInDatabaseEnum.baseExternals:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.ExternalTable, "external table");
                break;
            case TypeInDatabaseEnum.baseViews:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.View, "view");
                break;
            case TypeInDatabaseEnum.baseProcedures:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Procedure, "procedure");
                break;
            case TypeInDatabaseEnum.baseSequence:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Sequence, "sequence");
                break;
            case TypeInDatabaseEnum.baseFunctions:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Function, "function");
                break;
            case TypeInDatabaseEnum.baseSynonyms:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Synonym, "synonym");
                break;
            case TypeInDatabaseEnum.baseFluides:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Fluid, "fluid");
                break;
            case TypeInDatabaseEnum.otherNoneGroup:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.otherNoneGroup, null, childType: TypeInDatabaseEnum.otherNoneEntry, useTextTypeAsInfo: true);
                break;
            case TypeInDatabaseEnum.baseAggregates:
                LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.thisAggregate, "aggregate");
                break;
            case TypeInDatabaseEnum.baseIndexes:
                if (Parent?.ActualTypeInDatabase == TypeInDatabaseEnum.Table && Parent.Name is not null)
                {
                    LoadDbObjectChildren(
                        newNodeCollection,
                        TypeInDatabaseEnum.Index,
                        "index",
                        relatedToParentTableName: Parent.Name);
                }
                else
                {
                    LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Index, "index");
                }
                break;
            case TypeInDatabaseEnum.basePartitions:
                if (Parent?.ActualTypeInDatabase == TypeInDatabaseEnum.Table && Parent.Name is not null)
                {
                    LoadDbObjectChildren(
                        newNodeCollection,
                        TypeInDatabaseEnum.Partition,
                        "partition",
                        relatedToParentTableName: Parent.Name);
                }
                else
                {
                    LoadDbObjectChildren(newNodeCollection, TypeInDatabaseEnum.Partition, "partition");
                }
                break;
            case TypeInDatabaseEnum.Table:
                AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.columnInTables, "Columns", "columns");
                if (databaseTypeEnum == DatabaseTypeEnum.PostgreSql)
                {
                    AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.baseIndexes, "Indexes", "indexes", parentObjectName: Name);
                    AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.basePartitions, "Partitions", "partitions", parentObjectName: Name);
                }
                if (databaseTypeEnum == DatabaseTypeEnum.NetezzaSQL || databaseTypeEnum == DatabaseTypeEnum.NetezzaSQLOdbc)
                {
                    AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.distributionColumns, "Distributed On", "distribution");
                    AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.organizeColumns, "Organized On", "organization");
                    AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.references, "References", "references");
                }
                AddMetadataNode(newNodeCollection, TypeInDatabaseEnum.DbItemMoreInfo, $"Owner : {this.Owner ?? "empty owner"}", "more info");
                break;
            case TypeInDatabaseEnum.columnInTables:
                LoadColumnChildren(newNodeCollection, Parent?.Name, TypeInDatabaseEnum.columnInThisTable, includeDesc: true);
                break;
            case TypeInDatabaseEnum.View:
                LoadColumnChildren(newNodeCollection, Name, TypeInDatabaseEnum.columnInThisView);
                break;
            case TypeInDatabaseEnum.ExternalTable:
                LoadColumnChildren(newNodeCollection, Name, TypeInDatabaseEnum.columnInThisExternal);
                break;
            case TypeInDatabaseEnum.columnInThisTable:
            case TypeInDatabaseEnum.columnInThisView:
            case TypeInDatabaseEnum.columnInThisExternal:
                string name = Parent?.Name;
                if (ActualTypeInDatabase == TypeInDatabaseEnum.columnInThisTable)
                {
                    name = Parent?.Parent?.Name;
                }
                var columnDetailsService = DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, Parent?.ConnectionName);
                if (columnDetailsService is null)
                {
                    break;
                }
                IEnumerable<DatabaseColumn> columnsX = columnDetailsService.GetColumns(Parent?.Database, Parent?.CurrentSchema, name, "");
                DatabaseColumn colTemp = null;
                foreach (var item in columnsX)
                {
                    if (item.Name == Name)
                    {
                        colTemp = item;
                        break;
                    }
                }
                if (colTemp is not null)
                {
                    newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.ColumnDataType, this.DatabaseTypeEnumValue, _generalApplicationData)
                    { Name = colTemp.FullTypeName, Info = "data type", ConnectionName = this.ConnectionName });
                    newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.ColumnDataTypeNullInfo, this.DatabaseTypeEnumValue, _generalApplicationData)
                    { Name = colTemp.ColumnNotNull.ToString(), Info = "not null", ConnectionName = this.ConnectionName });

                    var colDesc = String.IsNullOrWhiteSpace(colTemp.Desc) ? "No description" : colTemp.Desc;
                    newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.ColumnComment, this.DatabaseTypeEnumValue, _generalApplicationData)
                    { Name = colDesc, Info = "comment", ConnectionName = this.ConnectionName });
                }
                break;
            case TypeInDatabaseEnum.distributionColumns:
                var nzService = (DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName) as INetezza);
                if (nzService is not null)
                {
                    if (nzService.DistributionDictionary.TryGetValue(Database, out var dc0) &&
                        dc0.TryGetValue(CurrentSchema, out var dic1) && Parent?.Name != null && dic1.TryGetValue(Parent.Name, out var distList))
                    {
                        foreach (var item in distList)
                        {
                            newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.thisDistributionCollumn, this.DatabaseTypeEnumValue, _generalApplicationData)
                            { Parent = this, Name = item, Info = "", ConnectionName = this.ConnectionName });
                        }
                    }
                }
                break;
            case TypeInDatabaseEnum.organizeColumns:
                var nzService1 = (DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName) as INetezza);
                if (nzService1 is not null)
                {
                    if (nzService1.OrganizeDictionary.TryGetValue(Database, out var dc0) &&
                        dc0.TryGetValue(CurrentSchema, out var dic1) && Parent is not null && dic1.TryGetValue(Parent.Name, out var organizeList))
                    {
                        foreach (var item in organizeList)
                        {
                            newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.thisOrganizeCollumn, this.DatabaseTypeEnumValue, _generalApplicationData)
                            { Parent = this, Name = item, Info = "", ConnectionName = this.ConnectionName });
                        }
                    }
                }
                break;
            case TypeInDatabaseEnum.references:
                var nzService2 = (DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName) as INetezza);
                if (nzService2 is not null)
                {
                    if (nzService2.KeysDictionary.TryGetValue(Database, out var dict1) && dict1.TryGetValue(CurrentSchema, out var dict2)
                && Parent is not null && Parent.Name is not null && dict2.TryGetValue(Parent.Name, out var dict3)
                )
                    {
                        foreach (var (keyName, kefInfo) in dict3)
                        {
                            newNodeCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.thisReference, this.DatabaseTypeEnumValue, _generalApplicationData)
                            {
                                Parent = this,
                                Name = $"{DatabaseService.KeyNameFromChar(kefInfo.KeyType)}: {keyName}",
                                Info = "(" + string.Join(',', kefInfo.ColumnList.Select(o => o.colName)) + ")"
                                ,
                                ConnectionName = this.ConnectionName
                            });
                        }
                    }
                }
                break;
            default:
                break;
        }
        return newNodeCollection;
    }

    /// <summary>
    /// Adds a metadata child node (like "Columns", "Distribution", etc.) to a table.
    /// </summary>
    private void AddMetadataNode(
        ObservableCollection<DbSchemaModel> collection,
        TypeInDatabaseEnum type,
        string name,
        string info,
        string? parentObjectName = null)
    {
        collection.Add(new DbSchemaModel(type, this.DatabaseTypeEnumValue, _generalApplicationData)
        {
            Parent = this,
            Name = name,
            Info = info,
            ConnectionName = ConnectionName,
            Database = Database,
            CurrentSchema = CurrentSchema,
            ParentObjectName = parentObjectName ?? string.Empty
        });
    }

    /// <summary>
    /// Loads column children from the database service, eliminating duplication across 3 switch cases.
    /// </summary>
    private void LoadColumnChildren(
        ObservableCollection<DbSchemaModel> collection,
        string? objectName,
        TypeInDatabaseEnum columnType,
        bool includeDesc = false)
    {
        var service = DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName);
        if (service is null)
        {
            return;
        }
        var columns = service.GetColumns(Database, CurrentSchema, objectName, "");
        foreach (var item in columns)
        {
            var node = new DbSchemaModel(columnType, this.DatabaseTypeEnumValue, _generalApplicationData)
            {
                Parent = this,
                Name = item.Name,
                Info = "column",
                ConnectionName = this.ConnectionName
            };
            if (includeDesc)
            {
                node.Comment = item.Desc;
            }
            collection.Add(node);
        }
    }

    /// <summary>
    /// Loads database object children of a given type, eliminating duplication across 10+ switch cases.
    /// </summary>
    private void LoadDbObjectChildren(
        ObservableCollection<DbSchemaModel> collection,
        TypeInDatabaseEnum queryType,
        string? infoLabel,
        TypeInDatabaseEnum? childType = null,
        bool setOwner = false,
        bool useTextTypeAsInfo = false,
        string? relatedToParentTableName = null)
    {
        var resolvedChildType = childType ?? queryType;
        var service = DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName);
        if (service is null)
        {
            return;
        }
        var objects = service
            .GetDbObjects(Database, CurrentSchema, "", queryType)
            .Where(o =>
            {
                if (string.IsNullOrWhiteSpace(relatedToParentTableName))
                {
                    return true;
                }

                var fullNameToken = $"{CurrentSchema}.{relatedToParentTableName}";
                return !string.IsNullOrWhiteSpace(o.Desc)
                    && o.Desc.Contains(fullNameToken, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(o => o.Owner.PadRight(20) + o.Name);

        foreach (var item in objects)
        {
            var info = useTextTypeAsInfo
                ? $"{item.Owner}'s {item.TextType}"
                : $"{item.Owner}'s {infoLabel}";

            var node = new DbSchemaModel(resolvedChildType, this.DatabaseTypeEnumValue, _generalApplicationData)
            {
                Parent = this,
                Name = item.Name,
                Info = info,
                ConnectionName = ConnectionName,
                Database = Database,
                CurrentSchema = CurrentSchema,
                Comment = item.Desc
            };

            if (setOwner)
            {
                node.Owner = item.Owner;
            }

            collection.Add(node);
        }
    }
}


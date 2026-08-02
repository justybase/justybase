using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Minimal helpers for product-ish headless scenarios without full App bootstrap.
/// </summary>
internal static class HeadlessProductHost
{
    public static Mock<IGeneralApplicationData> CreateAppData(string connectionName = "SAMPLE_CONNECTION")
    {
        var appData = new Mock<IGeneralApplicationData>();
        var login = new LoginDataModel
        {
            ConnectionName = connectionName,
            Driver = "SQLite",
            Database = "main",
            Server = "localhost",
            UserName = "u",
            Password = "p"
        };
        appData.SetupGet(a => a.LoginDataDic).Returns(new Dictionary<string, LoginDataModel>
        {
            [connectionName] = login
        });
        appData.SetupGet(a => a.Config).Returns(new AppOptions
        {
            ConnectionNameInSchemaSearch = connectionName,
            RefreshOnStartupInSchemaSearch = false
        });
        appData.SetupGet(a => a.GlobalLoggerObject).Returns(Mock.Of<ISimpleLogger>());
        return appData;
    }

    public static Mock<IDatabaseService> CreateLargeSchemaService(int tablesPerSchema = 200, int schemas = 5)
    {
        var service = new Mock<IDatabaseService>();
        service.SetupGet(s => s.Database).Returns("main");
        service.SetupProperty(s => s.ConnectedLevel, DatabaseConnectedLevel.ConnectedDatabaseObjects);
        service.Setup(s => s.GetDatabases(It.IsAny<string>())).Returns(["main"]);

        var schemaNames = Enumerable.Range(1, schemas).Select(i => $"S{i}").ToArray();
        service.Setup(s => s.GetSchemas("main", It.IsAny<string>())).Returns(schemaNames);
        service.Setup(s => s.GetSchemas(It.IsAny<string>(), It.IsAny<string>())).Returns(schemaNames);

        service.Setup(s => s.GetDbObjects(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TypeInDatabaseEnum>()))
            .Returns((string _, string schema, string _, TypeInDatabaseEnum type) =>
            {
                if (type != TypeInDatabaseEnum.Table)
                {
                    return Array.Empty<DatabaseObject>();
                }

                return Enumerable.Range(1, tablesPerSchema)
                    .Select(i => new DatabaseObject(
                        i,
                        $"T{i}",
                        $"desc {schema}.{i}",
                        TypeInDatabaseEnum.Table,
                        "TABLE",
                        "owner",
                        DateTime.UtcNow))
                    .ToArray();
            });

        service.Setup(s => s.GetColumnsFromAllTablesAndSchemas(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);

        service.Setup(s => s.GetColumns(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns([]);

        service.Setup(s => s.GetProceduresSignaturesFromName(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        service.Setup(s => s.CacheAllObjects(It.IsAny<TypeInDatabaseEnum[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return service;
    }
}

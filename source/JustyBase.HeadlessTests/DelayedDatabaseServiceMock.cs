using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Adds synchronous artificial latency to selected <see cref="IDatabaseService"/> members
/// so headless UI tests can assert the dispatcher keeps ticking during offline/slow DB work.
/// </summary>
internal static class DelayedDatabaseServiceMock
{
    public const int DefaultInjectedDelayMs = 300;

    public static Mock<IDatabaseService> Create(
        int delayMs = DefaultInjectedDelayMs,
        int tablesPerSchema = 20,
        int schemas = 3,
        bool delayGetDatabases = true,
        bool delayGetSchemas = true,
        bool delayGetDbObjects = false,
        bool delayGetColumns = false)
    {
        var service = HeadlessProductHost.CreateLargeSchemaService(tablesPerSchema, schemas);
        ApplyCallDelay(
            service,
            delayMs,
            tablesPerSchema,
            schemas,
            delayGetDatabases: delayGetDatabases,
            delayGetSchemas: delayGetSchemas,
            delayGetDbObjects: delayGetDbObjects,
            delayGetColumns: delayGetColumns);
        return service;
    }

    public static void ApplyCallDelay(
        Mock<IDatabaseService> service,
        int delayMs,
        int tablesPerSchema = 20,
        int schemas = 3,
        bool delayGetDatabases = true,
        bool delayGetSchemas = true,
        bool delayGetDbObjects = false,
        bool delayGetColumns = false)
    {
        if (delayMs <= 0)
        {
            return;
        }

        string[] databaseNames = ["main"];
        var schemaNames = Enumerable.Range(1, schemas).Select(i => $"S{i}").ToArray();

        if (delayGetDatabases)
        {
            service.Setup(s => s.GetDatabases(It.IsAny<string>()))
                .Returns(() =>
                {
                    Thread.Sleep(delayMs);
                    return databaseNames;
                });
        }

        if (delayGetSchemas)
        {
            service.Setup(s => s.GetSchemas(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    Thread.Sleep(delayMs);
                    return schemaNames;
                });
        }

        if (delayGetDbObjects)
        {
            service.Setup(s => s.GetDbObjects(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TypeInDatabaseEnum>()))
                .Returns((string _, string schema, string _, TypeInDatabaseEnum type) =>
                {
                    Thread.Sleep(delayMs);
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
        }

        if (delayGetColumns)
        {
            service.Setup(s => s.GetColumns(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    Thread.Sleep(delayMs);
                    return Array.Empty<DatabaseColumn>();
                });
        }
    }
}

using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Services;
using JustyBase.Helpers;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Threading;

namespace JustyBase.Services;

public sealed class LocalToolExecutor : ILocalToolExecutor
{
    private readonly ISimpleLogger _logger;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private readonly SqlDiagnosticsViewModel _diagnosticsViewModel;
    private readonly SqlExecutionErrorStore _sqlExecutionErrorStore;
    private readonly NetezzaReferenceProvider _netezzaReferenceProvider = new();

    private Func<string?>? _currentSqlProvider;
    private Func<(string ConnectionName, string DatabaseName)?>? _activeSqlContextProvider;
    private Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?>? _sqlEditorContextProvider;
    private Func<string, bool>? _sqlEditorBufferUpdater;
    private readonly Dictionary<string, Func<string, Task<string>>> _toolExecutors;

    public LocalToolExecutor(
        ISimpleLogger logger,
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver,
        SqlDiagnosticsViewModel diagnosticsViewModel,
        SqlExecutionErrorStore sqlExecutionErrorStore)
    {
        _logger = logger;
        _generalApplicationData = generalApplicationData;
        _databaseServiceResolver = databaseServiceResolver;
        _diagnosticsViewModel = diagnosticsViewModel;
        _sqlExecutionErrorStore = sqlExecutionErrorStore;

        _toolExecutors = new Dictionary<string, Func<string, Task<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GetCurrentSql"] = args => GetCurrentSqlAsync(),
            ["GetCurrentSqlEditorContext"] = args => GetCurrentSqlEditorContextAsync(DeserializeInt(args, "maxChars", 20000)),
            ["GetActiveDatabaseContext"] = args => Task.Run(() => GetActiveDatabaseContext()),
            ["ListSchemas"] = args => Task.Run(() => ListSchemas(DeserializeString(args, "databaseName"), DeserializeInt(args, "limit", 50))),
            ["BrowseSchemaObjects"] = args => Task.Run(() => BrowseSchemaObjects(DeserializeString(args, "schemaName"), DeserializeString(args, "objectType", "all"), DeserializeInt(args, "limit", 100))),
            ["SearchSchemaObjects"] = args => Task.Run(() => SearchSchemaObjects(DeserializeString(args, "pattern"), DeserializeString(args, "objectType"), DeserializeString(args, "schemaName"), DeserializeInt(args, "limit", 50))),
            ["GetObjectDefinition"] = args => GetObjectDefinitionAsync(DeserializeString(args, "objectName") ?? string.Empty, DeserializeString(args, "objectType"), DeserializeString(args, "schemaName"), DeserializeString(args, "databaseName"), DeserializeInt(args, "maxChars", 20000)),
            ["GetObjectColumns"] = args => Task.Run(() => GetObjectColumns(DeserializeString(args, "objectName"), DeserializeString(args, "schemaName"), DeserializeString(args, "databaseName"), DeserializeInt(args, "limit", 200))),
            ["GetTableMetadata"] = args => GetTableMetadataAsync(DeserializeString(args, "tableName") ?? string.Empty, DeserializeString(args, "schemaName"), DeserializeString(args, "databaseName"), DeserializeBool(args, "includeStatsPreview", false), DeserializeInt(args, "rowLimit", 20)),
            ["GetNetezzaReference"] = args => Task.FromResult(GetNetezzaReference(DeserializeString(args, "topic", "all"))),
            ["GetDiagnostics"] = args => GetDiagnosticsAsync(DeserializeString(args, "severity"), DeserializeInt(args, "limit", 50)),
            ["GetLastExecutionError"] = args => GetLastExecutionError(),
            ["ExportSchema"] = args => ExportSchema(DeserializeString(args, "schemaName"), DeserializeString(args, "objectType"), DeserializeInt(args, "maxChars", 30000)),
            ["ExecuteSql"] = args => ExecuteSql(DeserializeString(args, "sql") ?? string.Empty),
            ["ApplySqlFix"] = args => ApplySqlFixAsync(DeserializeString(args, "proposedSql") ?? string.Empty),
        };
    }

    private static string? DeserializeString(string json, string propertyName, string? defaultValue = null)
    {
        if (string.IsNullOrEmpty(json)) return defaultValue;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(propertyName, out var element))
        {
            return element.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static int DeserializeInt(string json, string propertyName, int defaultValue = 0)
    {
        if (string.IsNullOrEmpty(json)) return defaultValue;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(propertyName, out var element))
        {
            return element.ValueKind == JsonValueKind.Number ? element.GetInt32() : defaultValue;
        }
        return defaultValue;
    }

    private static bool DeserializeBool(string json, string propertyName, bool defaultValue = false)
    {
        if (string.IsNullOrEmpty(json)) return defaultValue;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(propertyName, out var element))
        {
            return element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False ? element.GetBoolean() : defaultValue;
        }
        return defaultValue;
    }

    #region Provider Setters

    public void SetCurrentSqlProvider(Func<string?> provider) => _currentSqlProvider = provider;
    public void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider) => _activeSqlContextProvider = provider;
    public void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider) => _sqlEditorContextProvider = provider;
    public void SetSqlEditorBufferUpdater(Func<string, bool> updater) => _sqlEditorBufferUpdater = updater;

    #endregion

    #region Database Exploration Tools

    [Description("Returns the active database context including connection name, database name, and available schemas. Use DATABASE.SCHEMA.OBJECT format in all SQL.")]
    public string GetActiveDatabaseContext()
    {
        if (!TryGetActiveDatabaseService(out var service, out var connectionName, out var databaseName, out var error) || service is null)
        {
            return error;
        }

        try
        {
            var schemas = service.GetSchemas(databaseName, "").Take(30).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("=== ACTIVE DATABASE CONTEXT ===");
            sb.AppendLine($"Connection: {connectionName}");
            sb.AppendLine($"Database: {databaseName}");
            sb.AppendLine();
            sb.AppendLine("Always use qualified names: DATABASE.SCHEMA.OBJECT");
            sb.AppendLine();
            sb.AppendLine("Available schemas:");
            foreach (var schema in schemas)
            {
                sb.AppendLine($"  - {databaseName}.{schema}");
            }

            return SanitizeToolResult(sb.ToString().TrimEnd(), nameof(GetActiveDatabaseContext));
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Database context lookup failed: {ex.Message}";
        }
    }

    [Description("Lists all schemas in the active database. Use this to discover schema structure.")]
    public string ListSchemas(string? databaseName = null, int limit = 50)
    {
        if (!TryGetActiveDatabaseService(out var service, out var connectionName, out var activeDatabase, out var error) || service is null)
        {
            return error;
        }

        var targetDb = string.IsNullOrWhiteSpace(databaseName) ? activeDatabase : databaseName;
        limit = Math.Clamp(limit, 1, 200);

        try
        {
            var schemas = service.GetSchemas(targetDb, "").Take(limit).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Schemas in {connectionName}.{targetDb} ({schemas.Count}):");
            foreach (var schema in schemas)
            {
                sb.AppendLine($"  - {targetDb}.{schema}");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Failed to list schemas: {ex.Message}";
        }
    }

    [Description("Browses database objects in a schema. Object types: table, view, procedure, function, synonym, external, fluid, all.")]
    public string BrowseSchemaObjects(string schemaName, string objectType = "all", int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return "Schema name cannot be empty.";
        }

        if (!TryGetActiveDatabaseService(out var service, out _, out var activeDatabase, out var error) || service is null)
        {
            return error;
        }

        limit = Math.Clamp(limit, 1, 500);
        var types = objectType.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? LocalToolHelpers.DefaultSchemaSearchTypes
            : LocalToolHelpers.ResolveSchemaObjectTypes(objectType);

        try
        {
            var sb = new StringBuilder();
            var totalFound = 0;

            foreach (var type in types)
            {
                var objects = service.GetDbObjects(activeDatabase, schemaName, "", type).Take(limit).ToList();
                if (objects.Count == 0) continue;

                totalFound += objects.Count;
                sb.AppendLine($"\n{type.ToStringEx()}s in {activeDatabase}.{schemaName} ({objects.Count}):");

                foreach (var obj in objects)
                {
                    var fqn = $"{activeDatabase}.{schemaName}.{obj.Name}";
                    if (string.IsNullOrWhiteSpace(obj.Desc))
                    {
                        sb.AppendLine($"  - {fqn}");
                    }
                    else
                    {
                        var desc = obj.Desc.Length > 60 ? obj.Desc[..60] + "..." : obj.Desc;
                        sb.AppendLine($"  - {fqn} // {desc}");
                    }
                }
            }

            if (totalFound == 0)
            {
                return $"No objects found in schema '{activeDatabase}.{schemaName}'.";
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Failed to browse schema objects: {ex.Message}";
        }
    }

    [Description("Searches schema objects by name pattern. Supports types: table, view, procedure, function, synonym, external, fluid.")]
    public string SearchSchemaObjects(string pattern, string? objectType = null, string? schemaName = null, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Pattern cannot be empty.";
        }

        if (!TryGetActiveDatabaseService(out var service, out _, out var activeDatabase, out var error) || service is null)
        {
            return error;
        }

        limit = Math.Clamp(limit, 1, 200);
        var objectTypes = LocalToolHelpers.ResolveSchemaObjectTypes(objectType);
        var found = new List<(string Schema, TypeInDatabaseEnum Type, DatabaseObject Object)>();

        try
        {
            var schemas = string.IsNullOrWhiteSpace(schemaName)
                ? service.GetSchemas(activeDatabase, "")
                : service.GetSchemas(activeDatabase, schemaName)
                    .Where(x => x.Equals(schemaName, StringComparison.OrdinalIgnoreCase));

            foreach (var schema in schemas)
            {
                foreach (var type in objectTypes)
                {
                    foreach (var dbObject in service.GetDbObjects(activeDatabase, schema, "", type))
                    {
                        if (dbObject.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(dbObject.Desc) && dbObject.Desc.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                        {
                            found.Add((schema, type, dbObject));
                            if (found.Count >= limit) goto END_SEARCH;
                        }
                    }
                }
            }
        END_SEARCH:;

            if (found.Count == 0) return $"No objects matching '{pattern}' were found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {found.Count} object(s) (limit {limit}):");
            foreach (var item in found)
            {
                sb.AppendLine($"- {activeDatabase}.{item.Schema}.{item.Object.Name} [{item.Type.ToStringEx()}]");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Schema search failed: {ex.Message}";
        }
    }

    #endregion

    #region Object Inspection Tools

    [Description("Returns column metadata for a table/view in the active database.")]
    public string GetObjectColumns(string objectName, string? schemaName = null, string? databaseName = null, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return "Object name cannot be empty.";
        if (!TryGetActiveDatabaseService(out var service, out _, out var activeDatabase, out var error) || service is null) return error;

        var parsed = CopilotSqlAssistantAnalyzer.ParseQualifiedName(objectName);
        var objectDatabase = string.IsNullOrWhiteSpace(parsed.Database) ? (databaseName ?? activeDatabase) : parsed.Database;
        var objectSchema = string.IsNullOrWhiteSpace(parsed.Schema) ? schemaName : parsed.Schema;
        var objectShortName = parsed.ObjectName;
        if (string.IsNullOrWhiteSpace(objectShortName)) return "Object name cannot be empty.";
        limit = Math.Clamp(limit, 1, 1000);

        try
        {
            var schemaCandidates = string.IsNullOrWhiteSpace(objectSchema)
                ? service.GetSchemas(objectDatabase, "")
                : [objectSchema];

            foreach (var schema in schemaCandidates)
            {
                var columns = service.GetColumns(objectDatabase, schema, objectShortName, "").Take(limit).ToList();
                if (columns.Count == 0) continue;

                var sb = new StringBuilder();
                sb.AppendLine($"Columns for {objectDatabase}.{schema}.{objectShortName}:");
                foreach (var column in columns)
                {
                    sb.AppendLine($"- {column.Name} {column.FullTypeName}");
                }
                return sb.ToString().TrimEnd();
            }

            return $"No columns found for '{objectName}'.";
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Column lookup failed: {ex.Message}";
        }
    }

    [Description("Returns DDL for a table/view/procedure in DATABASE.SCHEMA.OBJECT form.")]
    public Task<string> GetObjectDefinition(string objectName, string? objectType = null, string? schemaName = null, string? databaseName = null, int maxChars = 20000)
        => GetObjectDefinitionAsync(objectName, objectType, schemaName, databaseName, maxChars);

    private async Task<string> GetObjectDefinitionAsync(string objectName, string? objectType = null, string? schemaName = null, string? databaseName = null, int maxChars = 20000)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return "Object name cannot be empty.";
        if (!TryGetActiveDatabaseService(out var service, out _, out var activeDatabase, out var error) || service is null) return error;

        maxChars = Math.Clamp(maxChars, 500, 200000);
        var parsed = CopilotSqlAssistantAnalyzer.ParseQualifiedName(objectName);
        var targetDatabase = string.IsNullOrWhiteSpace(parsed.Database) ? (databaseName ?? activeDatabase) : parsed.Database;
        var targetSchema = string.IsNullOrWhiteSpace(parsed.Schema) ? schemaName : parsed.Schema;
        var shortName = parsed.ObjectName;
        if (string.IsNullOrWhiteSpace(shortName)) return "Object name cannot be empty.";

        TypeInDatabaseEnum? explicitType = null;
        if (!string.IsNullOrWhiteSpace(objectType))
            explicitType = DatabaseServiceHelpers.FromStringEx(objectType.Trim());

        if (string.IsNullOrWhiteSpace(targetSchema) && explicitType.HasValue && explicitType.Value != TypeInDatabaseEnum.otherNoneEntry)
            targetSchema = LocalToolHelpers.FindObjectSchema(service, targetDatabase, shortName, explicitType.Value);

        targetSchema ??= service.GetSchemas(targetDatabase, "").FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetSchema)) return $"Could not resolve schema for '{objectName}'.";

        try
        {
            string ddl = explicitType switch
            {
                TypeInDatabaseEnum.Table => await service.GetCreateTableText(targetDatabase, targetSchema, shortName),
                TypeInDatabaseEnum.View => await service.GetCreateViewText(targetDatabase, targetSchema, shortName),
                TypeInDatabaseEnum.Procedure => await service.GetCreateProcedureText(targetDatabase, targetSchema, shortName, forceFreshCode: true),
                TypeInDatabaseEnum.ExternalTable => await service.GetCreateExternalText(targetDatabase, targetSchema, shortName),
                TypeInDatabaseEnum.Synonym => await service.GetCreateSynonymText(targetDatabase, targetSchema, shortName),
                TypeInDatabaseEnum.Index => await service.GetCreateIndexText(targetDatabase, targetSchema, shortName),
                TypeInDatabaseEnum.Partition => await service.GetCreatePartitionText(targetDatabase, targetSchema, shortName),
                _ => await GetObjectSourceAsync(service, targetDatabase, targetSchema, shortName, objectType)
            };

            if (string.IsNullOrWhiteSpace(ddl)) return $"No definition found for {targetDatabase}.{targetSchema}.{shortName}.";
            if (ddl.Length > maxChars) ddl = ddl[..maxChars] + $"{Environment.NewLine}-- [truncated]";

            return $"Definition for {targetDatabase}.{targetSchema}.{shortName}:\n```sql\n{ddl}\n```";
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Definition lookup failed: {ex.Message}";
        }
    }

    [Description("Returns table metadata: DDL, distribution/organize hints, optional statistics preview.")]
    public Task<string> GetTableMetadata(string tableName, string? schemaName = null, string? databaseName = null, bool includeStatsPreview = false, int rowLimit = 20)
        => GetTableMetadataAsync(tableName, schemaName, databaseName, includeStatsPreview, rowLimit);

    private async Task<string> GetTableMetadataAsync(string tableName, string? schemaName = null, string? databaseName = null, bool includeStatsPreview = false, int rowLimit = 20)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return "Table name cannot be empty.";
        if (includeStatsPreview)
            return "[Blocked: AI tools cannot read table statistics or SQL result rows.]";
        if (!TryGetActiveDatabaseService(out var service, out _, out var activeDatabase, out var error) || service is null) return error;

        rowLimit = Math.Clamp(rowLimit, 1, 200);
        var parsed = CopilotSqlAssistantAnalyzer.ParseQualifiedName(tableName);
        var targetDatabase = string.IsNullOrWhiteSpace(parsed.Database) ? (databaseName ?? activeDatabase) : parsed.Database;
        var targetSchema = string.IsNullOrWhiteSpace(parsed.Schema) ? schemaName : parsed.Schema;
        var shortName = parsed.ObjectName;
        if (string.IsNullOrWhiteSpace(shortName)) return "Table name cannot be empty.";

        targetSchema ??= LocalToolHelpers.FindObjectSchema(service, targetDatabase, shortName, TypeInDatabaseEnum.Table);
        if (string.IsNullOrWhiteSpace(targetSchema)) return $"Could not resolve schema for table '{tableName}'.";

        var fqName = $"{targetDatabase}.{targetSchema}.{shortName}";
        var sb = new StringBuilder();
        sb.AppendLine($"Metadata for {fqName}");

        try
        {
            var ddl = await service.GetCreateTableText(targetDatabase, targetSchema, shortName);
            if (!string.IsNullOrWhiteSpace(ddl))
            {
                var distLine = ddl.Split(["\r\n", "\n"], StringSplitOptions.None)
                    .FirstOrDefault(x => x.Contains("DISTRIBUTE ON", StringComparison.OrdinalIgnoreCase));
                var organizeLine = ddl.Split(["\r\n", "\n"], StringSplitOptions.None)
                    .FirstOrDefault(x => x.Contains("ORGANIZE ON", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(distLine)) sb.AppendLine($"- Distribution: {distLine.Trim()}");
                if (!string.IsNullOrWhiteSpace(organizeLine)) sb.AppendLine($"- Organize: {organizeLine.Trim()}");
            }

            if (service is INetezza netezza)
            {
                if (!netezza.DistributionDictionary.ContainsKey(targetDatabase))
                    netezza.FillDistInfoForDatabase(targetDatabase);

                if (netezza.DistributionDictionary.TryGetValue(targetDatabase, out var d1) &&
                    d1.TryGetValue(targetSchema, out var d2) &&
                    d2.TryGetValue(shortName, out var distCols) && distCols.Count > 0)
                {
                    sb.AppendLine($"- Distribution columns: {string.Join(", ", distCols)}");
                }

                if (netezza.OrganizeDictionary.TryGetValue(targetDatabase, out var o1) &&
                    o1.TryGetValue(targetSchema, out var o2) &&
                    o2.TryGetValue(shortName, out var organizeCols) && organizeCols.Count > 0)
                {
                    sb.AppendLine($"- Organize columns: {string.Join(", ", organizeCols)}");
                }
            }

            if (includeStatsPreview)
            {
                var statsSql = service.GetCheckDistributeText(targetDatabase, targetSchema, shortName);
                if (!string.IsNullOrWhiteSpace(statsSql) && !statsSql.Contains("not supported", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                    sb.AppendLine("Statistics preview:");
                    sb.AppendLine(RunReadOnlySqlPreview(service, targetDatabase, statsSql, rowLimit));
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Metadata lookup warning: {ex.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    #endregion

    #region SQL Editor Tools

    [Description("Returns the current SQL from the active editor.")]
    public Task<string> GetCurrentSql() => GetCurrentSqlAsync();

    private async Task<string> GetCurrentSqlAsync()
    {
        try
        {
            if (_currentSqlProvider is null) return LocalSqlEditorContextFormatter.NoActiveSqlDocumentMessage;

            var sql = await UiThreadMarshal.InvokeAsync(_currentSqlProvider);

            if (string.IsNullOrWhiteSpace(sql)) return LocalSqlEditorContextFormatter.NoActiveSqlDocumentMessage;
            if (sql.Length > 20000) sql = sql[..20000] + "\n-- [truncated at 20k chars] --";
            return sql;
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Error getting current SQL: {ex.Message}";
        }
    }

    [Description("Reads active SQL editor context: selection, cursor position, full buffer with markers.")]
    public Task<string> GetCurrentSqlEditorContext(int maxChars = 20000)
        => GetCurrentSqlEditorContextAsync(maxChars);

    private async Task<string> GetCurrentSqlEditorContextAsync(int maxChars = 20000)
    {
        var context = await GetSqlEditorContextSnapshotAsync();
        if (context is null || string.IsNullOrWhiteSpace(context.Value.FullText))
        {
            return LocalSqlEditorContextFormatter.NoActiveSqlDocumentMessage;
        }

        maxChars = Math.Clamp(maxChars, 500, 200000);
        var hasSelection = LocalSqlEditorContextFormatter.HasValidSelection(context.Value);
        var selectedText = hasSelection ? LocalSqlEditorContextFormatter.GetSelectedText(context.Value) : string.Empty;

        var markedBuffer = hasSelection ? LocalSqlEditorContextFormatter.MarkSelectedSqlRegion(context.Value) : context.Value.FullText;
        if (markedBuffer.Length > maxChars) markedBuffer = markedBuffer[..maxChars] + $"{Environment.NewLine}-- [truncated]";

        var sb = new StringBuilder();
        sb.AppendLine("Active SQL editor context:");
        sb.AppendLine($"- Buffer length: {context.Value.FullText.Length}");
        sb.AppendLine($"- Caret offset: {context.Value.CaretOffset}");
        sb.AppendLine($"- Selection: {(hasSelection ? $"start={context.Value.SelectionStart}, length={context.Value.SelectionLength}" : "none")}");
        if (hasSelection)
        {
            sb.AppendLine("Selected snippet:");
            sb.AppendLine("```sql");
            sb.AppendLine(selectedText);
            sb.AppendLine("```");
        }
        sb.AppendLine("Full buffer:");
        sb.AppendLine("```sql");
        sb.AppendLine(markedBuffer);
        sb.AppendLine("```");
        return sb.ToString().TrimEnd();
    }

    [Description("Returns Netezza SQL reference guidance. topic: optimization | nzplsql | all.")]
    public string GetNetezzaReference(string topic = "all")
    {
        topic = string.IsNullOrWhiteSpace(topic) ? "all" : topic.Trim().ToLowerInvariant();
        return _netezzaReferenceProvider.GetNetezzaReference(topic);
    }

    #endregion

    #region Diagnostics Tools

    [Description("Returns current SQL diagnostics (errors, warnings, info) from a heuristic/static linter. Results may be incomplete, stale, or incorrect; treat them as advisory and verify against SQL/schema. Use severity filter: error, warning, info, hint.")]
    public Task<string> GetDiagnostics(string? severityFilter = null, int limit = 50)
        => GetDiagnosticsAsync(severityFilter, limit);

    private async Task<string> GetDiagnosticsAsync(string? severityFilter = null, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        IReadOnlyList<DiagnosticItem> items;

        try
        {
            items = await UiThreadMarshal.InvokeAsync(() => (IReadOnlyList<DiagnosticItem>)_diagnosticsViewModel.Items);
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Failed to read diagnostics: {ex.Message}";
        }

        if (items.Count == 0) return "No diagnostics issues found.";

        IEnumerable<DiagnosticItem> query = items;
        if (!string.IsNullOrWhiteSpace(severityFilter))
            query = query.Where(d => d.Severity.Equals(severityFilter, StringComparison.OrdinalIgnoreCase));

        var matches = query.Take(limit).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Diagnostics ({matches.Count} shown, {items.Count} total):");
        foreach (var d in matches)
        {
            var loc = d.StartLine > 0 ? $" L{d.StartLine}:{d.StartColumn}" : "";
            sb.AppendLine($"[{d.Severity}] {d.RuleId}: {d.Message}{loc}");
        }

        return sb.ToString().TrimEnd();
    }

    #endregion

    #region SQL Fix Tools

    [Description("Return the most recent SQL execution error without returning SQL result data.")]
    public Task<string> GetLastExecutionError()
    {
        var error = _sqlExecutionErrorStore.LastError;
        if (error is not null)
        {
            var context = string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(error.DocumentTitle) ? null : $"document={error.DocumentTitle}",
                string.IsNullOrWhiteSpace(error.ConnectionName) ? null : $"connection={error.ConnectionName}",
                string.IsNullOrWhiteSpace(error.DatabaseName) ? null : $"database={error.DatabaseName}"
            }.Where(value => value is not null));
            var suffix = string.IsNullOrWhiteSpace(context) ? string.Empty : $" ({context})";
            return Task.FromResult($"{error.Timestamp:O}: {error.Message}{suffix}");
        }

        return Task.FromResult("No SQL execution error has been recorded.");
    }

    [Description("Export schema names, object names and bounded metadata. This never reads table rows.")]
    public async Task<string> ExportSchema(string? schemaName = null, string? objectType = null, int maxChars = 30000)
    {
        maxChars = Math.Clamp(maxChars, 1000, 50000);
        var sb = new StringBuilder();
        sb.AppendLine("SCHEMA METADATA EXPORT (no table rows)");
        sb.AppendLine(GetActiveDatabaseContext());
        sb.AppendLine();
        sb.AppendLine(ListSchemas(limit: 200));

        if (!TryGetActiveDatabaseService(out var service, out _, out var activeDatabase, out var error) || service is null)
        {
            sb.AppendLine(error);
            return TrimSchemaExport(sb, maxChars);
        }

        var schemas = string.IsNullOrWhiteSpace(schemaName)
            ? service.GetSchemas(activeDatabase, "").Take(10).ToList()
            : [schemaName];
        var types = string.IsNullOrWhiteSpace(objectType) || objectType.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? LocalToolHelpers.DefaultSchemaSearchTypes
            : LocalToolHelpers.ResolveSchemaObjectTypes(objectType);
        var definitionsAdded = 0;

        foreach (var schema in schemas)
        {
            foreach (var type in types)
            {
                foreach (var dbObject in service.GetDbObjects(activeDatabase, schema, "", type).Take(50))
                {
                    if (sb.Length >= maxChars || definitionsAdded >= 250)
                        return TrimSchemaExport(sb, maxChars);

                    var definition = await GetObjectDefinitionAsync(
                        dbObject.Name,
                        type.ToStringEx(),
                        schema,
                        activeDatabase,
                        Math.Min(12000, maxChars - sb.Length)).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(definition) || definition.StartsWith("No definition", StringComparison.OrdinalIgnoreCase))
                        continue;

                    sb.AppendLine();
                    sb.AppendLine(definition);
                    definitionsAdded++;
                }
            }
        }

        return TrimSchemaExport(sb, maxChars);
    }

    private static string TrimSchemaExport(StringBuilder builder, int maxChars)
    {
        var text = builder.ToString();
        return text.Length <= maxChars ? text : text[..maxChars] + "\n-- [schema export truncated]";
    }

    [Description("Execute exact SQL without reading or returning result rows. The caller must obtain user approval first.")]
    public async Task<string> ExecuteSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "[Error: SQL cannot be empty.]";

        var context = _activeSqlContextProvider?.Invoke();
        if (context is null)
            return "[Error: No active database connection is selected.]";

        try
        {
            var service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, context.Value.ConnectionName);
            if (service is null)
                return $"[Error: Connection '{context.Value.ConnectionName}' is unavailable.]";

            await using var connection = service.GetConnection(context.Value.DatabaseName, pooling: false);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = service.CreateCommandFromConnection(connection);
            command.CommandText = sql;
            var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            _sqlExecutionErrorStore.Clear();
            return $"SQL executed successfully. No result rows were read. Affected rows: {affected}.";
        }
        catch (Exception ex)
        {
            _sqlExecutionErrorStore.Record(ex, connectionName: context.Value.ConnectionName, databaseName: context.Value.DatabaseName);
            _logger.TrackError(ex, isCrash: false);
            return $"SQL execution failed: {ex.Message}";
        }
    }

    [Description("Applies a SQL fix to the editor (preview + apply in one step). Provide the full corrected SQL as proposedSql.")]
    public Task<string> ApplySqlFix(string proposedSql) => ApplySqlFixAsync(proposedSql);

    private async Task<string> ApplySqlFixAsync(string proposedSql)
    {
        if (string.IsNullOrWhiteSpace(proposedSql)) return LocalSqlPatchResponseFormatter.ProposedSqlCannotBeEmptyMessage;

        var currentSql = await GetCurrentSqlAsync();
        if (LocalSqlEditorContextFormatter.IsUnavailableSqlMessage(currentSql)) return currentSql;

        if (string.Equals(currentSql, proposedSql, StringComparison.Ordinal))
            return LocalSqlPatchResponseFormatter.NoChangesDetectedMessage;

        if (_sqlEditorBufferUpdater is null) return LocalSqlPatchResponseFormatter.BufferUpdateUnavailableMessage;

        try
        {
            var applied = await UiThreadMarshal.InvokeAsync(() => _sqlEditorBufferUpdater.Invoke(proposedSql));

            if (!applied) return LocalSqlPatchResponseFormatter.PatchApplicationFailedMessage;

            var oldLineCount = currentSql.Split(["\r\n", "\n"], StringSplitOptions.None).Length;
            var newLineCount = proposedSql.Split(["\r\n", "\n"], StringSplitOptions.None).Length;
            return LocalSqlPatchResponseFormatter.FormatPatchApplied(oldLineCount, newLineCount);
        }
        catch (Exception ex)
        {
            return LocalSqlPatchResponseFormatter.FormatPatchApplicationFailed(ex.Message);
        }
    }

    #endregion

    #region Tool List Builder

    public List<AIFunction> BuildToolList()
    {
        return
        [
            AIFunctionFactory.Create(GetActiveDatabaseContext),
            AIFunctionFactory.Create(ListSchemas),
            AIFunctionFactory.Create(BrowseSchemaObjects),
            AIFunctionFactory.Create(SearchSchemaObjects),
            AIFunctionFactory.Create(GetObjectDefinition),
            AIFunctionFactory.Create(GetObjectColumns),
            AIFunctionFactory.Create(GetTableMetadata),
            AIFunctionFactory.Create(GetNetezzaReference),
            AIFunctionFactory.Create(GetCurrentSql),
            AIFunctionFactory.Create(GetCurrentSqlEditorContext),
            AIFunctionFactory.Create(GetDiagnostics),
            AIFunctionFactory.Create(GetLastExecutionError),
            AIFunctionFactory.Create(ExportSchema),
        ];
    }

    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson)
    {
        if (toolName.Equals("GetTableMetadata", StringComparison.OrdinalIgnoreCase)
            && DeserializeBool(argumentsJson, "includeStatsPreview", false))
        {
            return "[Blocked: AI tools cannot read table statistics or SQL result rows.]";
        }

        if (_toolExecutors.TryGetValue(toolName, out var executor))
        {
            return await executor(argumentsJson);
        }
        return $"[Error: Tool '{toolName}' not found]";
    }

    #endregion

    #region Private Helper Methods

    private string SanitizeToolResult(string result, string toolName)
    {
        if (string.IsNullOrEmpty(result)) return result;

        foreach (var pattern in LocalToolHelpers.SensitiveFieldPatterns)
        {
            if (result.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.TrackError(new InvalidOperationException(
                    $"SECURITY: Tool '{toolName}' returned data containing sensitive field pattern '{pattern}'."), isCrash: false);
                return $"[BLOCKED] Tool '{toolName}' attempted to return sensitive credential data.";
            }
        }

        return result;
    }

    private async Task<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> GetSqlEditorContextSnapshotAsync()
    {
        if (_sqlEditorContextProvider is null) return null;
        try
        {
            return await UiThreadMarshal.InvokeAsync(_sqlEditorContextProvider);
        }
        catch { return null; }
    }

    private bool TryGetActiveDatabaseService(
        out IDatabaseService? databaseService,
        out string connectionName,
        out string databaseName,
        out string errorMessage)
    {
        databaseService = null;
        connectionName = string.Empty;
        databaseName = string.Empty;
        errorMessage = string.Empty;

        try
        {
            if (_activeSqlContextProvider is null)
            {
                errorMessage = "No active SQL context provider is configured.";
                return false;
            }

            // Capture on UI without sync-over-async: Post + ManualResetEvent when off UI thread.
            (string ConnectionName, string DatabaseName)? context;
            if (Dispatcher.UIThread.CheckAccess())
            {
                context = _activeSqlContextProvider.Invoke();
            }
            else
            {
                (string ConnectionName, string DatabaseName)? captured = null;
                Exception? captureError = null;
                using var done = new ManualResetEventSlim(false);
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try { captured = _activeSqlContextProvider.Invoke(); }
                    catch (Exception ex) { captureError = ex; }
                    finally { done.Set(); }
                });
                if (!done.Wait(TimeSpan.FromSeconds(5))) // ManualResetEventSlim
                {
                    errorMessage = "Timed out waiting for active SQL context on UI thread.";
                    return false;
                }
                if (captureError is not null) throw captureError;
                context = captured;
            }

            if (context is null || string.IsNullOrWhiteSpace(context.Value.ConnectionName))
            {
                errorMessage = "No active SQL document/connection is available.";
                return false;
            }

            connectionName = context.Value.ConnectionName;
            databaseService = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
            if (databaseService is null)
            {
                errorMessage = $"Could not initialize database service for connection '{connectionName}'.";
                return false;
            }

            databaseName = string.IsNullOrWhiteSpace(context.Value.DatabaseName)
                ? databaseService.Database
                : context.Value.DatabaseName;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to resolve active database context: {ex.Message}";
            return false;
        }
    }

    private async Task<string> GetObjectSourceAsync(IDatabaseService service, string database, string? schema, string objectName, string? objectType)
    {
        var resolvedSchema = schema ?? string.Empty;

        TypeInDatabaseEnum? explicitType = null;
        if (!string.IsNullOrWhiteSpace(objectType))
            explicitType = DatabaseServiceHelpers.FromStringEx(objectType.Trim());

        if (string.IsNullOrWhiteSpace(resolvedSchema) && explicitType.HasValue)
            resolvedSchema = LocalToolHelpers.FindObjectSchema(service, database, objectName, explicitType.Value) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(resolvedSchema))
            resolvedSchema = service.GetSchemas(database, "").FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(resolvedSchema)) return string.Empty;

        var viewSource = await service.GetCreateViewText(database, resolvedSchema, objectName);
        if (!string.IsNullOrWhiteSpace(viewSource))
        {
            return viewSource;
        }

        var procSource = await service.GetCreateProcedureText(database, resolvedSchema, objectName, forceFreshCode: true);
        if (!string.IsNullOrWhiteSpace(procSource))
        {
            return procSource;
        }

        return string.Empty;
    }

    private string RunReadOnlySqlPreview(IDatabaseService service, string databaseName, string sql, int rowLimit)
    {
        try
        {
            using var connection = service.GetConnection(null, pooling: false);
            service.ChangeDatabaseIfNeeded(connection, databaseName);
            if (connection.State != System.Data.ConnectionState.Open) connection.Open();

            using var command = service.CreateCommandFromConnection(connection);
            command.CommandText = sql;
            command.CommandTimeout = 120;
            var sw = Stopwatch.StartNew();
            using var reader = command.ExecuteReader();
            sw.Stop();
            return LocalToolHelpers.FormatReaderPreview(reader, rowLimit, databaseName, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            return $"Statistics preview failed: {ex.Message}";
        }
    }

    #endregion
}

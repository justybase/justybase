using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Services.Fim;

/// <summary>
/// Builds a compact, deterministic schema-context comment block for FIM prompts:
/// one <c>-- table: name (col:type, …)</c> line per physical table referenced by the
/// statement near the caret, resolved from the in-memory schema snapshot (no database
/// round-trips) plus <c>-- cte:</c> lines for CTEs in scope.
/// </summary>
public static class FimSchemaHintBuilder
{
    public const int MaxTables = 8;

    /// <summary>
    /// Returns a schema hint for the statement containing <paramref name="caretOffset"/>,
    /// or <c>null</c> when nothing usable is found (no statement, unknown tables only,
    /// empty document, parse failure, …). The result is deterministic for identical
    /// (document, caret) pairs so llama.cpp prompt caching is not thrashing.
    /// </summary>
    public static string? Build(
        DocumentParsingCoordinator? parsingCoordinator,
        ISchemaProvider? schemaProvider,
        string documentUri,
        SqlDialect dialect,
        string documentText,
        int caretOffset,
        int maxHintChars)
    {
        ArgumentNullException.ThrowIfNull(parsingCoordinator);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        if (string.IsNullOrWhiteSpace(documentText) || maxHintChars <= 0)
        {
            return null;
        }

        ParseResult parse;
        try
        {
            parse = parsingCoordinator.GetOrCreate(documentUri, dialect).Parse(documentText);
        }
        catch
        {
            return null;
        }

        if (parse.Statements.Count == 0)
        {
            return null;
        }

        var statement = parse.Statements.LastOrDefault(s => s.Position.Absolute <= caretOffset);
        if (statement is null)
        {
            return null;
        }

        var tables = new List<TableInfo>();
        var ctes = new List<CteDefinition>();
        CollectReferences(statement, schemaProvider, tables, ctes);

        if (tables.Count == 0 && ctes.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var cte in ctes
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxTables))
        {
            lines.Add($"-- cte: {cte.Name}({FormatNameColumns(cte.Columns)})");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables
                     .OrderBy(QualifiedName, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(QualifiedName(table)))
            {
                continue;
            }

            var columns = FormatTableColumns(table.Columns);
            lines.Add(columns.Length == 0
                ? $"-- table: {QualifiedName(table)}"
                : $"-- table: {QualifiedName(table)}({columns})");
            if (lines.Count >= MaxTables + ctes.Count)
            {
                break;
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var hint = string.Join("\n", lines);
        return hint.Length <= maxHintChars ? hint : Truncate(lines, maxHintChars);
    }

    private static void CollectReferences(
        Statement statement,
        ISchemaProvider schema,
        List<TableInfo> tables,
        List<CteDefinition> ctes)
    {
        switch (statement)
        {
            case SelectStatement select:
                if (select.With?.Ctes is { } cteDefs)
                {
                    foreach (var cte in cteDefs)
                    {
                        ctes.Add(cte);
                        CollectReferences(cte.Query, schema, tables, ctes);
                    }
                }

                if (select.From is { } from)
                {
                    foreach (var tr in from)
                    {
                        CollectTableReference(tr, schema, tables, ctes);
                    }
                }

                if (select.CompoundSelects is { } compounds)
                {
                    foreach (var compound in compounds)
                    {
                        CollectReferences(compound, schema, tables, ctes);
                    }
                }

                break;
            case InsertStatement insert:
                AddTableName(insert.Target, schema, tables);
                if (insert.SourceQuery is not null)
                {
                    CollectReferences(insert.SourceQuery, schema, tables, ctes);
                }

                break;
            case UpdateStatement update:
                AddTableName(update.Target, schema, tables);
                if (update.From is { } updFrom)
                {
                    foreach (var tr in updFrom)
                    {
                        CollectTableReference(tr, schema, tables, ctes);
                    }
                }

                break;
            case DeleteStatement delete:
                AddTableName(delete.Target, schema, tables);
                if (delete.From is { } delFrom)
                {
                    foreach (var tr in delFrom)
                    {
                        CollectTableReference(tr, schema, tables, ctes);
                    }
                }

                break;
            case MergeStatement merge:
                AddTableName(merge.Target, schema, tables);
                CollectTableSource(merge.Source, schema, tables, ctes);
                break;
        }
    }

    private static void CollectTableReference(
        TableReference reference,
        ISchemaProvider schema,
        List<TableInfo> tables,
        List<CteDefinition> ctes)
    {
        CollectTableSource(reference.Source, schema, tables, ctes);
        if (reference.Joins is { } joins)
        {
            foreach (var join in joins)
            {
                CollectTableSource(join.Source, schema, tables, ctes);
            }
        }

        if (reference.Applies is { } applies)
        {
            foreach (var apply in applies)
            {
                CollectTableSource(apply.Source, schema, tables, ctes);
            }
        }
    }

    private static void CollectTableSource(
        TableSource source,
        ISchemaProvider schema,
        List<TableInfo> tables,
        List<CteDefinition> ctes)
    {
        if (source.Table is not null)
        {
            AddTableName(source.Table, schema, tables);
        }

        if (source.Subquery is not null)
        {
            CollectReferences(source.Subquery, schema, tables, ctes);
        }
    }

    private static void AddTableName(TableName name, ISchemaProvider schema, List<TableInfo> tables)
    {
        var info = schema.GetTable(name.Database, name.Schema, name.Name);
        if (info is not null)
        {
            tables.Add(info);
        }
    }

    private static string FormatTableColumns(IReadOnlyList<ColumnInfo>? columns)
    {
        if (columns is null || columns.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", columns.Select(c =>
            string.IsNullOrWhiteSpace(c.DataType) ? c.Name : $"{c.Name}:{c.DataType}"));
    }

    private static string FormatNameColumns(IReadOnlyList<string>? columns) =>
        columns is null || columns.Count == 0 ? string.Empty : string.Join(", ", columns);

    private static string QualifiedName(TableInfo table)
    {
        if (table.Database is not null && table.Schema is not null)
        {
            return $"{table.Database}.{table.Schema}.{table.Name}";
        }

        if (table.Database is not null)
        {
            return $"{table.Database}..{table.Name}";
        }

        return table.Schema is not null ? $"{table.Schema}.{table.Name}" : table.Name;
    }

    private static string Truncate(List<string> lines, int maxHintChars)
    {
        // Stable ordering — drop trailing lines first so the first tables are kept.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var joined = string.Join("\n", lines.Take(i + 1));
            if (joined.Length <= maxHintChars)
            {
                return joined;
            }
        }

        var first = lines[0];
        return first.Length <= maxHintChars ? first : first[..maxHintChars];
    }
}

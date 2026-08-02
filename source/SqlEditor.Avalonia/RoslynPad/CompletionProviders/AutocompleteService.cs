using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using System;
using System.Collections.Generic;
using System.Text;

namespace JustyBase.Helpers;

public sealed class AutocompleteService
{
    public IEnumerable<CompletionDataSql> GetWordsList(string input, Dictionary<string, List<string>> aliasDbTable,
        Dictionary<string, List<string>> subqueryHints,Dictionary<string, List<string>> withHints,
        Dictionary<string, List<string>> tempTableHints, IDatabaseService databaseService, string? databaseName
    )
    {
        input.GetDotsPositionsAndCount(out int lastDotIndex, out int howManyDots,out int firsDotIndex);
        if (howManyDots == 0 && input.Length <= 2)
        {
            yield break;
        }

        string firstWord = "";
        string middleWord = "";
        string lastWord = "";
        if (databaseService is null)
        {
            yield break;
        }
        bool TYPE3_DB_SCHEMA_OBJECT = ((databaseService.AutoCompletDatabaseMode & CurrentAutoCompletDatabaseMode.DatabaseSchemaTable) != CurrentAutoCompletDatabaseMode.NotSet);// DB.SCHEMA.TABLE type
        bool TYPE2_SCHEMA_OBJECT = ((databaseService.AutoCompletDatabaseMode & CurrentAutoCompletDatabaseMode.SchemaTable) != CurrentAutoCompletDatabaseMode.NotSet);// SCHEMA.TABLE type
        bool TYPE_SCHEMA_OPTIONAL = ((databaseService.AutoCompletDatabaseMode & CurrentAutoCompletDatabaseMode.DatabaseAndSchemaOptional) != CurrentAutoCompletDatabaseMode.NotSet);
        bool TYPE_SCHEMA_CAN_BE_NULL = ((databaseService.AutoCompletDatabaseMode & CurrentAutoCompletDatabaseMode.NullSchemaCanBeAccepted) != CurrentAutoCompletDatabaseMode.NotSet);

        string? TEMP_DB = null;
        if (TYPE3_DB_SCHEMA_OBJECT)
        {
            TEMP_DB = databaseName;
        }

        if (firsDotIndex != -1)
        {
            firstWord = input[..firsDotIndex];
        }

        if (lastDotIndex != firsDotIndex)
        {
            middleWord = input[(firsDotIndex + 1)..lastDotIndex];
            lastWord = input[(lastDotIndex + 1)..];
        }
        else
        {
            lastWord = input[(firsDotIndex + 1)..];
        }

        if (howManyDots == 0 && lastWord.Length > 0)
        {
            foreach (var item in subqueryHints.Keys)
            {
                if (item.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new CompletionDataSql(item, "subquery", false, Glyph.SubQuery, null);
                }
            }

            foreach (var item in aliasDbTable.Values)
            {
                foreach (var item2 in item)
                {
                    if (item2.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new CompletionDataSql(item2, "alias", false, Glyph.Table, null);
                    }
                }
            }

            foreach (var item in withHints.Keys)
            {

                if (item.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new CompletionDataSql(item, "with", false, Glyph.WithDb, null);
                }
            }

            foreach (var item in tempTableHints.Keys)
            {
                if (item.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new CompletionDataSql(item, "tempTable", false, Glyph.TempTable, null);
                }
            }

            foreach (var columnAutocomplete in getColumnAutocomplete(lastWord))
            {
                yield return columnAutocomplete;
            }
        }


        if (TYPE3_DB_SCHEMA_OBJECT && howManyDots == 0 && lastWord.Length > 0)
        {
            foreach (string item in databaseService.GetDatabases(lastWord))
            {
                yield return new CompletionDataSql(item, "database", false, Glyph.Database, null);
            }

            if (TYPE_SCHEMA_OPTIONAL && lastWord.Length >= 3)
            {
                var yieldedShortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string schemaName in databaseService.GetSchemas(TEMP_DB, ""))
                {
                    foreach (var itme in getSchemaObjectsForAutocomplete(TEMP_DB, schemaName, lastWord))
                    {
                        if (!yieldedShortNames.Add(itme.Text))
                            continue;
                        yield return itme;
                    }
                }
            }
        }

        if (TYPE2_SCHEMA_OBJECT && howManyDots == 0) // schema autocomplete
        {
            foreach (string item in databaseService.GetSchemas(TEMP_DB, lastWord))
            {
                yield return new CompletionDataSql(item, "schema", false, Glyph.Schema, null);
            }
        }

        if (howManyDots == 1)
        {
            if (subqueryHints.TryGetValue(firstWord, out var strings))
            {
                foreach (var item in strings)
                {
                    if (item.Contains(lastWord, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new CompletionDataSql(item, "subquert column", false, Glyph.None, null);
                    }
                }
            }
            if (withHints.TryGetValue(firstWord, out var strings2))
            {
                foreach (var item in strings2)
                {
                    if (item.Contains(lastWord, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new CompletionDataSql(item, "temp table column", false, Glyph.None, null);
                    }
                }
            }
            if (tempTableHints.TryGetValue(firstWord, out var strings3))
            {
                foreach (var item in strings3)
                {
                    if (item.Contains(lastWord, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new CompletionDataSql(item, "temp table column", false, Glyph.Table, null);
                    }
                }
            }

            foreach (KeyValuePair<string, List<string>> longName in aliasDbTable)
            {
                var parts = longName.Key.Split('.');
                foreach (string alias in longName.Value)
                {
                    if (parts.Length == 1 && firstWord.Equals(alias, StringComparison.OrdinalIgnoreCase) && withHints.TryGetValue(longName.Key, out var colList))
                    {
                        foreach (var item in colList)
                        {
                            yield return new CompletionDataSql(item, "with column", false, Glyph.None, null);
                        }
                    }
                    else if (parts.Length == 1 && firstWord.Equals(alias, StringComparison.OrdinalIgnoreCase) && tempTableHints.TryGetValue(longName.Key, out var colList2))
                    {
                        foreach (var item in colList2)
                        {
                            yield return new CompletionDataSql(item, "temp table column", false, Glyph.None, null);
                        }
                    }
                }
            }
        }

        if (TYPE2_SCHEMA_OBJECT && howManyDots == 1)
        {
            foreach (var item in getSchemaObjectsForAutocomplete(TEMP_DB, firstWord, lastWord))
            {
                yield return item;
            }
        }

        if (TYPE3_DB_SCHEMA_OBJECT && howManyDots == 1)
        {
            foreach (string item in databaseService.GetSchemas(firstWord, lastWord))
            {
                yield return new CompletionDataSql(item, "schema", false, Glyph.Schema, null);
            }
        }

        if ((TYPE3_DB_SCHEMA_OBJECT || TYPE2_SCHEMA_OBJECT) && howManyDots == 1)
        {
            foreach (var longName in aliasDbTable)
            {
                foreach (string alias in longName.Value)
                {
                    if (!firstWord.Equals(alias, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!TryParseAliasDbTableKey(longName.Key, TYPE2_SCHEMA_OBJECT, TYPE_SCHEMA_OPTIONAL, TEMP_DB,
                            out var db1, out var sch1, out var obj1))
                        continue;

                    //handle "word", WORD, word, Word etc. in netezza.
                    db1 = databaseService.CleanSqlWord(db1, databaseService.AutoCompletDatabaseMode);
                    sch1 = databaseService.CleanSqlWord(sch1, databaseService.AutoCompletDatabaseMode);
                    obj1 = databaseService.CleanSqlWord(obj1, databaseService.AutoCompletDatabaseMode);

                    foreach (var item in databaseService.GetColumns(db1, sch1, obj1, lastWord))
                    {
                        yield return new CompletionDataSql(
                            item.Name,
                            item.FullTypeName,
                            false,
                            Glyph.Column,
                            null,
                            detailText: item.FullTypeName,
                            descriptionText: item.Desc);
                    }
                }
            }
        }

        if (TYPE3_DB_SCHEMA_OBJECT && howManyDots == 2 &&
            (!string.IsNullOrEmpty(middleWord) || TYPE_SCHEMA_CAN_BE_NULL))
        {
            foreach (var item in getSchemaObjectsForAutocomplete(firstWord, middleWord, lastWord))
            {
                yield return item;
            }
        }

        IEnumerable<CompletionDataSql> getSchemaObjectsForAutocomplete(string firstWord, string middleWord, string lastWord)
        {
            var listOfTypes = new TypeInDatabaseEnum[] { TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View, TypeInDatabaseEnum.Synonym, TypeInDatabaseEnum.Procedure, TypeInDatabaseEnum.ExternalTable };
            foreach (var type in listOfTypes)
            {
                foreach (var item in databaseService.GetDbObjects(firstWord, middleWord, lastWord, type))
                {
                    Glyph g = item.TypeInDatabase switch
                    {
                        TypeInDatabaseEnum.Table => Glyph.Table,
                        TypeInDatabaseEnum.View => Glyph.View,
                        TypeInDatabaseEnum.Procedure => Glyph.Procedure,
                        TypeInDatabaseEnum.Synonym => Glyph.Synonym,
                        TypeInDatabaseEnum.ExternalTable => Glyph.ExternalTable,
                        _ => Glyph.None
                    };

                    yield return new CompletionDataSql(
                        item.Name,
                        prepareDesc(item.Desc),
                        false,
                        g,
                        null,
                        detailText: g switch
                        {
                            Glyph.View => "View",
                            Glyph.Table => "Table",
                            Glyph.Procedure => "Procedure",
                            Glyph.Synonym => "Synonym",
                            Glyph.ExternalTable => "External",
                            _ => null
                        },
                        descriptionText: item.Desc);
                }
            }
        }

        string prepareDesc(string? descProposal)
        {
            if (descProposal is null)
            {
                return "no object desc";
            }
            if (descProposal.Length >= 2_048)
            {
                StringBuilder sb = new(2_048);
                sb.Append(descProposal.AsSpan()[..^3]);
                sb.Append("...");
                return sb.ToString();
            }
            return descProposal;
        }

        IEnumerable<CompletionDataSql> getColumnAutocomplete(string lastWord)
        {
            foreach (var item in aliasDbTable.Keys)
            {
                if (!TryParseAliasDbTableKey(item, TYPE2_SCHEMA_OBJECT, TYPE_SCHEMA_OPTIONAL, TEMP_DB,
                        out var partDatabase, out var partSchema, out var partObject))
                    continue;

                foreach (var alias in aliasDbTable[item])
                {
                    var alias2 = alias == "" ? partObject : alias;
                    foreach (var item2 in databaseService.GetColumns(partDatabase, partSchema, partObject, lastWord))
                    {
                        yield return new CompletionDataSql(
                            alias2 + "." + item2.Name,
                            item2.FullTypeName,
                            false,
                            Glyph.Column,
                            null,
                            detailText: item2.FullTypeName,
                            descriptionText: item2.Desc);
                    }
                }
            }
        }

    }

    internal static bool TryParseAliasDbTableKey(
        string key,
        bool type2SchemaObject,
        bool typeSchemaOptional,
        string? tempDb,
        out string? database,
        out string? schema,
        out string objectName)
    {
        database = null;
        schema = null;
        objectName = key;

        int dd = key.IndexOf("..", StringComparison.Ordinal);
        if (dd > 0 && !key.AsSpan(0, dd).Contains('.'))
        {
            database = key[..dd];
            objectName = key[(dd + 2)..];
            return true;
        }

        var parts = key.Split('.');
        if (parts.Length == 3)
        {
            database = parts[0];
            schema = parts[1];
            objectName = parts[2];
            return true;
        }

        if (parts.Length == 2 && type2SchemaObject)
        {
            database = tempDb;
            schema = parts[0];
            objectName = parts[1];
            return true;
        }

        if (parts.Length == 1 && typeSchemaOptional)
        {
            database = tempDb;
            objectName = parts[0];
            return true;
        }

        return false;
    }

}


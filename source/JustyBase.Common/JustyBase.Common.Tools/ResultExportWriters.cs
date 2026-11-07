using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using JustyBase.PluginCommon.Enums;
using Sylvan.Data.Csv;

namespace JustyBase.Common.Tools;

/// <summary>
/// Additional streaming result exporters (JSON, XML, TSV and SQL INSERT scripts).
/// CSV/Parquet/Excel stay in their existing host writers; these formats share the
/// same open reader and never materialize the whole result set in memory.
/// </summary>
public static class ResultExportWriters
{
    public static async Task WriteJsonAsync(
        DbDataReader reader,
        string filePath,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        if (reader.FieldCount > 0)
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    writer.WritePropertyName(reader.GetName(index));
                    WriteJsonValue(writer, reader.IsDBNull(index) ? null : reader.GetValue(index));
                }

                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken);
            }
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
    }

    public static async Task WriteXmlAsync(
        DbDataReader reader,
        string filePath,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = encoding,
            CloseOutput = true,
        };
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = XmlWriter.Create(stream, settings);
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "results", null);
        if (reader.FieldCount > 0)
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteStartElementAsync(null, "row", null);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    await writer.WriteStartElementAsync(null, "column", null);
                    await writer.WriteAttributeStringAsync(null, "name", null, reader.GetName(index));
                    if (!reader.IsDBNull(index))
                    {
                        await writer.WriteStringAsync(
                            Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? "");
                    }

                    await writer.WriteEndElementAsync();
                }

                await writer.WriteEndElementAsync();
            }
        }

        await writer.WriteEndElementAsync();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
    }

    public static Task WriteTsvAsync(
        DbDataReader reader,
        string filePath,
        Encoding encoding,
        bool header = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Task.Run(() =>
        {
            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, encoding);
            using var csv = CsvDataWriter.Create(writer, new CsvDataWriterOptions
            {
                Delimiter = '\t',
                NewLine = "\n",
                WriteHeaders = header,
            });
            csv.Write(reader);
        }, cancellationToken);
    }

    /// <summary>
    /// Writes a self-contained SQL script: optional DDL followed by one INSERT per row.
    /// Values are rendered with the target engine's literal syntax.
    /// </summary>
    public static async Task<long> WriteSqlScriptAsync(
        DbDataReader reader,
        string filePath,
        string insertTarget,
        DatabaseTypeEnum databaseType,
        string? ddl = null,
        Encoding? encoding = null,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(insertTarget);

        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, encoding);

        if (!string.IsNullOrWhiteSpace(ddl))
        {
            await writer.WriteAsync(ddl.TrimEnd().AsMemory(), cancellationToken);
            await writer.WriteAsync("\n\n".AsMemory(), cancellationToken);
        }

        await writer.WriteLineAsync("-- Table data as SQL INSERT statements".AsMemory(), cancellationToken);
        await writer.WriteLineAsync().ConfigureAwait(false);

        if (reader.FieldCount <= 0)
        {
            await writer.FlushAsync(cancellationToken);
            return 0;
        }

        string columns = string.Join(
            ", ",
            Enumerable.Range(0, reader.FieldCount).Select(index => QuoteIdentifier(reader.GetName(index))));

        long rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows % 250 == 0)
            {
                progress?.Invoke(rows);
            }

            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.IsDBNull(index)
                    ? "NULL"
                    : FormatLiteral(reader.GetValue(index), databaseType);
            }

            await writer.WriteAsync(
                $"INSERT INTO {insertTarget} ({columns}) VALUES ({string.Join(", ", values)});".AsMemory(),
                cancellationToken);
            await writer.WriteLineAsync().ConfigureAwait(false);
            rows++;
        }

        progress?.Invoke(rows);
        await writer.FlushAsync(cancellationToken);
        return rows;
    }

    public static string FormatLiteral(object value, DatabaseTypeEnum databaseType)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            DBNull => "NULL",
            string text => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'",
            char character => $"'{character.ToString().Replace("'", "''", StringComparison.Ordinal)}'",
            bool flag => FormatBoolean(flag, databaseType),
            byte[] bytes => FormatBinary(bytes, databaseType),
            DateTime dateTime => $"'{dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}'",
            DateTimeOffset offset => $"'{offset.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)}'",
            DateOnly date => $"'{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'",
            TimeOnly time => $"'{time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}'",
            TimeSpan span => $"'{span.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)}'",
            Guid guid => $"'{guid:D}'",
            float single => single.ToString("R", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            decimal decimalNumber => decimalNumber.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => $"'{value.ToString()?.Replace("'", "''", StringComparison.Ordinal)}'",
        };
    }

    private static string QuoteIdentifier(string name)
        => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string FormatBoolean(bool value, DatabaseTypeEnum databaseType)
        => databaseType is DatabaseTypeEnum.DB2 or DatabaseTypeEnum.Sqlite or DatabaseTypeEnum.MsSqlTrusted
            ? (value ? "1" : "0")
            : (value ? "TRUE" : "FALSE");

    private static string FormatBinary(byte[] bytes, DatabaseTypeEnum databaseType)
    {
        string hex = Convert.ToHexString(bytes);
        return databaseType switch
        {
            DatabaseTypeEnum.DB2 => $"BX'{hex}'",
            DatabaseTypeEnum.Sqlite => $"X'{hex}'",
            DatabaseTypeEnum.PostgreSql => $"decode('{hex}', 'hex')",
            DatabaseTypeEnum.Oracle => $"HEXTORAW('{hex}')",
            _ => $"X'{hex}'",
        };
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                writer.WriteNullValue();
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong unsigned:
                writer.WriteNumberValue(unsigned);
                break;
            case float single:
                writer.WriteNumberValue(single);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal decimalNumber:
                writer.WriteNumberValue(decimalNumber);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            case DateTimeOffset offset:
                writer.WriteStringValue(offset);
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}

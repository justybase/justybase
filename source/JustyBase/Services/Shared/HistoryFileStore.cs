using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustyBase.Common.Services;

/// <summary>
/// Durable append-only storage for SQL history. Each record is independently
/// framed and checksummed, so a truncated or corrupt record cannot make later
/// records unreadable.
/// </summary>
internal sealed class HistoryFileStore
{
    private const int HeaderLength = 8;
    private const int MarkerLength = 4;
    private const int FrameHeaderLength = MarkerLength + sizeof(int);
    private const int FrameTrailerLength = sizeof(uint);
    private const int MaxPayloadLength = 16 * 1024 * 1024;

    private static readonly byte[] Header = "JBHIST2\0"u8.ToArray();
    private static readonly byte[] Marker = "JBFR"u8.ToArray();

    private readonly string _filePath;
    private readonly string[] _sidecarPaths;

    public HistoryFileStore(string filePath, params string[] sidecarPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _sidecarPaths = sidecarPaths;
    }

    public IReadOnlyList<HistoryFileRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var fileInfo = new FileInfo(_filePath);
        if (fileInfo.Length == 0)
        {
            return [];
        }

        bool hasCurrentHeader;
        using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            hasCurrentHeader = HasCurrentHeader(stream);
        }

        if (!hasCurrentHeader)
        {
            MigrateLegacyFile();
            return [];
        }

        using var currentStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadFrames(currentStream);
    }

    public void Append(HistoryFileRecord record)
    {
        EnsureCurrentFile();

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(record, HistoryFileJsonContext.Default.HistoryFileRecord);
        if (payload.Length > MaxPayloadLength)
        {
            throw new InvalidDataException("History record is too large.");
        }

        byte[] frame = new byte[FrameHeaderLength + payload.Length + FrameTrailerLength];
        Marker.CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(MarkerLength), payload.Length);
        payload.CopyTo(frame, FrameHeaderLength);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(FrameHeaderLength + payload.Length),
            ComputeCrc32(payload));

        using var stream = new FileStream(
            _filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.WriteThrough);
        stream.Write(frame);
        stream.Flush(flushToDisk: true);
    }

    private void EnsureCurrentFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        if (!File.Exists(_filePath) || new FileInfo(_filePath).Length == 0)
        {
            using var stream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (stream.Length == 0)
            {
                stream.Write(Header);
                stream.Flush(flushToDisk: true);
            }

            return;
        }

        using var readStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (!HasCurrentHeader(readStream))
        {
            MigrateLegacyFile();
        }
    }

    private static bool HasCurrentHeader(Stream stream)
    {
        if (stream.Length < HeaderLength)
        {
            return false;
        }

        stream.Position = 0;
        Span<byte> buffer = stackalloc byte[HeaderLength];
        stream.ReadExactly(buffer);
        return buffer.SequenceEqual(Header);
    }

    private static List<HistoryFileRecord> ReadFrames(Stream stream)
    {
        var result = new List<HistoryFileRecord>();
        stream.Position = HeaderLength;

        while (TryFindNextMarker(stream, out long frameStart))
        {
            Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
            if (!TryReadExactly(stream, lengthBuffer))
            {
                break;
            }

            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            long payloadStart = stream.Position;
            long frameEnd = payloadStart + payloadLength + FrameTrailerLength;
            if (payloadLength < 0 || payloadLength > MaxPayloadLength || frameEnd > stream.Length)
            {
                stream.Position = frameStart + 1;
                continue;
            }

            byte[] payload = new byte[payloadLength];
            stream.ReadExactly(payload);
            Span<byte> crcBuffer = stackalloc byte[sizeof(uint)];
            stream.ReadExactly(crcBuffer);
            uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(crcBuffer);
            if (ComputeCrc32(payload) != expectedCrc)
            {
                stream.Position = frameStart + 1;
                continue;
            }

            try
            {
                HistoryFileRecord? record = JsonSerializer.Deserialize(
                    payload,
                    HistoryFileJsonContext.Default.HistoryFileRecord);
                if (record is not null)
                {
                    result.Add(record);
                }
            }
            catch (JsonException)
            {
                stream.Position = frameStart + 1;
            }
        }

        return result;
    }

    private static bool TryFindNextMarker(Stream stream, out long markerStart)
    {
        int matched = 0;
        while (stream.ReadByte() is int value and >= 0)
        {
            if (value == Marker[matched])
            {
                matched++;
                if (matched == MarkerLength)
                {
                    markerStart = stream.Position - MarkerLength;
                    return true;
                }
            }
            else
            {
                matched = value == Marker[0] ? 1 : 0;
            }
        }

        markerStart = -1;
        return false;
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private void MigrateLegacyFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        string token = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        string backupPath = $"{_filePath}.legacy-{token}.bak";

        File.Copy(_filePath, backupPath, overwrite: false);
        foreach (string sidecarPath in _sidecarPaths)
        {
            if (File.Exists(sidecarPath))
            {
                File.Copy(sidecarPath, $"{sidecarPath}.legacy-{token}.bak", overwrite: false);
            }
        }

        string temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Header);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            ResetSidecars();
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void ResetSidecars()
    {
        foreach (string sidecarPath in _sidecarPaths)
        {
            if (!File.Exists(sidecarPath))
            {
                continue;
            }

            string temporaryPath = $"{sidecarPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, "[]", Encoding.UTF8);
                File.Move(temporaryPath, sidecarPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}

internal sealed class HistoryFileRecord
{
    [JsonPropertyName("DateBinary")]
    public long DateBinary { get; set; }

    [JsonPropertyName("Sql")]
    public string Sql { get; set; } = "";

    [JsonPropertyName("Database")]
    public string Database { get; set; } = "";

    [JsonPropertyName("Connection")]
    public string Connection { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(HistoryFileRecord))]
internal partial class HistoryFileJsonContext : JsonSerializerContext
{
}

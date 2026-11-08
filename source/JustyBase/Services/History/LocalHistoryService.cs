using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JustyBase.Services.Files;

namespace JustyBase.Services.History;

/// <summary>One version of a file that Local History kept.</summary>
public sealed record LocalHistoryEntry(
    long Id,
    string Path,
    string Hash,
    DateTimeOffset RecordedAt,
    string Reason,
    long Length);

/// <summary>
/// Keeps automatic versions of the files the editor touches, so a script can be compared with or
/// restored to an earlier state even when the change happened outside JustyBase. Versions are
/// content-addressed (SHA-256), gzip-compressed, and deduplicated: saving the same text twice keeps
/// one blob. The index is plain JSON and is written atomically through
/// <see cref="Files.SafeFileWriter"/>.
/// </summary>
public interface ILocalHistoryService
{
    /// <summary>The folder versions are kept in.</summary>
    string RootPath { get; }

    /// <summary>
    /// Records <paramref name="text"/> as a version of <paramref name="filePath"/>. Returns
    /// <see langword="null"/> when the last recorded version already has exactly this text.
    /// </summary>
    Task<LocalHistoryEntry?> RecordAsync(
        string filePath,
        string text,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Every kept version of a file, newest first.</summary>
    Task<IReadOnlyList<LocalHistoryEntry>> GetEntriesAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>The kept text of <paramref name="entry"/>, or null when its blob is gone.</summary>
    Task<string?> ReadTextAsync(LocalHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="entry"/>'s text back to its file (recording the current content
    /// first, so a restore can itself be undone) and returns the text that was written.
    /// </summary>
    Task<string?> RestoreAsync(LocalHistoryEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>The serialized index. A class (not a record) so trimming keeps a parameterless shape.</summary>
internal sealed class LocalHistoryIndex
{
    public int Version { get; set; } = 1;

    public long NextId { get; set; } = 1;

    public List<LocalHistoryEntry> Entries { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalHistoryIndex))]
internal sealed partial class LocalHistoryJsonContext : JsonSerializerContext
{
}

/// <summary>Default implementation. All writes are serialized through one gate; reads load the index
/// once and answer from memory.</summary>
public sealed class LocalHistoryService : ILocalHistoryService, IDisposable
{
    /// <summary>How many versions one file keeps; the oldest fall away first.</summary>
    public const int MaxEntriesPerFile = 40;

    /// <summary>Upper bound on all kept versions, whatever the number of files.</summary>
    public const int MaxEntriesTotal = 2_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalHistoryIndex? _index;
    private bool _disposed;

    public LocalHistoryService()
        : this(Path.Combine(Common.Contracts.IGeneralApplicationData.ConfigDirectoryEvo, "localHistory"))
    {
    }

    public LocalHistoryService(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        BlobsPath = Path.Combine(RootPath, "blobs");
        IndexPath = Path.Combine(RootPath, "index.json");
    }

    public string RootPath { get; }

    private string BlobsPath { get; }

    private string IndexPath { get; }

    public async Task<LocalHistoryEntry?> RecordAsync(
        string filePath,
        string text,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string path = Normalize(filePath);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalHistoryIndex index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);

            LocalHistoryEntry? lastForFile = index.Entries
                .LastOrDefault(entry => PathsEqual(entry.Path, path));
            if (lastForFile is not null && string.Equals(lastForFile.Hash, hash, StringComparison.Ordinal))
            {
                return null;
            }

            Directory.CreateDirectory(BlobsPath);
            string blobPath = BlobPath(hash);
            if (!File.Exists(blobPath))
            {
                await WriteBlobAsync(blobPath, bytes, cancellationToken).ConfigureAwait(false);
            }

            var entry = new LocalHistoryEntry(
                index.NextId++,
                path,
                hash,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(reason) ? "change" : reason,
                bytes.LongLength);

            index.Entries.Add(entry);
            Prune(index);
            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LocalHistoryEntry>> GetEntriesAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string path = Normalize(filePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LocalHistoryIndex index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            return index.Entries
                .Where(entry => PathsEqual(entry.Path, path))
                .OrderByDescending(entry => entry.Id)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ReadTextAsync(
        LocalHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string blobPath = BlobPath(entry.Hash);
        if (!File.Exists(blobPath))
        {
            return null;
        }

        await using FileStream file = new(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> RestoreAsync(
        LocalHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string? text = await ReadTextAsync(entry, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return null;
        }

        string path = entry.Path;
        if (File.Exists(path))
        {
            try
            {
                TextFileSnapshot current = await SafeFileWriter.ReadAsync(path, cancellationToken).ConfigureAwait(false);
                await RecordAsync(path, current.Text, "before restore", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The file may be unreadable; the restore below still reports a useful error.
            }
        }

        TextFileFormat? format = null;
        if (File.Exists(path))
        {
            try
            {
                format = (await SafeFileWriter.ReadAsync(path, cancellationToken).ConfigureAwait(false)).Format;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Fall back to the default format.
            }
        }

        await SafeFileWriter.WriteAsync(path, text, format, cancellationToken).ConfigureAwait(false);
        await RecordAsync(path, text, "restore", cancellationToken).ConfigureAwait(false);
        return text;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task<LocalHistoryIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        if (_index is not null)
        {
            return _index;
        }

        if (!File.Exists(IndexPath))
        {
            _index = new LocalHistoryIndex();
            return _index;
        }

        try
        {
            await using FileStream stream = new(IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            LocalHistoryIndex? loaded = await JsonSerializer
                .DeserializeAsync(stream, LocalHistoryJsonContext.Default.LocalHistoryIndex, cancellationToken)
                .ConfigureAwait(false);
            _index = loaded ?? new LocalHistoryIndex();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or FormatException)
        {
            // A corrupt index must never block saves: start a fresh one next to the broken file.
            TryRenameBrokenIndex();
            _index = new LocalHistoryIndex();
        }

        return _index;
    }

    private async Task SaveIndexAsync(LocalHistoryIndex index, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootPath);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            index,
            LocalHistoryJsonContext.Default.LocalHistoryIndex);
        await SafeFileWriter.WriteAsync(IndexPath, Encoding.UTF8.GetString(json), TextFileFormat.Default, cancellationToken)
            .ConfigureAwait(false);
    }

    private void Prune(LocalHistoryIndex index)
    {
        var removed = new List<LocalHistoryEntry>();
        foreach (IGrouping<string, LocalHistoryEntry> group in index.Entries
                     .GroupBy(entry => entry.Path, PathComparer))
        {
            LocalHistoryEntry[] extra = group.OrderByDescending(entry => entry.Id)
                .Skip(MaxEntriesPerFile)
                .ToArray();
            removed.AddRange(extra);
        }

        if (index.Entries.Count - removed.Count > MaxEntriesTotal)
        {
            int excess = index.Entries.Count - removed.Count - MaxEntriesTotal;
            removed.AddRange(index.Entries
                .Except(removed)
                .OrderBy(entry => entry.Id)
                .Take(excess));
        }

        if (removed.Count == 0)
        {
            return;
        }

        var removedIds = removed.Select(entry => entry.Id).ToHashSet();
        index.Entries.RemoveAll(entry => removedIds.Contains(entry.Id));

        foreach (string hash in removed.Select(entry => entry.Hash).Distinct(StringComparer.Ordinal))
        {
            if (index.Entries.Any(entry => string.Equals(entry.Hash, hash, StringComparison.Ordinal)))
            {
                continue;
            }

            TryDeleteBlob(hash);
        }
    }

    private static async Task WriteBlobAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            {
                await gzip.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException)
            {
                // best effort
            }
            catch (UnauthorizedAccessException)
            {
                // best effort
            }
        }
    }

    private string BlobPath(string hash)
        => Path.Combine(BlobsPath, hash[..2], hash + ".gz");

    private void TryDeleteBlob(string hash)
    {
        try
        {
            string path = BlobPath(hash);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best effort: a stale blob costs space but is harmless
        }
        catch (UnauthorizedAccessException)
        {
            // best effort: a stale blob costs space but is harmless
        }
    }

    private void TryRenameBrokenIndex()
    {
        try
        {
            File.Move(IndexPath, IndexPath + ".bad", overwrite: true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static bool PathsEqual(string left, string right) => PathComparer.Equals(left, right);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

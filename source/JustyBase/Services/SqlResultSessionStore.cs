namespace JustyBase.Services;

/// <summary>Disk-backed spill for large result sets (tab-separated pages).</summary>
public sealed class SqlResultSessionStore
{
    private readonly string _rootDirectory;

    public SqlResultSessionStore()
    {
        _rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JustDataEvo",
            "result-sessions");
        Directory.CreateDirectory(_rootDirectory);
    }

    public string CreateSessionId() => Guid.NewGuid().ToString("N");

    public async Task WritePageAsync(string sessionId, int pageIndex, IReadOnlyList<string[]> rows, CancellationToken cancellationToken = default)
    {
        var path = GetPagePath(sessionId, pageIndex);
        var lines = rows.Select(row => string.Join('\t', row.Select(cell => cell.Replace('\t', ' '))));
        await File.WriteAllLinesAsync(path, lines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string[]>> ReadPageAsync(string sessionId, int pageIndex, CancellationToken cancellationToken = default)
    {
        var path = GetPagePath(sessionId, pageIndex);
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines.Select(line => line.Split('\t')).ToArray();
    }

    public void DeleteSession(string sessionId)
    {
        var dir = Path.Combine(_rootDirectory, sessionId);
        if (!Directory.Exists(dir))
            return;

        Directory.Delete(dir, recursive: true);
    }

    private string GetPagePath(string sessionId, int pageIndex)
    {
        var dir = Path.Combine(_rootDirectory, sessionId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"page-{pageIndex:D6}.tsv");
    }
}

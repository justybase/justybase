using JustyBase.Services.History;

namespace JustyBase.Tests;

public sealed class LocalHistoryServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "JustyBase.Tests",
        "history",
        Guid.NewGuid().ToString("N"));

    public LocalHistoryServiceTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string FilePath(string name = "script.sql") => Path.Combine(_folder, name);

    private LocalHistoryService CreateService() => new(Path.Combine(_folder, ".history"));

    [Fact]
    public async Task Record_DedupesConsecutiveSameText()
    {
        using LocalHistoryService history = CreateService();
        string path = FilePath();

        LocalHistoryEntry? first = await history.RecordAsync(path, "SELECT 1;", "save");
        LocalHistoryEntry? duplicate = await history.RecordAsync(path, "SELECT 1;", "save");
        LocalHistoryEntry? second = await history.RecordAsync(path, "SELECT 2;", "save");

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.NotNull(second);
        Assert.Equal(2, (await history.GetEntriesAsync(path)).Count);
    }

    [Fact]
    public async Task GetEntries_ReturnsNewestFirst()
    {
        using LocalHistoryService history = CreateService();
        string path = FilePath();

        await history.RecordAsync(path, "one", "save");
        await history.RecordAsync(path, "two", "save");
        await history.RecordAsync(path, "three", "save");

        IReadOnlyList<LocalHistoryEntry> entries = await history.GetEntriesAsync(path);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].Id > entries[1].Id);
        Assert.True(entries[1].Id > entries[2].Id);
        Assert.Equal("three", await history.ReadTextAsync(entries[0]));
        Assert.Equal("one", await history.ReadTextAsync(entries[2]));
    }

    [Fact]
    public async Task ReadText_RoundTripsUnicodeAndLargeText()
    {
        using LocalHistoryService history = CreateService();
        string path = FilePath();
        string text = "SELECT 'zażółć gęślą jaźń';\n" + new string('x', 200_000);

        LocalHistoryEntry entry = Assert.IsType<LocalHistoryEntry>(
            await history.RecordAsync(path, text, "save"));

        Assert.Equal(text, await history.ReadTextAsync(entry));
    }

    [Fact]
    public async Task Restore_WritesTheStoredVersionAndKeepsTheCurrentOne()
    {
        using LocalHistoryService history = CreateService();
        string path = FilePath();
        await File.WriteAllTextAsync(path, "SELECT 2;");

        LocalHistoryEntry entry = Assert.IsType<LocalHistoryEntry>(
            await history.RecordAsync(path, "SELECT 1;", "save"));

        await history.RestoreAsync(entry);

        Assert.Equal("SELECT 1;", await File.ReadAllTextAsync(path));
        IReadOnlyList<LocalHistoryEntry> entries = await history.GetEntriesAsync(path);
        Assert.Contains(entries, e => e.Reason == "before restore");
        Assert.Contains(entries, e => e.Reason == "restore");
    }

    [Fact]
    public async Task Record_PrunesOldVersionsPerFile()
    {
        using LocalHistoryService history = CreateService();
        string path = FilePath();

        for (int i = 0; i < LocalHistoryService.MaxEntriesPerFile + 7; i++)
        {
            await history.RecordAsync(path, $"SELECT {i};", "save");
        }

        IReadOnlyList<LocalHistoryEntry> entries = await history.GetEntriesAsync(path);
        Assert.Equal(LocalHistoryService.MaxEntriesPerFile, entries.Count);
        Assert.Equal($"SELECT {LocalHistoryService.MaxEntriesPerFile + 6};", await history.ReadTextAsync(entries[0]));
    }

    [Fact]
    public async Task Record_KeepsFilesApart_EvenWhenTextIsIdentical()
    {
        using LocalHistoryService history = CreateService();
        string first = FilePath("a.sql");
        string second = FilePath("b.sql");

        await history.RecordAsync(first, "SELECT 1;", "save");
        await history.RecordAsync(second, "SELECT 1;", "save");

        Assert.Single(await history.GetEntriesAsync(first));
        Assert.Single(await history.GetEntriesAsync(second));
    }
}

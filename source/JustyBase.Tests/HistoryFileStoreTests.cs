using System.Text;
using JustyBase.Common.Services;

namespace JustyBase.Tests;

public sealed class HistoryFileStoreTests
{
    [Fact]
    public void AppendAndLoad_RoundTripsUnicodeAndMultilineSql()
    {
        using var fixture = new HistoryFileFixture();
        var store = fixture.CreateStore();
        var expected = new HistoryFileRecord
        {
            DateBinary = new DateTime(2026, 8, 2, 12, 30, 0, DateTimeKind.Local).ToBinary(),
            Sql = "select 'zażółć'\nfrom таблица",
            Database = "baza",
            Connection = "połączenie",
        };

        store.Append(expected);

        HistoryFileRecord actual = Assert.Single(store.Load());
        Assert.Equal(expected.DateBinary, actual.DateBinary);
        Assert.Equal(expected.Sql, actual.Sql);
        Assert.Equal(expected.Database, actual.Database);
        Assert.Equal(expected.Connection, actual.Connection);
    }

    [Fact]
    public void Load_SkipsCorruptFrameAndReadsFollowingFrame()
    {
        using var fixture = new HistoryFileFixture();
        var store = fixture.CreateStore();
        store.Append(CreateRecord(1));
        store.Append(CreateRecord(2));

        byte[] bytes = File.ReadAllBytes(fixture.FilePath);
        bytes[16] ^= 0x7F;
        File.WriteAllBytes(fixture.FilePath, bytes);

        HistoryFileRecord actual = Assert.Single(store.Load());
        Assert.Equal(2, actual.DateBinary);
    }

    [Fact]
    public void Load_IgnoresTruncatedTail_AndAppendCanContinue()
    {
        using var fixture = new HistoryFileFixture();
        var store = fixture.CreateStore();
        store.Append(CreateRecord(1));

        using (var stream = new FileStream(fixture.FilePath, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.SetLength(stream.Length - 3);
        }

        Assert.Empty(store.Load());

        store.Append(CreateRecord(2));

        HistoryFileRecord actual = Assert.Single(store.Load());
        Assert.Equal(2, actual.DateBinary);
    }

    [Fact]
    public void Load_LegacyFile_CreatesBackupAndStartsEmpty()
    {
        using var fixture = new HistoryFileFixture();
        File.WriteAllBytes(fixture.FilePath, Encoding.UTF8.GetBytes("legacy history"));
        File.WriteAllText(fixture.FavoritesPath, "[\"old-favorite\"]");
        File.WriteAllText(fixture.RunsPath, "[{\"Key\":\"old-run\"}]");
        var store = fixture.CreateStore();

        Assert.Empty(store.Load());
        Assert.NotEmpty(Directory.GetFiles(fixture.DirectoryPath, "history.dat.zst.legacy-*.bak"));
        Assert.NotEmpty(Directory.GetFiles(fixture.DirectoryPath, "history.favorites.json.legacy-*.bak"));
        Assert.NotEmpty(Directory.GetFiles(fixture.DirectoryPath, "history.runs.json.legacy-*.bak"));
        Assert.Equal("[]", File.ReadAllText(fixture.FavoritesPath));
        Assert.Equal("[]", File.ReadAllText(fixture.RunsPath));
        Assert.Equal("JBHIST2\0", Encoding.UTF8.GetString(File.ReadAllBytes(fixture.FilePath), 0, 8));
    }

    private static HistoryFileRecord CreateRecord(long value) => new()
    {
        DateBinary = value,
        Sql = $"select {value}",
        Database = "db",
        Connection = "connection",
    };

    private sealed class HistoryFileFixture : IDisposable
    {
        public HistoryFileFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "JustyBase-History-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            FilePath = Path.Combine(DirectoryPath, "history.dat.zst");
            FavoritesPath = Path.Combine(DirectoryPath, "history.favorites.json");
            RunsPath = Path.Combine(DirectoryPath, "history.runs.json");
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }
        public string FavoritesPath { get; }
        public string RunsPath { get; }

        public HistoryFileStore CreateStore() => new(FilePath, FavoritesPath, RunsPath);

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}

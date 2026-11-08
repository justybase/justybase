using System.Text;
using JustyBase.Services.Files;

namespace JustyBase.Tests;

public sealed class SafeFileWriterTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "JustyBase.Tests",
        Guid.NewGuid().ToString("N"));

    public SafeFileWriterTests()
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

    [Fact]
    public async Task WriteAsync_CreatesMissingFolders_AndRoundTrips()
    {
        string path = Path.Combine(_folder, "nested", "script.sql");

        await SafeFileWriter.WriteAsync(path, "SELECT 1;\nSELECT 2;");

        Assert.True(File.Exists(path));
        TextFileSnapshot snapshot = SafeFileWriter.Read(path);
        Assert.Equal("SELECT 1;" + Environment.NewLine + "SELECT 2;", snapshot.Text);
        Assert.False(snapshot.Format.HasBom);
        Assert.Equal(65001, snapshot.Format.Encoding.CodePage);
    }

    [Fact]
    public async Task WriteAsync_KeepsUtf8Bom()
    {
        string path = Path.Combine(_folder, "bom.sql");
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = Encoding.UTF8.GetBytes("SELECT 'zażółć';\r\n");
        await File.WriteAllBytesAsync(path, [.. bom, .. body]);

        TextFileSnapshot snapshot = SafeFileWriter.Read(path);
        Assert.True(snapshot.Format.HasBom);
        Assert.Equal(TextLineEnding.CrLf, snapshot.Format.LineEnding);

        await SafeFileWriter.WriteAsync(path, "SELECT 'zmiana';\r\n", snapshot.Format);

        byte[] written = await File.ReadAllBytesAsync(path);
        Assert.True(written.AsSpan().StartsWith(bom));
        Assert.Equal("SELECT 'zmiana';\r\n", Encoding.UTF8.GetString(written, bom.Length, written.Length - bom.Length));
        Assert.DoesNotContain("zażółć", Encoding.UTF8.GetString(written));
    }

    [Fact]
    public async Task WriteAsync_KeepsUtf16LittleEndian()
    {
        string path = Path.Combine(_folder, "utf16.sql");
        byte[] bom = [0xFF, 0xFE];
        byte[] body = Encoding.Unicode.GetBytes("SELECT 1;\r\n");
        await File.WriteAllBytesAsync(path, [.. bom, .. body]);

        TextFileSnapshot snapshot = SafeFileWriter.Read(path);
        Assert.Equal(1200, snapshot.Format.Encoding.CodePage);
        Assert.True(snapshot.Format.HasBom);

        await SafeFileWriter.WriteAsync(path, "SELECT 2;\r\n", snapshot.Format);

        byte[] written = await File.ReadAllBytesAsync(path);
        Assert.True(written.AsSpan().StartsWith(bom));
        Assert.Equal("SELECT 2;\r\n", Encoding.Unicode.GetString(written, bom.Length, written.Length - bom.Length));
    }

    [Fact]
    public async Task WriteAsync_NormalizesLineEndingsToDetectedFormat()
    {
        string path = Path.Combine(_folder, "crlf.sql");
        await File.WriteAllTextAsync(path, "a\r\nb\r\n", new UTF8Encoding(false));
        TextFileFormat format = SafeFileWriter.Read(path).Format;

        await SafeFileWriter.WriteAsync(path, "one\ntwo\nthree", format);

        Assert.Equal("one\r\ntwo\r\nthree", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_RefusesReadOnlyFile()
    {
        string path = Path.Combine(_folder, "readonly.sql");
        await File.WriteAllTextAsync(path, "SELECT 1;");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        await Assert.ThrowsAsync<ReadOnlyFileException>(
            () => SafeFileWriter.WriteAsync(path, "SELECT 2;"));

        Assert.Equal("SELECT 1;", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_LeavesNoTemporaryFile()
    {
        string path = Path.Combine(_folder, "clean.sql");
        await SafeFileWriter.WriteAsync(path, "SELECT 1;");
        await SafeFileWriter.WriteAsync(path, "SELECT 2;");

        string[] leftovers = Directory.GetFiles(_folder, "*.tmp");
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task Read_FallsBackToLatin1_ForInvalidUtf8()
    {
        string path = Path.Combine(_folder, "latin1.sql");
        byte[] bytes = [.. Encoding.ASCII.GetBytes("SELECT '"), 0xB1, .. Encoding.ASCII.GetBytes("';")];
        await File.WriteAllBytesAsync(path, bytes);

        TextFileSnapshot snapshot = SafeFileWriter.Read(path);

        Assert.Equal(28591, snapshot.Format.Encoding.CodePage);
        Assert.Equal("SELECT '±';", snapshot.Text);
        await SafeFileWriter.WriteAsync(path, snapshot.Text, snapshot.Format);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Write_PromotesLatin1ToUtf8_WhenTheTextNoLongerFits()
    {
        string path = Path.Combine(_folder, "latin1-promote.sql");
        byte[] bytes = [.. Encoding.ASCII.GetBytes("SELECT '"), 0xB1, .. Encoding.ASCII.GetBytes("';")];
        await File.WriteAllBytesAsync(path, bytes);
        TextFileSnapshot snapshot = SafeFileWriter.Read(path);

        await SafeFileWriter.WriteAsync(path, "SELECT 'ż';", snapshot.Format);

        byte[] written = await File.ReadAllBytesAsync(path);
        Assert.Equal("SELECT 'ż';", Encoding.UTF8.GetString(written));
        Assert.False(written.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }
}

using System.Text;

namespace JustyBase.Services.Files;

/// <summary>The line ending a text file uses. Kept with the file so saving writes it back the same way.</summary>
public enum TextLineEnding
{
    Lf,
    CrLf,
    Cr,
}

/// <summary>How a text file was stored: its encoding (without a preamble), whether it began with a
/// byte-order mark, and its line ending. A file read this way is written back the same way unless the
/// caller explicitly asks for another format.</summary>
public sealed record TextFileFormat(Encoding Encoding, bool HasBom, TextLineEnding LineEnding)
{
    /// <summary>The format used for a file that does not exist yet: UTF-8 without a BOM, the platform's line ending.</summary>
    public static TextFileFormat Default { get; } = new(
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        false,
        Environment.NewLine == "\r\n" ? TextLineEnding.CrLf : TextLineEnding.Lf);

    /// <summary>The bytes a byte-order mark occupies in this format, empty when the file has none.</summary>
    public byte[] GetPreamble() => HasBom
        ? Encoding.CodePage switch
        {
            65001 => [0xEF, 0xBB, 0xBF],
            1200 => [0xFF, 0xFE],
            1201 => [0xFE, 0xFF],
            12000 => [0xFF, 0xFE, 0x00, 0x00],
            12001 => [0x00, 0x00, 0xFE, 0xFF],
            _ => Encoding.GetPreamble(),
        }
        : [];

    /// <summary>The characters this format writes between lines.</summary>
    public string LineSeparator => LineEnding switch
    {
        TextLineEnding.CrLf => "\r\n",
        TextLineEnding.Cr => "\r",
        _ => "\n",
    };
}

/// <summary>A file's text together with the format it was read in.</summary>
public sealed record TextFileSnapshot(string Text, TextFileFormat Format);

/// <summary>Raised when a file could not be written because the file (or its folder) is read-only.</summary>
public sealed class ReadOnlyFileException(string path)
    : IOException($"The file '{path}' is read-only.")
{
    public string Path { get; } = path;
}

/// <summary>
/// One place that reads and writes text files. Reads detect the encoding, byte-order mark and line
/// ending; writes go through a temporary file in the same folder and replace the destination only
/// once every byte has landed, so a crash or a full disk never leaves a half-written script. A file
/// that another process holds open is retried for a short while, and a read-only file is refused
/// instead of silently failing.
/// </summary>
public static class SafeFileWriter
{
    /// <summary>How many times a write is retried while another process holds the destination open.</summary>
    public const int DefaultRetryCount = 6;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>Reads <paramref name="path"/> and detects its storage format.</summary>
    public static TextFileSnapshot Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    /// <summary>Reads <paramref name="path"/> asynchronously and detects its storage format.</summary>
    public static async Task<TextFileSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Decode(bytes);
    }

    /// <summary>Detects the format of <paramref name="bytes"/> without decoding the whole file first.</summary>
    public static TextFileFormat DetectFormat(ReadOnlySpan<byte> bytes)
    {
        foreach (TextFileFormat candidate in KnownFormats)
        {
            byte[] preamble = candidate.GetPreamble();
            if (preamble.Length > 0 && bytes.StartsWith(preamble))
            {
                TextLineEnding ending = DetectLineEnding(DecodeBody(bytes[preamble.Length..], candidate.Encoding));
                return candidate with { LineEnding = ending };
            }
        }

        Encoding encoding = IsValidUtf8(bytes) ? FallbackUtf8 : Encoding.Latin1;
        TextLineEnding lineEnding = DetectLineEnding(DecodeBody(bytes, encoding));
        return new TextFileFormat(encoding, HasBom: false, lineEnding);
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/> atomically, in
    /// <paramref name="format"/> (or <see cref="TextFileFormat.Default"/> for a new file). The
    /// text's line endings are normalized to the format's, so a file never mixes them.
    /// </summary>
    public static Task WriteAsync(
        string path,
        string text,
        TextFileFormat? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        TextFileFormat effective = format ?? TextFileFormat.Default;
        string fullPath = Path.GetFullPath(path);
        byte[] content = Encode(text, effective);

        return WriteBytesAsync(fullPath, content, cancellationToken);
    }

    /// <summary>Encodes <paramref name="text"/> exactly as <see cref="WriteAsync"/> would store it.</summary>
    public static byte[] Encode(string text, TextFileFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        TextFileFormat effective = format ?? TextFileFormat.Default;
        string normalized = NormalizeLineEndings(text, effective.LineSeparator);
        byte[] preamble = effective.GetPreamble();

        // A Latin-1 file cannot hold a character above U+00FF. Rather than write '?' and lose the
        // typed text, save such a file as UTF-8 (without a byte-order mark) so nothing is dropped.
        bool latin1CannotEncode = effective.Encoding.CodePage == 28591
            && normalized.Any(character => character > '\u00FF');
        byte[] body = latin1CannotEncode
            ? FallbackUtf8.GetBytes(normalized)
            : effective.Encoding.GetBytes(normalized);

        if (latin1CannotEncode)
        {
            preamble = [];
        }

        if (preamble.Length == 0)
        {
            return body;
        }

        byte[] result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    /// <summary>Replaces every line ending in <paramref name="text"/> with <paramref name="separator"/>.</summary>
    public static string NormalizeLineEndings(string text, string separator)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(separator);

        if (!text.Contains('\r') && separator == "\n")
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append(separator);
            }
            else if (c == '\n')
            {
                builder.Append(separator);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static async Task WriteBytesAsync(
        string fullPath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string? folder = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        FileAttributes? existingAttributes = null;
        if (File.Exists(fullPath))
        {
            existingAttributes = File.GetAttributes(fullPath);
            if ((existingAttributes.Value & FileAttributes.ReadOnly) != 0)
            {
                throw new ReadOnlyFileException(fullPath);
            }
        }

        string tempPath = Path.Combine(
            folder ?? ".",
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await RunWithRetryAsync(async () =>
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    // Flush to the device, not just the OS cache: the replacement below is only
                    // safe once the temporary file's bytes are durable.
                    stream.Flush(flushToDisk: true);
                }

                ReplaceDestination(tempPath, fullPath);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempPath);
        }

        if (existingAttributes is { } attributes)
        {
            try
            {
                File.SetAttributes(fullPath, attributes);
            }
            catch (IOException)
            {
                // Attributes are best effort; the content is already safe.
            }
            catch (UnauthorizedAccessException)
            {
                // Attributes are best effort; the content is already safe.
            }
        }
    }

    private static void ReplaceDestination(string tempPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(tempPath, destinationPath);
            return;
        }

        try
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (Exception exception) when (exception is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // File.Replace needs both paths on one volume and a filesystem that supports it.
            // The temporary file is in the destination folder, so a move is still a rename.
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    private static async Task RunWithRetryAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        TimeSpan delay = FirstRetryDelay;
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (IOException exception) when (attempt < DefaultRetryCount && IsSharingViolation(exception))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay *= 2;
            }
        }
    }

    private static bool IsSharingViolation(IOException exception)
        => exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless; never mask the real write result.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temporary file is harmless; never mask the real write result.
        }
    }

    private static TextFileSnapshot Decode(byte[] bytes)
    {
        TextFileFormat format = DetectFormat(bytes);
        int skip = format.GetPreamble().Length;
        string text = DecodeBody(bytes.AsSpan(skip), format.Encoding);
        return new TextFileSnapshot(text, format);
    }

    private static string DecodeBody(ReadOnlySpan<byte> body, Encoding encoding)
        => encoding.GetString(body);

    private static TextLineEnding DetectLineEnding(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (c == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return TextLineEnding.CrLf;
        }

        if (cr > lf && cr > 0)
        {
            return TextLineEnding.Cr;
        }

        return TextLineEnding.Lf;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly Encoding FallbackUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private static readonly TextFileFormat[] KnownFormats =
    [
        new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), HasBom: true, TextLineEnding.Lf),
        new(new UTF32Encoding(bigEndian: false, byteOrderMark: false), HasBom: true, TextLineEnding.Lf),
        new(new UTF32Encoding(bigEndian: true, byteOrderMark: false), HasBom: true, TextLineEnding.Lf),
        new(new UnicodeEncoding(bigEndian: false, byteOrderMark: false), HasBom: true, TextLineEnding.Lf),
        new(new UnicodeEncoding(bigEndian: true, byteOrderMark: false), HasBom: true, TextLineEnding.Lf),
    ];
}

using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using System.Diagnostics;
using System.Text;

namespace JustyBase.Services.Credentials;

/// <summary>
/// Linux Secret Service backend via the <c>secret-tool</c> CLI (libsecret).
/// No new dependencies, no guessed native ABI: passwords travel over stdin
/// (never argv), failures (missing tool, no D-Bus session, timeouts) degrade
/// to false so callers fall back to file storage. No-op off Linux.
/// </summary>
internal sealed class LinuxSecretToolStore : ICredentialSecretStore
{
    private const string ServiceName = "JustyBase";
    private const int TimeoutMs = 10_000;

    private static readonly string[] ProbePaths =
    [
        "secret-tool",
        "/usr/bin/secret-tool",
        "/usr/local/bin/secret-tool",
    ];

    private readonly ISimpleLogger _logger;
    private bool? _available;

    public LinuxSecretToolStore(ISimpleLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            _available ??= ProbeTool() is not null;
            return _available.Value;
        }
    }

    public bool TryGetSecret(string connectionName, out string? secret)
    {
        secret = null;
        if (!TryNormalize(connectionName, out string? key))
        {
            return false;
        }

        string? tool = ToolPath;
        if (tool is null)
        {
            return false;
        }

        try
        {
            using var process = Start(tool, "lookup service JustyBase account " + Quote(key), redirectInput: false);
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeoutMs))
            {
                Kill(process);
                return false;
            }

            if (process.ExitCode != 0)
            {
                return false;
            }

            secret = output.TrimEnd('\r', '\n');
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    public bool TrySetSecret(string connectionName, string secret)
    {
        if (!TryNormalize(connectionName, out string? key) || secret is null)
        {
            return false;
        }

        string? tool = ToolPath;
        if (tool is null)
        {
            return false;
        }

        try
        {
            using var process = Start(
                tool,
                $"store --label={Quote($"JustyBase {key}")} service JustyBase account " + Quote(key),
                redirectInput: true);
            process.StandardInput.Write(secret);
            process.StandardInput.Close();
            if (!process.WaitForExit(TimeoutMs))
            {
                Kill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    public bool TryRemoveSecret(string connectionName)
    {
        if (!TryNormalize(connectionName, out string? key))
        {
            return false;
        }

        string? tool = ToolPath;
        if (tool is null)
        {
            return false;
        }

        try
        {
            using var process = Start(tool, "clear service JustyBase account " + Quote(key), redirectInput: false);
            if (!process.WaitForExit(TimeoutMs))
            {
                Kill(process);
                return false;
            }

            // Missing entry is not an error: goal state (absent) already holds.
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            _logger.TrackError(ex, isCrash: false);
            return false;
        }
    }

    private string? ToolPath => IsAvailable ? ProbeTool() : null;

    private static string? ProbeTool()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        foreach (string candidate in ProbePaths)
        {
            try
            {
                if (!candidate.Contains('/'))
                {
                    string? onPath = FindOnPath(candidate);
                    if (onPath is not null)
                    {
                        return onPath;
                    }

                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort probe; try the next candidate.
            }
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Malformed PATH entry; try the next one.
            }
        }

        return null;
    }

    private static Process Start(string tool, string arguments, bool redirectInput)
    {
        var startInfo = new ProcessStartInfo(tool, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        Process? process = Process.Start(startInfo);
        return process ?? throw new InvalidOperationException("secret-tool failed to start.");
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException)
        {
            // Best-effort; the timeout already failed the operation.
        }
    }

    private static bool TryNormalize(string? connectionName, out string key)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            key = string.Empty;
            return false;
        }

        key = connectionName.Trim().ToUpperInvariant();
        return true;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// Static guard: fail the PR when new sync-over-async / UI-blocking patterns appear
/// in JustyBase / JustyBase.PluginBase (allowlisted exceptions only).
/// </summary>
public sealed class ConcurrencyGuardTests
{
    private static readonly Regex GetAwaiterGetResult = new(
        @"\.GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex TaskWaitCall = new(
        @"(?<![A-Za-z0-9_])\.Wait\s*\(",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedGetResultFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Intentionally empty after P0 marshal refactor — add only with justification.
    };

    private static readonly HashSet<string> AllowedWaitFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "IProgramErrorHandlingService.cs",
        "SqlConnectionManager.cs",
        // CacheAllObjects still uses Task.WaitAny for connection close timeout (bounded).
        "DatabaseCacheManager.cs",
    };

    [Fact]
    public void JustyBase_And_PluginBase_HaveNoForbiddenSyncOverAsync()
    {
        var root = FindSourceRoot();
        Assert.True(Directory.Exists(root), $"Source root not found: {root}");

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                // Match project folders under source/, not any path segment named JustyBase
                // (repo root /JustyBase/ would otherwise scan tests, Common, etc.).
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                return rel.StartsWith("JustyBase/", StringComparison.OrdinalIgnoreCase)
                       || rel.StartsWith("JustyBase.PluginBase/", StringComparison.OrdinalIgnoreCase);
            })
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(files);

        var violations = new List<string>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (GetAwaiterGetResult.IsMatch(line) && !AllowedGetResultFiles.Contains(name))
                {
                    violations.Add($"{Rel(root, file)}:{i + 1}: GetAwaiter().GetResult()");
                }

                if (TaskWaitCall.IsMatch(line)
                    && !line.Contains("WaitAsync", StringComparison.Ordinal)
                    && !line.Contains("ManualResetEvent", StringComparison.Ordinal)
                    && !line.Contains("WaitAny", StringComparison.Ordinal)
                    && !line.Contains("WaitAll", StringComparison.Ordinal)
                    && !line.Contains("Wait(TimeSpan", StringComparison.Ordinal)
                    && !AllowedWaitFiles.Contains(name))
                {
                    if (line.Contains(".Wait(", StringComparison.Ordinal))
                    {
                        violations.Add($"{Rel(root, file)}:{i + 1}: .Wait(");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Forbidden sync-over-async patterns found (use async marshal / await instead):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Take(40)));
    }

    private static string Rel(string root, string file)
        => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "source");
            if (Directory.Exists(Path.Combine(candidate, "JustyBase"))
                && Directory.Exists(Path.Combine(candidate, "JustyBase.PluginBase")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        // Fallback: relative to test project output (bin/Debug/netXX)
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}

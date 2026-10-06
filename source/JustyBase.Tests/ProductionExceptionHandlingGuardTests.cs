using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// Static guard that keeps broad exception handling from spreading in production code
/// (<c>JustyBase/</c> and <c>JustyBase.PluginBase/</c>).
///
/// Two rules:
/// <list type="bullet">
/// <item>Empty catch blocks are forbidden — handle or log the exception, or document the
/// intent with a comment so the intentionally-ignored path is explicit.</item>
/// <item>The number of broad catches (a bare <c>catch</c> or <c>catch (Exception ...)</c>
/// without a <c>when (...)</c> filter) may not exceed <see cref="BroadCatchBaseline"/>.
/// This is a scoreboard: lowering the number is always welcome, raising it requires an
/// explicit, reviewed edit to the baseline.</item>
/// </list>
/// </summary>
public sealed class ProductionExceptionHandlingGuardTests
{
    /// <summary>
    /// Snapshot of broad catches in production code at the time this guard was introduced.
    /// Prefer catching specific exception types or adding a <c>when (...)</c> filter.
    /// </summary>
    private const int BroadCatchBaseline = 161;

    // catch { ... }  OR  catch (Exception [ex]) { ... } with no `when` filter.
    private static readonly Regex BroadCatch = new(
        @"\bcatch\s*(?:\(\s*(?:System\.)?Exception(?:\s+[A-Za-z_]\w*)?\s*\)\s*\{|\{)",
        RegexOptions.Compiled);

    private static readonly Regex EmptyCatch = new(
        @"\bcatch\s*(?:\([^)]*\))?\s*\{\s*\}",
        RegexOptions.Compiled);

    [Fact]
    public void ProductionCode_HasNoEmptyCatchBlocks()
    {
        var violations = new List<string>();

        foreach (var file in ScanProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in EmptyCatch.Matches(text))
            {
                violations.Add($"{Rel(file)}:{LineOf(text, match.Index)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Empty catch blocks are not allowed in production code. Handle or log the exception, "
            + "or document the intent with a comment:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProductionCode_BroadCatchCountDoesNotIncrease()
    {
        var perFile = new List<(string File, int Count)>();
        var total = 0;

        foreach (var file in ScanProductionFiles())
        {
            var text = File.ReadAllText(file);
            var count = BroadCatch.Matches(text).Count;
            if (count > 0)
            {
                perFile.Add((Rel(file), count));
                total += count;
            }
        }

        Assert.True(
            total <= BroadCatchBaseline,
            $"Broad catch count {total} exceeds baseline {BroadCatchBaseline}. "
            + "Catch specific exception types, or add a `when (...)` filter. Current locations:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                perFile.OrderByDescending(x => x.Count).Select(x => $"  {x.Count,3}  {x.File}")));
    }

    private static IEnumerable<string> ScanProductionFiles()
    {
        var root = FindSourceRoot();
        var projects = new[] { "JustyBase", "JustyBase.PluginBase" };

        return projects
            .Select(project => Path.Combine(root, project))
            .Where(Directory.Exists)
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            .Where(IsProductionFile);
    }

    private static bool IsProductionFile(string path)
    {
        var skip = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                           $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                           $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}",
                           $"{Path.DirectorySeparatorChar}.codex-aot-out{Path.DirectorySeparatorChar}" };
        return !skip.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string Rel(string file)
    {
        var root = FindSourceRoot();
        return Path.GetRelativePath(root, file).Replace('\\', '/');
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

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

        // Fallback: relative to test project output (bin/Debug/netXX) -> repo/source.
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}

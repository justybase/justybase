namespace JustyBase.Tests;

/// <summary>
/// F0: Shared helper for architecture guard tests (source root + production file scan).
/// </summary>
internal static class TestSourceRoot
{
    public static string Find()
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

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    public static string Rel(string root, string file)
        => Path.GetRelativePath(root, file).Replace('\\', '/');

    public static IEnumerable<string> EnumerateProductionFiles(string root, string[] projectDirs)
    {
        foreach (var project in projectDirs)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}

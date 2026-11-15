using System.IO;
using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// Documentation invariants that already drifted once. The README is the first
/// thing a contributor reads, so its pinned dependency versions must match the
/// project files.
/// </summary>
public sealed class DocumentationConsistencyTests
{
    [Fact]
    public void Readme_ProDataGridVersion_MatchesProjectReference()
    {
        var sourceRoot = TestSourceRoot.Find();
        var repoRoot = Directory.GetParent(sourceRoot)?.FullName;
        Assert.NotNull(repoRoot);

        var projectFile = Path.Combine(sourceRoot, "JustyBase", "JustyBase.csproj");
        var readmeFile = Path.Combine(repoRoot!, "README.md");
        Assert.True(File.Exists(projectFile), $"Missing {projectFile}");
        Assert.True(File.Exists(readmeFile), $"Missing {readmeFile}");

        var projectText = File.ReadAllText(projectFile);
        var match = Regex.Match(
            projectText,
            "<PackageReference Include=\"ProDataGrid\" Version=\"(?<version>[^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "ProDataGrid PackageReference not found.");

        var version = match.Groups["version"].Value;
        var readme = File.ReadAllText(readmeFile);

        Assert.Contains($"ProDataGrid {version}", readme, StringComparison.Ordinal);
        Assert.Contains($"NuGet {version}", readme, StringComparison.Ordinal);
    }
}

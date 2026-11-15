using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// AOT guard: result-grid grouping must not go through the reflection-based
/// <c>DataGridPathGroupDescription</c> / <c>DataGridSortDescription.FromPath</c> pair.
/// Both are <c>[RequiresUnreferencedCode]</c>, and in the trimmed Windows build the
/// <c>Fields[index]</c> lookup silently yields null, which collapses every row into one
/// group labelled <c>&lt;NULL&gt;</c>.
/// </summary>
public sealed class ResultGridGroupingAotGuardTests
{
    private static readonly Regex ReflectionGrouping = new(
        @"DataGridPathGroupDescription|DataGridSortDescription\.FromPath",
        RegexOptions.Compiled);

    private static readonly string[] GroupingFiles =
    [
        "JustyBase/ViewModels/Tools/SqlResultsViewModel.GridInteraction.cs",
    ];

    [Fact]
    public void ResultsGridGrouping_DoesNotUseReflectionPathDescriptions()
    {
        var root = TestSourceRoot.Find();
        var offenders = new List<string>();

        foreach (var relativeFile in GroupingFiles)
        {
            var path = Path.Combine(root, relativeFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{relativeFile} missing");

            var text = File.ReadAllText(path);
            if (ReflectionGrouping.IsMatch(text))
            {
                offenders.Add(relativeFile);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Result-grid grouping must use ResultGridGroupingFactory (AOT-safe): "
            + string.Join(", ", offenders));
    }
}

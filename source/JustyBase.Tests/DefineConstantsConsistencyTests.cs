using System.Xml.Linq;

namespace JustyBase.Tests;

public sealed class DefineConstantsConsistencyTests
{
    [Fact]
    public void JustyBaseProject_ShouldKeepDefineConstantsAndConditionalReferencesInSync()
    {
        var projectPath = FindJustyBaseProjectPath();
        var document = XDocument.Load(projectPath);

        var defineConstants = document
            .Descendants("DefineConstants")
            .Select(node => node.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        var declaredConstants = defineConstants
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MYSQL"] = @"..\Plugins\MySqlPlugin\MySqlPlugin.csproj",
            ["POSTGRES"] = @"..\Plugins\PostgresPlugin\PostgresPlugin.csproj",
                ["ORACLE"] = @"..\Plugins\OraclePlugin\OraclePlugin.csproj",
            ["DB2"] = @"..\Plugins\DB2Plugin\DB2Plugin.csproj"
        };

        var conditionalReferences = document
            .Descendants("ItemGroup")
            .Where(group => group.Attribute("Condition") is not null)
            .SelectMany(group => group.Elements("ProjectReference").Select(projectReference => new
            {
                Condition = group.Attribute("Condition")!.Value,
                Include = projectReference.Attribute("Include")?.Value ?? string.Empty
            }))
            .ToList();

        foreach (var (constant, expectedProjectReference) in expectedMappings)
        {
            Assert.Contains(
                conditionalReferences,
                item => item.Condition.Contains($"Contains('{constant}')", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizePath(item.Include), NormalizePath(expectedProjectReference), StringComparison.OrdinalIgnoreCase));
        }

        foreach (var declaredConstant in declaredConstants.Where(expectedMappings.ContainsKey))
        {
            Assert.Contains(
                conditionalReferences,
                item => item.Condition.Contains($"Contains('{declaredConstant}')", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string FindJustyBaseProjectPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "source", "JustyBase", "JustyBase.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate source\\JustyBase\\JustyBase.csproj from test runtime directory.");
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\').Trim();
    }
}

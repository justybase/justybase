using System.Xml.Linq;

namespace JustyBase.Tests;

public sealed class DefineConstantsConsistencyTests
{
    [Fact]
    public void JustyBaseProject_ShouldKeepDriverPropertiesAndReferencesInSync()
    {
        var projectPath = FindJustyBaseProjectPath();
        var document = XDocument.Load(projectPath);

        var expectedMappings = new Dictionary<string, (string Symbol, string Project, string DefaultValue)>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnableMySqlPlugin"] = ("MYSQL", @"..\Plugins\MySqlPlugin\MySqlPlugin.csproj", "false"),
            ["EnablePostgresPlugin"] = ("POSTGRES", @"..\Plugins\PostgresPlugin\PostgresPlugin.csproj", "true"),
            ["EnableOraclePlugin"] = ("ORACLE", @"..\Plugins\OraclePlugin\OraclePlugin.csproj", "false"),
            ["EnableDb2Plugin"] = ("DB2", @"..\Plugins\DB2Plugin\DB2Plugin.csproj", "true"),
            ["EnableDuckDbPlugin"] = ("DUCKDB", @"..\Plugins\DuckDBPlugin\DuckDBPlugin.csproj", "false")
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

        foreach (var (property, (symbol, expectedProjectReference, defaultValue)) in expectedMappings)
        {
            var propertyNode = document
                .Descendants("PropertyGroup")
                .Elements(property)
                .Single();
            Assert.Equal(defaultValue, propertyNode.Value);

            Assert.Contains(
                conditionalReferences,
                item => item.Condition.Contains($"'$(Enable{property[6..]})' == 'true'", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(NormalizePath(item.Include), NormalizePath(expectedProjectReference), StringComparison.OrdinalIgnoreCase));

            var constantNode = document
                .Descendants("PropertyGroup")
                .Elements("DefineConstants")
                .SingleOrDefault(node => node.Attribute("Condition")?.Value.Contains(
                    $"'$(Enable{property[6..]})' == 'true'",
                    StringComparison.OrdinalIgnoreCase) == true);
            Assert.NotNull(constantNode);
            Assert.Contains(symbol, constantNode!.Value, StringComparison.Ordinal);

            Assert.Contains(
                conditionalReferences,
                item => item.Condition.Contains($"'$(Enable{property[6..]})' == 'true'", StringComparison.OrdinalIgnoreCase));
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

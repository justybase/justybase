using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// F0 guard: ServiceLocator usage must shrink, not grow.
/// <c>Program.ServiceProvider</c> / <c>App.GetRequiredService</c> are allowed only in
/// composition-root / view-creation files (allowlist). Everywhere else VMs and services
/// must use constructor injection (see F1: IAiChatNavigator, IDatabaseServiceResolver,
/// IQuickOpenDialogService, IUiDispatcher).
/// <c>DatabaseServiceHelpers.GetDatabaseService</c> is allowed only as a fallback behind
/// an injected <c>IDatabaseServiceResolver</c> (DbSchemaModel static provider + App wiring).
/// </summary>
public sealed class ServiceLocatorGuardTests
{
    private static readonly string[] ServiceLocatorAllowlist =
    [
        "Program.cs",
        "App.axaml.cs",
        "ViewLocator.cs",
        "DockViewModelFactory.cs",
        "SqlDocumentDataTemplate.cs",
        // XAML-instantiated views without DI ctor (documented, ViewLocator path uses DI):
        "MainView.axaml.cs",
        "SnippetControl.axaml.cs",
        // F1 transitional fallbacks (injected first, ServiceLocator only when manually constructed):
        "SqlDocumentViewModel.cs",
        "SqlDiagnosticsViewModel.cs",
        "DbSchemaViewModel.cs",
        "SettingsViewModel.cs",
        "NzLinterService.cs",
        "IProgramErrorHandlingService.cs",
        "DbSchemaModel.cs",
        "IDatabaseSchemaItem.cs",
    ];

    private static readonly string[] DatabaseHelpersAllowlist =
    [
        "ServiceCollectionExtensions.cs",
        "DatabaseServiceHelpers.cs",
        "DatabaseServiceRegistry.cs",
        "DbSchemaModel.cs",
        "IDatabaseSchemaItem.cs",
        "SqlCodeFormatterService.cs",
        "SchemaSearchViewModel.cs",
        "DbSchemaViewModel.cs",
        "AddNewConnectionViewModel.Connection.cs",
        "GeneralApplicationData.cs",
    ];

    [Fact]
    public void ServiceLocator_OnlyInAllowlistedFiles()
    {
        var root = TestSourceRoot.Find();
        var violations = new List<string>();
        var pattern = new Regex(@"Program\.ServiceProvider|App\.GetRequiredService", RegexOptions.Compiled);

        foreach (var file in TestSourceRoot.EnumerateProductionFiles(root, ["JustyBase", "JustyBase.PluginBase"]))
        {
            var name = Path.GetFileName(file);
            if (ServiceLocatorAllowlist.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (pattern.IsMatch(lines[i]))
                {
                    violations.Add($"{TestSourceRoot.Rel(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ServiceLocator used outside allowlist (use ctor injection instead):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Take(40)));
    }

    [Fact]
    public void DatabaseServiceHelpers_OnlyInAllowlistedFiles()
    {
        var root = TestSourceRoot.Find();
        var violations = new List<string>();
        var pattern = new Regex(@"DatabaseServiceHelpers\.GetDatabaseService", RegexOptions.Compiled);

        foreach (var file in TestSourceRoot.EnumerateProductionFiles(root, ["JustyBase", "JustyBase.PluginBase"]))
        {
            var name = Path.GetFileName(file);
            if (DatabaseHelpersAllowlist.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (pattern.IsMatch(lines[i]))
                {
                    violations.Add($"{TestSourceRoot.Rel(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "DatabaseServiceHelpers.GetDatabaseService used outside allowlist (use IDatabaseServiceResolver instead):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Take(40)));
    }
}

using Avalonia.Threading;
using JustyBase.Common.Contracts;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using Moq;
using Xunit.Abstractions;

namespace JustyBase.HeadlessTests;

public sealed class FileTreeResponsivenessTests : HeadlessSessionTestBase
{
    private readonly ITestOutputHelper _output;

    public FileTreeResponsivenessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task FileTree_Expand_WithSimulatedSlowEnumerate_StaysWithinStallBudget() => RunOnUi(() =>
    {
        const int injectedDelayMs = DelayedDatabaseServiceMock.DefaultInjectedDelayMs;
        var rootDir = Path.Combine(Path.GetTempPath(), "justybase-filetree-resp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDir);

        try
        {
            for (var i = 0; i < 220; i++)
            {
                File.WriteAllText(Path.Combine(rootDir, $"f{i:D3}.txt"), "x");
            }

            FileTreeNodeModel.SimulateEnumerateDelayMs = injectedDelayMs;

            var node = new FileTreeNodeModel(
                rootDir,
                isDirectory: true,
                isRoot: true,
                Mock.Of<IMessageForUserTools>(),
                Mock.Of<ISimpleLogger>());

            var probe = new UiResponsivenessProbe(_output);
            var snapshot = probe.RunDuring(
                "FileTree.LoadChildrenAsync",
                () => node.LoadChildrenAsync(),
                TimeSpan.FromSeconds(15),
                injectedDelayMs: injectedDelayMs);

            ResponsivenessMetricsWriter.Append(nameof(FileTree_Expand_WithSimulatedSlowEnumerate_StaysWithinStallBudget), snapshot);
            UiResponsivenessProbe.AssertWithinBudget(snapshot);

            Dispatcher.UIThread.RunJobs();
            Assert.True(node.Children.Count >= 220);
        }
        finally
        {
            FileTreeNodeModel.SimulateEnumerateDelayMs = 0;
            try
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp tree.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of temp tree.
            }
        }
    });
}

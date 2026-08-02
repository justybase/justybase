using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class DockDocumentActivationServiceTests
{
    [Fact]
    public void GetDocumentOfType_WhenMultipleDocumentsExist_ReturnsLastMatchingDocument()
    {
        var first = new FakeSettingsDocument { Id = "first" };
        var second = new FakeSettingsDocument { Id = "second" };
        var service = new DockDocumentActivationService();

        FakeSettingsDocument? result = service.GetDocumentOfType<FakeSettingsDocument>([first, new FakeImportDocument(), second]);

        Assert.Same(second, result);
    }

    [Fact]
    public void EnsureDocument_WhenDocumentDoesNotExist_CreatesAndAddsIt()
    {
        List<IDockable> visibleDockables = [];
        var service = new DockDocumentActivationService();

        FakeSettingsDocument result = service.EnsureDocument(
            visibleDockables,
            () => new FakeSettingsDocument { Id = "settings" });

        Assert.Single(visibleDockables);
        Assert.Same(result, visibleDockables[0]);
    }

    [Fact]
    public void EnsureDocument_WhenDocumentExistsAndRecreateExistingIsFalse_ReusesExistingInstance()
    {
        FakeImportDocument existing = new() { Id = "import" };
        List<IDockable> visibleDockables = [existing];
        var service = new DockDocumentActivationService();

        FakeImportDocument result = service.EnsureDocument(
            visibleDockables,
            () => new FakeImportDocument { Id = "new-import" });

        Assert.Single(visibleDockables);
        Assert.Same(existing, result);
    }

    [Fact]
    public void EnsureDocument_WhenDocumentExistsAndRecreateExistingIsTrue_ReplacesExistingInstance()
    {
        FakeHistoryDocument existing = new() { Id = "history-old" };
        List<IDockable> visibleDockables = [existing];
        var service = new DockDocumentActivationService();

        FakeHistoryDocument result = service.EnsureDocument(
            visibleDockables,
            () => new FakeHistoryDocument { Id = "history-new" },
            recreateExisting: true);

        Assert.Single(visibleDockables);
        Assert.NotSame(existing, result);
        Assert.Same(result, visibleDockables[0]);
        Assert.Equal("history-new", result.Id);
    }

    private sealed class FakeSettingsDocument : Document;
    private sealed class FakeImportDocument : Document;
    private sealed class FakeHistoryDocument : Document;
}

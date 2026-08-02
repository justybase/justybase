using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Models;
using JustyBase.Services;
using System.Text.Json;

namespace JustyBase.Tests;

public sealed class DockSessionPersistenceServiceTests
{
    [Fact]
    public void SaveSession_SortsDocumentsByVisibleDockOrderAndPersistsSelectedTab()
    {
        OfflineDocumentContainer container = new()
        {
            SqlOfflineDocumentDictionary = new Dictionary<string, OfflineTabData>
            {
                ["doc-b"] = CreateOfflineTabData("doc-b"),
                ["doc-a"] = CreateOfflineTabData("doc-a"),
                ["doc-c"] = CreateOfflineTabData("doc-c")
            }
        };

        List<IDockable> visibleDockables =
        [
            new Document { Id = "doc-a" },
            new Document { Id = "doc-c" }
        ];

        string? savedContent = null;
        var service = new DockSessionPersistenceService();

        service.SaveSession(
            new DockSessionSaveRequest(
                SelectedTabId: "doc-c",
                OfflineDocumentContainer: container,
                VisibleDockables: visibleDockables,
                SaveEncodedText: content => savedContent = content));

        Assert.NotNull(savedContent);
        OfflineDocumentContainer? persisted = JsonSerializer.Deserialize(
            savedContent,
            MyJsonContextOfflineDocumentContainer.Default.OfflineDocumentContainer);

        Assert.NotNull(persisted);
        Assert.Equal("doc-c", persisted.SelectedTabId);
        Assert.Equal(["doc-a", "doc-c", "doc-b"], persisted.SqlOfflineDocumentDictionary.Keys);
    }

    [Fact]
    public void SaveSession_WhenVisibleDockListMissesDocument_KeepsItAtTheEnd()
    {
        OfflineDocumentContainer container = new()
        {
            SqlOfflineDocumentDictionary = new Dictionary<string, OfflineTabData>
            {
                ["doc-x"] = CreateOfflineTabData("doc-x"),
                ["doc-y"] = CreateOfflineTabData("doc-y")
            }
        };

        List<IDockable> visibleDockables =
        [
            new Document { Id = "doc-y" }
        ];

        string? savedContent = null;
        var service = new DockSessionPersistenceService();

        service.SaveSession(
            new DockSessionSaveRequest(
                SelectedTabId: "doc-y",
                OfflineDocumentContainer: container,
                VisibleDockables: visibleDockables,
                SaveEncodedText: content => savedContent = content));

        OfflineDocumentContainer? persisted = JsonSerializer.Deserialize(
            savedContent,
            MyJsonContextOfflineDocumentContainer.Default.OfflineDocumentContainer);

        Assert.NotNull(persisted);
        Assert.Equal(["doc-y", "doc-x"], persisted.SqlOfflineDocumentDictionary.Keys);
    }

    [Fact]
    public void SaveSession_WhenSelectedTabIdIsMissing_PersistsWithoutThrowing()
    {
        OfflineDocumentContainer container = new()
        {
            SqlOfflineDocumentDictionary = new Dictionary<string, OfflineTabData>
            {
                ["doc-a"] = CreateOfflineTabData("doc-a")
            }
        };

        string? savedContent = null;
        var service = new DockSessionPersistenceService();

        service.SaveSession(
            new DockSessionSaveRequest(
                SelectedTabId: "   ",
                OfflineDocumentContainer: container,
                VisibleDockables: [new Document { Id = "doc-a" }],
                SaveEncodedText: content => savedContent = content));

        Assert.NotNull(savedContent);
        OfflineDocumentContainer? persisted = JsonSerializer.Deserialize(
            savedContent,
            MyJsonContextOfflineDocumentContainer.Default.OfflineDocumentContainer);

        Assert.NotNull(persisted);
        Assert.True(string.IsNullOrEmpty(persisted.SelectedTabId));
        Assert.Equal(["doc-a"], persisted.SqlOfflineDocumentDictionary.Keys);
    }

    [Fact]
    public void SaveSession_AfterReorder_PersistsNewVisibleOrderAndActiveTab()
    {
        OfflineDocumentContainer container = new()
        {
            SqlOfflineDocumentDictionary = new Dictionary<string, OfflineTabData>
            {
                ["doc-a"] = CreateOfflineTabData("doc-a"),
                ["doc-b"] = CreateOfflineTabData("doc-b"),
                ["doc-c"] = CreateOfflineTabData("doc-c")
            }
        };

        // Reordered tab strip: B, A, C with B active.
        List<IDockable> visibleDockables =
        [
            new Document { Id = "doc-b" },
            new Document { Id = "doc-a" },
            new Document { Id = "doc-c" }
        ];

        string? savedContent = null;
        var service = new DockSessionPersistenceService();

        service.SaveSession(
            new DockSessionSaveRequest(
                SelectedTabId: "doc-b",
                OfflineDocumentContainer: container,
                VisibleDockables: visibleDockables,
                SaveEncodedText: content => savedContent = content));

        Assert.NotNull(savedContent);
        OfflineDocumentContainer? persisted = JsonSerializer.Deserialize(
            savedContent,
            MyJsonContextOfflineDocumentContainer.Default.OfflineDocumentContainer);

        Assert.NotNull(persisted);
        Assert.Equal("doc-b", persisted.SelectedTabId);
        Assert.Equal(["doc-b", "doc-a", "doc-c"], persisted.SqlOfflineDocumentDictionary.Keys);
    }

    private static OfflineTabData CreateOfflineTabData(string id)
    {
        return new OfflineTabData
        {
            MyId = id,
            Title = id,
            SqlText = "SELECT 1;",
            SqlFilePath = null
        };
    }
}

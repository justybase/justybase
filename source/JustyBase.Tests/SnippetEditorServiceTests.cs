using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using System.Collections.Generic;
using System.Linq;

namespace JustyBase.Tests;

public sealed class SnippetEditorServiceTests
{
    [Fact]
    public void LoadSnippets_ReturnsAllSnippetsFromConfig()
    {
        var appData = new StubAppData();
        appData.Config.AllSnippets["greet"] = (SnippetModel.STANDARD_STRING, "Greeting", "Hello!", "greet");
        appData.Config.AllSnippets["bye"] = (SnippetModel.FAST_STRING, "Farewell", "Goodbye!", "bye");
        var service = new SnippetEditorService(appData);

        var snippets = service.LoadSnippets().ToList();

        Assert.Equal(2, snippets.Count);
        Assert.Contains(snippets, s => s.SnippetName == "greet" && s.SnippetText == "Hello!");
        Assert.Contains(snippets, s => s.SnippetName == "bye" && s.SnippetType == SnippetModel.FAST_STRING);
    }

    [Fact]
    public void SaveSnippets_UpdatesConfigAndCallsClearTemp()
    {
        var appData = new StubAppData();
        appData.Config.AllSnippets["old"] = (SnippetModel.STANDARD_STRING, null, "old text", "old");
        var service = new SnippetEditorService(appData);

        service.SaveSnippets([
            new SnippetModel { SnippetName = "new1", SnippetType = SnippetModel.STANDARD_STRING, SnippetText = "abc" },
            new SnippetModel { SnippetName = "new2", SnippetType = SnippetModel.FAST_STRING, SnippetText = "xyz" }
        ]);

        Assert.False(appData.Config.AllSnippets.ContainsKey("old"));
        Assert.True(appData.Config.AllSnippets.ContainsKey("new1"));
        Assert.True(appData.Config.AllSnippets.ContainsKey("new2"));
        Assert.True(appData.ClearTempCalled);
    }

    [Fact]
    public void CreateNewSnippet_ReturnsSnippetWithStandardTypeAndPlaceholderName()
    {
        var service = new SnippetEditorService(new StubAppData());

        var snippet = service.CreateNewSnippet();

        Assert.Equal(SnippetModel.STANDARD_STRING, snippet.SnippetType);
        Assert.Equal("<NAME>", snippet.SnippetName);
    }

    [Fact]
    public void LoadSnippets_WhenConfigEmpty_ReturnsEmpty()
    {
        var service = new SnippetEditorService(new StubAppData());

        var snippets = service.LoadSnippets().ToList();

        Assert.Empty(snippets);
    }

    private sealed class StubAppData : IGeneralApplicationData
    {
        public AppOptions Config { get; set; } = new AppOptions();
        public bool ClearTempCalled { get; private set; }
        public void ClearTempSippetsObjects() => ClearTempCalled = true;

        // IGeneralApplicationData
        public string SelectedTabIdFromStart { get; set; } = string.Empty;
        public string DownloadPluginsBasePath => throw new NotImplementedException();
        public bool AddToOrEditLoginData(string name, string database, string driver, string password, string userName, string server, string? role = null, string? warehouse = null, string? schema = null) => throw new NotImplementedException();
        public bool DeleteFromLoginData(string name) => throw new NotImplementedException();
        public void SaveConfig() => throw new NotImplementedException();
        public void SaveCredentials() => throw new NotImplementedException();
        public string GetCurrentCopyVersion() => throw new NotImplementedException();

        // IDatabaseInfo
        public Task LoadPluginsIfNeeded(Action? uiAction) => throw new NotImplementedException();
        public ISimpleLogger GlobalLoggerObject => ISimpleLogger.EmptyLogger;
        public Dictionary<string, LoginDataModel> LoginDataDic => throw new NotImplementedException();
        public string GetDataDir() => throw new NotImplementedException();

        // ISomeEditorOptions
        public Dictionary<string, (string snippetType, string? Description, string? Text, string? Keyword)> GetAllSnippets => Config.AllSnippets;
        public Dictionary<string, string> FastReplaceDictionary => throw new NotImplementedException();
        public List<string> TypoPatternList => throw new NotImplementedException();
        public Dictionary<string, string> VariablesDictionary { get; set; } = [];
        public bool CollapseFoldingOnStartup => false;
        public string GetFormatterSql(string txt) => throw new NotImplementedException();

        // IRuntimeDocumentsContainer
        public string AddNewDocument(string title, string? initText = null) => throw new NotImplementedException();
        public bool TryGetOpenedDocumentVmByFilePath(string path, out IHotDocumentVm? openedVm) { openedVm = null; throw new NotImplementedException(); }
        public bool RemoveDocumentById(string id) => throw new NotImplementedException();
        public OfflineTabData GetDocumentVmById(string id) => throw new NotImplementedException();
        public IEnumerable<KeyValuePair<string, OfflineTabData>> GetDocumentsKeyValueCollection() => throw new NotImplementedException();
        public bool TryGetDocumentById(string id, out OfflineTabData savedTabData) { savedTabData = default!; throw new NotImplementedException(); }
        public int GetDocumentIndexById(string id) => throw new NotImplementedException();
        public void AddProblemDocument(string id, IHotDocumentVm documentViewModel) => throw new NotImplementedException();
        public OfflineDocumentContainer GetOfflineDocumentContainer(string selectedTabId) => throw new NotImplementedException();
    }
}

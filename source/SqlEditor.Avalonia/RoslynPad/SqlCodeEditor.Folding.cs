using JustyBase.Editor.Folding;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Editor;

public sealed partial class SqlCodeEditor
{
    public void ExpandFoldings()
    {
        if (_foldingManager is not null)
        {
            foreach (var fold in _foldingManager.AllFoldings)
            {
                fold.IsFolded = false;
            }
        }
    }

    public void CollapseFoldings()
    {
        if (_foldingManager is not null)
        {
            foreach (var fold in _foldingManager.AllFoldings)
            {
                fold.IsFolded = true;
            }
        }
    }

    public bool ForceUpdateFoldings()
    {
        if (_xmlFoldingStrategy is not null)
        {
            _xmlFoldingStrategy.UpdateFoldings(_foldingManager, Document);
            return true;
        }
        else if (_foldingStrategy is not null)
        {
            _foldingStrategy.UpdateFoldings(_foldingManager, Document);
            return true;
        }
        return false;
    }

    private FoldingManager? _foldingManager;
    private SqlFoldingStrategy? _foldingStrategy;
    private XmlFoldingStrategy? _xmlFoldingStrategy;
    private DispatcherTimer? _foldingTimer;

    public void FoldingSetup()
    {
        if (_foldingManager != null)
        {
            _foldingManager.Clear();
            FoldingManager.Uninstall(_foldingManager);
        }
        _foldingManager = FoldingManager.Install(TextArea);

        if (ISomeEditorOptions.REGISTERED_EXTENSIONS.TryGetValue(LanguageFileExtension, out var res) && res.isXml)
        {
            _xmlFoldingStrategy = new XmlFoldingStrategy();
            _xmlFoldingStrategy.UpdateFoldings(_foldingManager, Document);
            CollapseFoldings();

            _foldingTimer = new();
            _foldingTimer.Tick += new EventHandler((s, e) =>
            {
                _foldingTimer.Stop();
                _xmlFoldingStrategy?.UpdateFoldings(_foldingManager, Document);
            });
            _foldingTimer.Interval = TimeSpan.FromSeconds(0.5);
        }
        else if (this.SyntaxHighlighting?.Name == "GeneralSql")
        {
            _foldingStrategy = new SqlFoldingStrategy();
            _foldingStrategy.UpdateFoldings(_foldingManager, Document);
            CollapseFoldings();

            _foldingTimer = new();
            _foldingTimer.Tick += new EventHandler((s, e) =>
            {
                _foldingTimer.Stop();
                _foldingStrategy?.UpdateFoldings(_foldingManager, Document);
            });
            _foldingTimer.Interval = TimeSpan.FromSeconds(0.5);
        }
    }
}

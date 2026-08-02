using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using JustyBase.ViewModels.Documents;
using JustyBase.Services;
using JustyBase.Services.Docking;

namespace JustyBase.ViewModels.Docks;

public sealed class CustomDocumentDock : DocumentDock
{
    private readonly IDockSqlDocumentFactory _dockSqlDocumentFactory;

    public CustomDocumentDock(IDockSqlDocumentFactory dockSqlDocumentFactory)
    {
        _dockSqlDocumentFactory = dockSqlDocumentFactory;
        CreateDocument = new RelayCommand(CreateNewDocument);
    }

    private void CreateNewDocument()
    {
        if (!CanCreateDocument)
        {
            return;
        }

        int index = (VisibleDockables?.Count ?? 0) + 1;

        string title = $"Document{index}";

        while (VisibleDockables.Select(x => x.Title.Trim('*')).Contains(title))
        {
            index++;
            title = $"Document{index}";
        }


        SqlDocumentViewModel document = _dockSqlDocumentFactory.CreateDocument(title);

        if (this.ActiveDockable is SqlDocumentViewModel sqlDocumentViewModel)
        {
            document.SelectedConnectionIndex = sqlDocumentViewModel.SelectedConnectionIndex;
            document.SelectedDatabase = sqlDocumentViewModel.SelectedDatabase;
        }

        Factory?.AddDockable(this, document);
        Factory?.SetActiveDockable(document);
        Factory?.SetFocusedDockable(this, document);
    }
}


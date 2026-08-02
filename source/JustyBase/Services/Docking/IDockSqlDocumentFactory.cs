using JustyBase.Common.Contracts;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services.Docking;

public interface IDockSqlDocumentFactory
{
    SqlDocumentViewModel CreateDocument(
        string title,
        string? initText = null,
        bool txtPreview = false,
        string? filePath = null,
        int? fontSize = null);
}

public sealed class DockSqlDocumentFactory(IGeneralApplicationData generalApplicationData, IDockViewModelFactory viewModelFactory) : IDockSqlDocumentFactory
{
    private readonly IGeneralApplicationData _generalApplicationData = generalApplicationData;
    private readonly IDockViewModelFactory _viewModelFactory = viewModelFactory;

    public SqlDocumentViewModel CreateDocument(
        string title,
        string? initText = null,
        bool txtPreview = false,
        string? filePath = null,
        int? fontSize = null)
    {
        string docId = _generalApplicationData.AddNewDocument(title, initText);
        var offlineDocument = _generalApplicationData.GetDocumentVmById(docId);

        SqlDocumentViewModel document = _viewModelFactory.CreateSqlDocumentViewModel();
        document.TxtPreview = txtPreview;
        document.Id = docId;
        document.Title = title;
        document.FilePath = filePath;
        document.FontSize = fontSize ?? offlineDocument.FontSize;

        offlineDocument.HotDocumentViewModel = document;
        return document;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Models;
using JustyBase.Services;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels;

public sealed partial class SnippetControlViewModel : ObservableObject
{
    private readonly ISnippetEditorService _snippetEditorService;

    public ObservableCollection<SnippetModel> SnippetModels { get; set; } = [];

    [ObservableProperty]
    public partial SnippetModel SelectedSnippetModel { get; set; }

    public int SnippetSelectedIndex { get; set; }

    public SnippetControlViewModel(ISnippetEditorService snippetEditorService)
    {
        _snippetEditorService = snippetEditorService;
        foreach (var item in snippetEditorService.LoadSnippets())
        {
            SnippetModels.Add(item);
        }
    }

    [RelayCommand]
    private void AddNew()
    {
        var snp = _snippetEditorService.CreateNewSnippet();
        int insertIndex = SnippetSelectedIndex > 0 ? SnippetSelectedIndex : 0;
        SnippetModels.Insert(insertIndex, snp);
        SelectedSnippetModel = snp;
        SnippetSelectedIndex = insertIndex;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedSnippetModel is not null)
        {
            SnippetModels.Remove(SelectedSnippetModel);
        }
    }

    [RelayCommand]
    private void Save()
    {
        _snippetEditorService.SaveSnippets(SnippetModels);
    }
}


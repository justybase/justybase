using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JustyBase.ViewModels;

public partial class AskForConfirmViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "abc";

    [ObservableProperty]
    public partial string TextMessage { get; set; } = "xyz";

    public string ResultAsString { get; set; } = "Cancel";

    public Action? CloseAction { get; set; }
    public Action? AdditionalYesAction { get; set; }

    [RelayCommand]
    private void ProcessAnswerKeys(string answerName)
    {
        ResultAsString = answerName;
        if (ResultAsString == "Yes")
        {
            AdditionalYesAction?.Invoke();
        }
        CloseAction?.Invoke();
    }
}

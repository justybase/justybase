using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.Common.Contracts;

namespace JustyBase.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IGeneralApplicationData _generalApplicationData;

    public AboutViewModel(IGeneralApplicationData generalApplicationData)
    {
        _generalApplicationData = generalApplicationData;

        CurrentVersionText = _generalApplicationData.GetCurrentCopyVersion();
    }

    [ObservableProperty]
    public partial string CurrentVersionText { get; set; }

    [ObservableProperty]
    public partial string WaringText { get; set; }

}

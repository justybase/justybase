using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Common.Helpers;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IOtherHelpers _otherHelpers;
    private readonly IMessageForUserTools _messageForUserTools;

    public AboutViewModel(IGeneralApplicationData generalApplicationData, IOtherHelpers otherHelpers,
        ISimpleLogger simpleLogger, IMessageForUserTools messageForUserTools)
    {
        _generalApplicationData = generalApplicationData;
        _otherHelpers = otherHelpers;
        _simpleLogger = simpleLogger;
        _messageForUserTools = messageForUserTools;

        CurrentVersionText = _generalApplicationData.GetCurrentCopyVersion();
    }

    [ObservableProperty]
    public partial string CurrentVersionText { get; set; }

    [ObservableProperty]
    public partial string WaringText { get; set; }

    [RelayCommand]
    private void DownloadPlugins()
    {
        _generalApplicationData.Config.ResetPlugins = true;
        _messageForUserTools.ShowSimpleMessageBoxInstance("Please restart application");
    }
}

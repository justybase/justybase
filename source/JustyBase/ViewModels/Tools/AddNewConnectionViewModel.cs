using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;

namespace JustyBase.ViewModels.Tools;

public sealed partial class AddNewConnectionViewModel : Tool
{
    public AddNewConnectionViewModel(IFactory factory, IGeneralApplicationData generalApplicationData)
    {
        _generalApplicationData = generalApplicationData;
        _simpleLogger = JustyBase.PluginCommon.Contracts.ISimpleLogger.EmptyLogger;
        this.Factory = factory;
        InitializeCommandsAndSamples();
    }

    private void RefreshConnections()
    {
        OnPropertyChanged(nameof(ConnectionList));
        DbSchemaViewModel? dbChemaViewModel = Factory.Find(a => a is DbSchemaViewModel).FirstOrDefault() as DbSchemaViewModel;
        dbChemaViewModel?.ResedConnectionList();
    }

    public string Pass
    {
        get;
        set => SetProperty(ref field, value);
    }
}

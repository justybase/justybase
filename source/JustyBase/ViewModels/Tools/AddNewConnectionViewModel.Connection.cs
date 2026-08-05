using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Helpers.Shared;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;

public sealed partial class AddNewConnectionViewModel
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;

    public AddNewConnectionViewModel(IFactory factory, IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools, ISimpleLogger simpleLogger)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
        this.Factory = factory;

        // Initialize commands
        AddNewCommand = new RelayCommand(AddNew);
        DeleteCommand = new RelayCommand(Delete);
        CloneConnectionCommand = new RelayCommand(CloneConnection);
        RefreshConnectionsCommand = new RelayCommand(RefreshConnections);
    }

    public ICommand AddNewCommand { get; init; }
    public ICommand DeleteCommand { get; init; }
    public ICommand CloneConnectionCommand { get; init; }
    public ICommand RefreshConnectionsCommand { get; init; }

    [ObservableProperty]
    public partial bool ShowExistings { get; set; } = true;

    public Action? CloseWindowAction { get; set; }//close window

    private void AddNew()
    {
        if (string.IsNullOrEmpty(ConName) || DriverIndex == -1)
        {
            return;
        }

        string driverName = DriversList[DriverIndex];
        var res = _generalApplicationData.AddToOrEditLoginData(
            ConName,
            Database,
            driverName,
            Pass,
            UserName,
            Server);
        Refresh(res);
        CloseWindowAction?.Invoke();
    }
    private void Delete()
    {
        var res = _generalApplicationData.DeleteFromLoginData(ConName);
        int selIndex = Refresh(res);
        if (ConnectionList.Any())
        {
            selIndex--;
            if (selIndex >= 0 && selIndex < ConnectionList.Count)
            {
                SelectedConnection = ConnectionList[selIndex];
            }
            else
            {
                SelectedConnection = ConnectionList[0];
            }
        }
    }

    private int Refresh(bool res)
    {
        int selIndex = SelectedConnectionIndex;
        if (res)
        {
            SqlDocumentViewModelHelper.SetConnectionList(_generalApplicationData, _messageForUserTools, _simpleLogger, true);
        }

        RefreshConnections();
        return selIndex;
    }

    [ObservableProperty]
    public partial int SelectedConnectionIndex { get; set; }

    private void CloneConnection()
    {
        if (string.IsNullOrEmpty(ConName) || DriverIndex == -1)
        {
            return;
        }

        string driverName = DriversList[DriverIndex];
        var res = _generalApplicationData.AddToOrEditLoginData(
            ConName + "_Clone",
            Database,
            driverName,
            Pass,
            UserName,
            Server);
        if (res)
        {
            SqlDocumentViewModelHelper.SetConnectionList(_generalApplicationData, _messageForUserTools, _simpleLogger, false);
        }
        RefreshConnections();
        if (ConnectionList.Any())
        {
            SelectedConnection = ConnectionList[^1];
        }
    }

    [ObservableProperty]
    public partial string ConName { get; set; }

    [ObservableProperty]
    public partial int DriverIndex { get; set; }

    private readonly List<string> _driversList = DatabaseServiceHelpers.GetSupportedDriversNames();
    public List<string> DriversList => _driversList;

    public ObservableCollection<ConnectionItem> ConnectionList => SqlDocumentViewModelHelper.ConnectionsList;

    public ConnectionItem SelectedConnection
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (SelectedConnection is not null)
            {
                ConName = SelectedConnection.Name;
                if (_generalApplicationData.LoginDataDic.TryGetValue(ConName, out var tmp))
                {
                    Server = tmp.Server;
                    DriverIndex = DriversList.IndexOf(tmp.Driver);
                    Database = tmp.Database;
                    UserName = tmp.UserName;
                    Pass = tmp.Password;
                }
                else
                {
                    var tmp2 = _generalApplicationData.LoginDataDic.FirstOrDefault().Value;
                    if (tmp2 is not null)
                    {
                        Server = tmp2.Server;
                        DriverIndex = DriversList.IndexOf(tmp2.Driver);
                        Database = tmp2.Database;
                        UserName = tmp2.UserName;
                        Pass = tmp2.Password;
                    }
                }
            }
        }
    }

    [ObservableProperty]
    public partial string Server { get; set; }

    [ObservableProperty]
    public partial string Database { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; }
}

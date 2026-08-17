using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Helpers.Shared;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services;
using JustyBase.SqliteDriver.Samples;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using System.Data.Common;

namespace JustyBase.ViewModels.Tools;

public sealed partial class AddNewConnectionViewModel
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IMessageForUserTools? _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;
    private string _previousDriverDefaultPort = string.Empty;

    private static readonly IReadOnlyList<ConnectionDriverOption> _driversList =
    [
        new("Postgres", "PostgreSQL", "Open-source relational database", "5432", true, false),
        new("MySQL", "MySQL", "Popular relational database", "3306", true, false),
        new("Oracle", "Oracle", "Enterprise relational database", "1521", true, false),
        new("DB2", "IBM Db2", "IBM relational database", "50000", true, false),
        new("NetezzaSQL", "Netezza", "Analytics and data warehouse database", "5480", true, false),
        new("MsSqlTrusted", "SQL Server (Windows)", "SQL Server using Windows authentication", "1433", false, false),
        new("SQLite", "SQLite", "Local database file", string.Empty, false, true),
        new("DuckDB", "DuckDB", "Local analytical database file", string.Empty, false, true),
    ];

    public AddNewConnectionViewModel(IFactory factory, IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools, ISimpleLogger simpleLogger, IAvaloniaSpecificHelpers avaloniaSpecificHelpers)
    {
        _generalApplicationData = generalApplicationData;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
        this.Factory = factory;

        InitializeCommandsAndSamples();
    }

    private void InitializeCommandsAndSamples()
    {
        AddNewCommand = new AsyncRelayCommand(AddNewAsync);
        DeleteCommand = new RelayCommand(Delete);
        CloneConnectionCommand = new RelayCommand(CloneConnection);
        RefreshConnectionsCommand = new RelayCommand(RefreshConnections);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
        OpenDatabaseFileCommand = new AsyncRelayCommand(OpenDatabaseFileAsync);
        CreateDatabaseFileCommand = new AsyncRelayCommand(CreateDatabaseFileAsync);
        UseMemoryDatabaseCommand = new RelayCommand(UseMemoryDatabase);
        SelectedSqliteSamplePack = SqliteSampleCatalog.Packs[0];
        foreach (ConnectionDriverOption driver in _driversList)
        {
            VisibleDrivers.Add(driver);
        }
        SelectedDriver = _driversList[0];
        UpdateSqliteSampleObjects();
    }

    public ICommand AddNewCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand CloneConnectionCommand { get; private set; } = null!;
    public ICommand RefreshConnectionsCommand { get; private set; } = null!;
    public ICommand TestConnectionCommand { get; private set; } = null!;
    public ICommand OpenDatabaseFileCommand { get; private set; } = null!;
    public ICommand CreateDatabaseFileCommand { get; private set; } = null!;
    public ICommand UseMemoryDatabaseCommand { get; private set; } = null!;

    [ObservableProperty]
    public partial bool ShowExistings { get; set; } = true;

    [ObservableProperty]
    public partial string DriverSearchText { get; set; } = string.Empty;

    public ObservableCollection<ConnectionDriverOption> VisibleDrivers { get; } = [];

    [ObservableProperty]
    public partial ConnectionDriverOption? SelectedDriver { get; set; }

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial bool HasConnectionTestResult { get; set; }

    [ObservableProperty]
    public partial bool ConnectionTestIsSuccess { get; set; }

    [ObservableProperty]
    public partial string ConnectionTestStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectionTestDetails { get; set; } = string.Empty;

    public bool CanSave => !IsTestingConnection && IsFormComplete;
    public bool CanTest => !IsTestingConnection && IsFormComplete;
    public bool IsConnectionTestVisible => HasConnectionTestResult || IsTestingConnection;

    public string DriverError { get; private set; } = string.Empty;
    public string NameError { get; private set; } = string.Empty;
    public string ServerError { get; private set; } = string.Empty;
    public string PortError { get; private set; } = string.Empty;
    public string DatabaseError { get; private set; } = string.Empty;
    public string UserNameError { get; private set; } = string.Empty;
    public bool HasNameError => !string.IsNullOrEmpty(NameError);
    public bool HasServerError => !string.IsNullOrEmpty(ServerError);
    public bool HasPortError => !string.IsNullOrEmpty(PortError);
    public bool HasDatabaseError => !string.IsNullOrEmpty(DatabaseError);
    public bool HasUserNameError => !string.IsNullOrEmpty(UserNameError);

    public bool IsFormComplete
        => !string.IsNullOrWhiteSpace(ConName)
            && SelectedDriver is not null
            && (SelectedDriver.UsesFilePath || !string.IsNullOrWhiteSpace(Server))
            && (!IsPortVisible || int.TryParse(Port, out int port) && port is > 0 and <= 65535)
            && !string.IsNullOrWhiteSpace(Database)
            && (!IsAuthenticationVisible || !string.IsNullOrWhiteSpace(UserName));

    public Action? CloseWindowAction { get; set; }

    private async Task AddNewAsync()
    {
        ValidateForm(checkDuplicate: !ShowExistings);
        if (!CanSave)
        {
            return;
        }

        try
        {
            string connectionName = ConName.Trim();
            if (CreateSampleDatabase && IsSqlite)
            {
                if (SelectedSqliteSamplePack is null)
                {
                    throw new InvalidOperationException("Choose a SQLite sample database.");
                }

                DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
                await SqliteSampleDatabaseBuilder.CreateAsync(
                    Server ?? string.Empty,
                    Database ?? string.Empty,
                    SelectedSqliteSamplePack,
                    SqliteSampleObjects.Where(item => item.IsSelected).Select(item => item.Definition.Id));
            }

            string driverName = SelectedDriver!.Id;
            bool saved = _generalApplicationData.AddToOrEditLoginData(
                connectionName,
                Database,
                driverName,
                Pass,
                UserName,
                Server,
                Port);
            if (!saved)
            {
                throw new InvalidOperationException("The connection could not be saved.");
            }

            _generalApplicationData.SaveConfig();
            Refresh(saved);
            CloseWindowAction?.Invoke();
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
            _messageForUserTools?.ShowSimpleMessageBoxInstance(ex);
        }
    }

    private async Task TestConnectionAsync()
    {
        ValidateForm();
        if (!CanTest)
        {
            return;
        }

        IsTestingConnection = true;
        HasConnectionTestResult = false;
        ConnectionTestStatus = "Testing connection...";
        ConnectionTestDetails = string.Empty;
        NotifyCommandState();

        string connectionName = ConName.Trim().ToUpperInvariant();
        bool hadOriginal = _generalApplicationData.LoginDataDic.TryGetValue(connectionName, out LoginDataModel? original);
        LoginDataModel? snapshot = hadOriginal && original is not null
            ? new LoginDataModel
            {
                ConnectionName = original.ConnectionName,
                Driver = original.Driver,
                Server = original.Server,
                Port = original.Port,
                UserName = original.UserName,
                Password = original.Password,
                Database = original.Database,
                Schema = original.Schema,
                Warehouse = original.Warehouse,
                Role = original.Role,
                DefaultIndex = original.DefaultIndex,
                SqliteOptions = original.SqliteOptions
            }
            : null;

        try
        {
            _generalApplicationData.AddToOrEditLoginData(connectionName, Database, SelectedDriver!.Id, Pass, UserName, Server, Port);
            DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
            await Task.Run(async () =>
            {
                IDatabaseService? service = DatabaseServiceHelpers.GetDatabaseService(
                    _generalApplicationData,
                    connectionName,
                    forceRefresh: true,
                    delayCache: true,
                    connectionTimeout: 15);
                if (service is null)
                {
                    throw new InvalidOperationException("The selected database driver is not available.");
                }

                await using DbConnection connection = service.GetConnection(null, pooling: false);
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
                await connection.OpenAsync(timeout.Token);
                if (service is IDatabaseConnectionConfigurator configurator)
                {
                    configurator.ConfigureOpenConnection(connection);
                }
            });

            ConnectionTestIsSuccess = true;
            ConnectionTestStatus = "Connection successful";
            ConnectionTestDetails = $"Connected to {SelectedDriver.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
            ConnectionTestStatus = "Connection timed out";
            ConnectionTestDetails = "The database did not respond within 15 seconds.";
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
            ConnectionTestStatus = "Connection failed";
            ConnectionTestDetails = ex.Message;
        }
        finally
        {
            DatabaseServiceHelpers.RemoveCachedConnection(connectionName);
            if (snapshot is not null)
            {
                _generalApplicationData.LoginDataDic[connectionName] = snapshot;
            }
            else
            {
                _generalApplicationData.LoginDataDic.Remove(connectionName);
            }

            HasConnectionTestResult = true;
            IsTestingConnection = false;
            NotifyCommandState();
        }
    }

    private bool CanTestConnection() => CanTest;

    private void Delete()
    {
        if (string.IsNullOrWhiteSpace(ConName))
        {
            return;
        }

        bool deleted = _generalApplicationData.DeleteFromLoginData(ConName);
        if (deleted)
        {
            _generalApplicationData.SaveConfig();
        }

        int selIndex = Refresh(deleted);
        if (ConnectionList.Any())
        {
            selIndex--;
            SelectedConnection = selIndex >= 0 && selIndex < ConnectionList.Count
                ? ConnectionList[selIndex]
                : ConnectionList[0];
        }
    }

    private int Refresh(bool changed)
    {
        int selIndex = SelectedConnectionIndex;
        if (changed && _messageForUserTools is not null)
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
        ValidateForm();
        if (!CanSave || string.IsNullOrWhiteSpace(ConName))
        {
            return;
        }

        string cloneName = ConName.Trim() + "_Clone";
        _generalApplicationData.AddToOrEditLoginData(cloneName, Database, SelectedDriver!.Id, Pass, UserName, Server, Port);
        _generalApplicationData.SaveConfig();
        Refresh(true);
        if (ConnectionList.Any())
        {
            SelectedConnection = ConnectionList[^1];
        }
    }

    [ObservableProperty]
    public partial string ConName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int DriverIndex { get; set; }

    [ObservableProperty]
    public partial bool CreateSampleDatabase { get; set; }

    [ObservableProperty]
    public partial SqliteSamplePack? SelectedSqliteSamplePack { get; set; }

    [ObservableProperty]
    public partial string Server { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Port { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Database { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UserName { get; set; } = string.Empty;

    public string Pass
    {
        get;
        set
        {
            SetProperty(ref field, value);
            ValidateForm();
        }
    }

    public IReadOnlyList<ConnectionDriverOption> DriversList => _driversList;
    public IReadOnlyList<SqliteSamplePack> SqliteSamplePacks => SqliteSampleCatalog.Packs;
    public ObservableCollection<SqliteSampleObjectOption> SqliteSampleObjects { get; } = [];

    public bool IsSqlite => SelectedDriver?.Id == "SQLite";
    public bool IsPortVisible => SelectedDriver is not null && !SelectedDriver.UsesFilePath;
    public bool IsAuthenticationVisible => SelectedDriver?.RequiresAuthentication == true;
    public bool IsDatabaseVisible => SelectedDriver is not null;
    public bool IsFileDatabase => SelectedDriver?.UsesFilePath == true;
    public bool IsSqliteSampleVisible => !ShowExistings && IsSqlite;
    public string ServerLabel => SelectedDriver?.UsesFilePath == true ? "Folder (optional)" : "Host";
    public string DatabaseLabel => SelectedDriver?.UsesFilePath == true ? "Database file" : "Database";
    public string ServerWatermark => SelectedDriver?.UsesFilePath == true ? "Optional folder or :memory:" : "db.example.com";
    public string DatabaseWatermark => SelectedDriver?.UsesFilePath == true ? "Path to .db file" : "Database name";
    public string DatabaseFileHint => Database.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
        ? "The database will live only for the duration of the session."
        : string.IsNullOrWhiteSpace(Database)
            ? "Choose an existing file, create a new one, or use an in-memory database."
            : Database;
    public string SelectedSqliteSampleDescription => SelectedSqliteSamplePack?.Description ?? string.Empty;
    public ObservableCollection<ConnectionItem> ConnectionList => SqlDocumentViewModelHelper.ConnectionsList;

    public ConnectionItem? SelectedConnection
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (value is null || !_generalApplicationData.LoginDataDic.TryGetValue(value.Name, out LoginDataModel? data))
            {
                return;
            }

            ConName = value.Name;
            SelectedDriver = _driversList.FirstOrDefault(driver => driver.Id == data.Driver);
            Server = data.Server ?? string.Empty;
            Port = data.Port ?? string.Empty;
            Database = data.Database ?? string.Empty;
            UserName = data.UserName ?? string.Empty;
            Pass = data.Password ?? string.Empty;
        }
    }

    partial void OnSelectedDriverChanged(ConnectionDriverOption? value)
    {
        DriverIndex = value is null ? -1 : FindDriverIndex(value);
        if (value is not null && (string.IsNullOrWhiteSpace(Port) || Port == _previousDriverDefaultPort))
        {
            Port = value.DefaultPort;
        }

        _previousDriverDefaultPort = value?.DefaultPort ?? string.Empty;
        CreateSampleDatabase = value?.Id != "SQLite" ? false : CreateSampleDatabase;
        OnPropertyChanged(nameof(IsSqlite));
        OnPropertyChanged(nameof(IsPortVisible));
        OnPropertyChanged(nameof(IsAuthenticationVisible));
        OnPropertyChanged(nameof(IsDatabaseVisible));
        OnPropertyChanged(nameof(IsFileDatabase));
        OnPropertyChanged(nameof(IsSqliteSampleVisible));
        OnPropertyChanged(nameof(ServerLabel));
        OnPropertyChanged(nameof(DatabaseLabel));
        OnPropertyChanged(nameof(ServerWatermark));
        OnPropertyChanged(nameof(DatabaseWatermark));
        OnPropertyChanged(nameof(DatabaseFileHint));
        ValidateForm();
        NotifyCommandState();
    }

    partial void OnDriverSearchTextChanged(string value)
    {
        string search = value.Trim();
        VisibleDrivers.Clear();
        foreach (ConnectionDriverOption driver in _driversList.Where(driver =>
                     search.Length == 0
                     || driver.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || driver.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleDrivers.Add(driver);
        }
    }

    partial void OnShowExistingsChanged(bool value)
    {
        if (value)
        {
            CreateSampleDatabase = false;
        }
        else
        {
            SelectedConnection = null;
            ConName = string.Empty;
            Server = string.Empty;
            Port = SelectedDriver?.DefaultPort ?? string.Empty;
            Database = string.Empty;
            UserName = string.Empty;
            Pass = string.Empty;
            HasConnectionTestResult = false;
            ConnectionTestStatus = string.Empty;
            ConnectionTestDetails = string.Empty;
        }

        OnPropertyChanged(nameof(IsSqliteSampleVisible));
        ValidateForm();
    }

    partial void OnSelectedSqliteSamplePackChanged(SqliteSamplePack? value)
    {
        UpdateSqliteSampleObjects();
        OnPropertyChanged(nameof(SelectedSqliteSampleDescription));
    }

    partial void OnIsTestingConnectionChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(IsConnectionTestVisible));
    }

    partial void OnHasConnectionTestResultChanged(bool value)
        => OnPropertyChanged(nameof(IsConnectionTestVisible));

    partial void OnConNameChanged(string value) => ValidateForm();
    partial void OnServerChanged(string value) => ValidateForm();
    partial void OnPortChanged(string value) => ValidateForm();
    partial void OnDatabaseChanged(string value)
    {
        OnPropertyChanged(nameof(DatabaseFileHint));
        ValidateForm();
    }
    partial void OnUserNameChanged(string value) => ValidateForm();

    private void ValidateForm(bool checkDuplicate = false)
    {
        NameError = string.IsNullOrWhiteSpace(ConName) ? "Enter a connection name." : string.Empty;
        if (checkDuplicate && !string.IsNullOrWhiteSpace(ConName)
            && _generalApplicationData.LoginDataDic.ContainsKey(ConName.Trim().ToUpperInvariant()))
        {
            NameError = "A connection with this name already exists.";
        }

        DriverError = SelectedDriver is null ? "Select a database type." : string.Empty;
        ServerError = SelectedDriver?.UsesFilePath == false && string.IsNullOrWhiteSpace(Server)
            ? "Enter a host name or IP address."
            : string.Empty;
        bool invalidPort = IsPortVisible
            && (!int.TryParse(Port, out int port) || port is < 1 or > 65535);
        PortError = invalidPort
            ? "Enter a port between 1 and 65535."
            : string.Empty;
        DatabaseError = string.IsNullOrWhiteSpace(Database) ? "Enter a database name or file path." : string.Empty;
        UserNameError = IsAuthenticationVisible && string.IsNullOrWhiteSpace(UserName)
            ? "Enter a username."
            : string.Empty;

        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(DriverError));
        OnPropertyChanged(nameof(ServerError));
        OnPropertyChanged(nameof(HasServerError));
        OnPropertyChanged(nameof(PortError));
        OnPropertyChanged(nameof(HasPortError));
        OnPropertyChanged(nameof(DatabaseError));
        OnPropertyChanged(nameof(HasDatabaseError));
        OnPropertyChanged(nameof(UserNameError));
        OnPropertyChanged(nameof(HasUserNameError));
        OnPropertyChanged(nameof(IsFormComplete));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanTest));
        NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        if (TestConnectionCommand is AsyncRelayCommand testCommand)
        {
            testCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task OpenDatabaseFileAsync()
    {
        IStorageProvider? storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        string[] patterns = IsSqlite
            ? ["*.db", "*.sqlite", "*.sqlite3"]
            : ["*.duckdb", "*.db"];
        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType($"{SelectedDriver?.DisplayName ?? "Database"} files") { Patterns = patterns },
                    FilePickerFileTypes.All
                ]
            });

        if (files.Count == 0)
        {
            return;
        }

        Server = string.Empty;
        Database = files[0].Path.LocalPath;
        HasConnectionTestResult = false;
        OnPropertyChanged(nameof(DatabaseFileHint));
    }

    private async Task CreateDatabaseFileAsync()
    {
        IStorageProvider? storageProvider = _avaloniaSpecificHelpers.GetStorageProvider();
        if (storageProvider is null)
        {
            return;
        }

        string extension = IsSqlite ? "db" : "duckdb";
        IStorageFile? file = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                FileTypeChoices = [new FilePickerFileType($"{SelectedDriver?.DisplayName ?? "Database"} files") { Patterns = [$"*.{extension}"] }],
                DefaultExtension = extension,
                SuggestedFileName = $"database.{extension}",
                ShowOverwritePrompt = true
            });
        if (file is null)
        {
            return;
        }

        Server = string.Empty;
        Database = file.Path.LocalPath;
        HasConnectionTestResult = false;
        OnPropertyChanged(nameof(DatabaseFileHint));
    }

    private void UseMemoryDatabase()
    {
        Server = string.Empty;
        Database = ":memory:";
        HasConnectionTestResult = false;
        OnPropertyChanged(nameof(DatabaseFileHint));
    }

    private static int FindDriverIndex(ConnectionDriverOption driver)
    {
        for (int index = 0; index < _driversList.Count; index++)
        {
            if (_driversList[index] == driver)
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateSqliteSampleObjects()
    {
        SqliteSampleObjects.Clear();
        if (SelectedSqliteSamplePack is null)
        {
            return;
        }

        foreach (SqliteSampleObjectDefinition definition in SelectedSqliteSamplePack.Objects)
        {
            SqliteSampleObjects.Add(new SqliteSampleObjectOption(definition) { IsSelected = true });
        }
    }
}

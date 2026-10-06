using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Common.Contracts;

public interface IGeneralApplicationData : IDatabaseInfo, ISomeEditorOptions, IRuntimeDocumentsContainer
{
    static readonly string ConfigDirectoryEvo = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\JustDataEvo";
    static readonly string ColorsPath = $"{ConfigDirectoryEvo}\\colors.json";
    static readonly string ConfigEvoFile = $"{ConfigDirectoryEvo}\\config.json.enc";
    static readonly string DataDirectory = $"{ConfigDirectoryEvo}\\data";
    static readonly string BackupPath = $"{ConfigDirectoryEvo}\\backup";
    static readonly string MessagesPath = $"{ConfigDirectoryEvo}\\messages";
    static readonly string LogsPath = $"{ConfigDirectoryEvo}\\logs";
    static readonly string StartupPath = $"{ConfigDirectoryEvo}\\simpleStartup.manysql.enc";
    static readonly string CredentialsPathEvo = $"{ConfigDirectoryEvo}\\credentials.json.enc";
    static readonly string HistoryDatFilePath = $"{ConfigDirectoryEvo}\\history.dat.zst";
    AppOptions Config { get; set; }

    string SelectedTabIdFromStart { get; set; }
    
    bool AddToOrEditLoginData(string name, string database, string driver, string password, string userName, string server, string? port = null);
    void ClearTempSippetsObjects();
    bool DeleteFromLoginData(string name);

    /// <summary>
    /// Updates driver-specific options of a saved connection. No-op when the
    /// connection does not exist. This is the only supported way to mutate a
    /// stored <c>LoginDataModel</c> besides <c>AddToOrEditLoginData</c>.
    /// </summary>
    void SetAccessOptions(string name, JustyBase.PluginCommon.Models.AccessConnectionOptions? options);

    /// <summary>
    /// Persists only the application config (config.json.enc), without
    /// touching the credentials file. Use this when only <c>Config</c>
    /// changed — rewriting credentials on every settings change multiplies
    /// corruption windows for no reason.
    /// </summary>
    void SaveAppConfig();
    void SaveConfig();
    void SaveCredentials();

    string DownloadPluginsBasePath { get; }

    public static readonly List<string> ADDITIONAL_EXTENSIONS =
    [
        ".xlsb",".xlsx",".xls",".xlsm",".accdb",".mdb",".csv"
    ];

    string GetCurrentCopyVersion();

}

using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Documents;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace JustyBase.Helpers.Shared;

public static partial class SqlDocumentViewModelHelper
{
    public sealed record SqlExecutionPlan(
        bool SingleCommand,
        bool TabsWithRows,
        bool TimeoutOverride,
        bool ContinueOnError,
        int? ForcedTimeout,
        List<string> SqlStatements);

    public const string CurrentOptionsListDROP = "Drop";
    public const string CurrentOptionsListDDL = "Ddl";
    public const string CurrentOptionsListRECREATE = "Recreate";
    public const string CurrentOptionsListRENAME = "Rename";
    public const string CurrentOptionsListJUMP_TO = "Jump to";
    public const string CurrentOptionsListCREATE_FROM = "Create from";
    public const string CurrentOptionsListGROOM = "Groom";
    public const string CurrentOptionsListSTATS = "Generate statistics";
    public const string CurrentOptionsListSELECT = "Select";

    private const string RegionResultNamePrefix = "--REGION RESULT_NAME:";

    /// <summary>
    /// Extracts a result-tab title from a SQL comment of the form
    /// <c>--REGION RESULT_NAME:MyTitle ...</c>.
    /// Returns <paramref name="defaultTitle"/> when the pattern is absent.
    /// </summary>
    public static string ParseResultTitle(ReadOnlySpan<char> shortQuery, string defaultTitle)
    {
        if (shortQuery.StartsWith(RegionResultNamePrefix.AsSpan(), StringComparison.Ordinal))
        {
            int start = RegionResultNamePrefix.Length;
            int ind = shortQuery[start..].IndexOf(' ');
            if (ind != -1)
            {
                return shortQuery.Slice(start, ind).ToString();
            }
        }
        return defaultTitle;
    }

    public static bool NotSupportedFileExtension(string path)
    {
        return !path.EndsWithAny([".xlsb", ".xlsx", ".csv", ".csv.br", ".dat.br", ".csv.gz", ".dat.gz", ".csv.zst", ".dat.zst"]);
    }

    public static bool ShouldRunAsSingleCommand(bool singleCommandEnabled, string? option)
    {
        if (singleCommandEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(option))
        {
            return false;
        }

        return option.Contains("|SingleBath", StringComparison.Ordinal)
            || option == ".xlsb"
            || option == ".xlsx";
    }

    public static bool RequiresExportPathSelection(string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return false;
        }

        return option.StartsWith(".xlsb", StringComparison.Ordinal)
            || option.Contains(".csv", StringComparison.Ordinal)
            || option.StartsWith(".parquet", StringComparison.Ordinal);
    }

    public static readonly Dictionary<string, string> KnownParams = [];
    public static List<string> GetVariableValuesP1(string query)
    {
        var toAsk = new List<string>();
        foreach (Match match in rxParam.Matches(query.CreateCleanSql()).Cast<Match>())
        {
            var variableTxt = match.Groups["param"].Value.ToUpper();
            if (!toAsk.Contains(variableTxt))
            {
                toAsk.Add(variableTxt);
                KnownParams.TryAdd(variableTxt, "");
            }
        }
        return toAsk;
    }


    public static List<string> ConvertSqlTextToListOfSqls(bool singleCommandLocal, string query)
    {
        List<string> sqls;
        if (singleCommandLocal)
        {
            sqls = [query];
        }
        else
        {
            sqls = query.MySplitForSqlSplit(';');
        }

        return sqls;
    }

    public static SqlExecutionPlan BuildExecutionPlan(bool singleCommandEnabled, string? option, string query, bool continueOnErrorCurrent)
    {
        var singleCommand = ShouldRunAsSingleCommand(singleCommandEnabled, option);
        var tabsWithRows = query.StartsWith(DatabaseService.TABS_WITH_ROWS);
        var timeoutOverride = query.Contains(DatabaseService.TIMEOUT_OVERRIDE);
        var continueOnError = continueOnErrorCurrent || query.Contains(DatabaseService.CONTINUE_ON_ERROR);
        var forcedTimeout = FindForcedTimeout(query);
        var sqlStatements = ConvertSqlTextToListOfSqls(singleCommand, query);

        return new SqlExecutionPlan(
            singleCommand,
            tabsWithRows,
            timeoutOverride,
            continueOnError,
            forcedTimeout,
            sqlStatements);
    }

    private static readonly char[] _newLiness = ['\r', '\n'];
    public static int? FindForcedTimeout(string query)
    {
        int? FORCED_TIMEOUT = null;
        var i1 = query.IndexOf(DatabaseService.TIMEOUT_OVERRIDE, StringComparison.Ordinal) + DatabaseService.TIMEOUT_OVERRIDE.Length + 1;
        if (i1 < query.Length - 1)
        {
            var i2 = query.IndexOfAny(_newLiness, i1);
            if (i1 != -1 && i2 > i1)
            {
                string timeoutTxt = query[(i1)..i2].Trim();
                if (int.TryParse(timeoutTxt, out var forcedTimeout))
                {
                    FORCED_TIMEOUT = forcedTimeout;
                }
            }
        }

        return FORCED_TIMEOUT;
    }


    [GeneratedRegex("(?<exportName>((___)|@)expCsv|((___)|@)expXlsx): (?<sql>.*)[\\s\\r\\n\\t]+->[\\s\\r\\n\\t]+(?<filePath>([-zżźćńółęąśa-z0-9\\\\:_\\.\\s]*\\.(xlsx|xlsb|dat|justData|parquet|[a-z]{3,4})|nul))([\\s\\r\\n\\t]+{[\\s\\r\\n\\t]+(?<options>.*)[\\s\\r\\n\\t]+})?", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex rxExportRegexGen();
    public static readonly Regex rxExportCsvXlsx = rxExportRegexGen();

    [GeneratedRegex(@"^\s*declare\s+(?<sessionVar>&[a-z]{1}[a-z_\d]*)\s*=\s*(?<sessionValue>[^;]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex rxSessionVariableDefineGen();
    public static readonly Regex RxSessionVariableDefine = rxSessionVariableDefineGen();

    [GeneratedRegex(@"(?<param>\$[a-zA-Z]{1}[a-zA-Z_\d]*)", RegexOptions.CultureInvariant)]
    private static partial Regex rxParamGen();
    public static readonly Regex rxParam = rxParamGen();

    public static readonly Regex DatabaseSchemaTableRegex = new(@"(((?<part1>\w+)\.)?(?<part2>\w*)\.)?(?<part3>\w+)");

    public static readonly Regex SleepRegex = new(@"(^|(\r\n)+)@sleep: (?<num>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly Regex ExtractRegex = new(@"(^|(\r\n)+)@extract: (?<path>.+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly Regex CompressRegex = new(@"(^|(\r\n)+)@compress: (?<path>.+) (?<mode>\w+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly Regex ChangeConnectionRegex = new(@"(^|(\r\n)+)@change_connection: (?<connectionName>\w+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void SetConnectionList(IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools, ISimpleLogger simpleLogger, bool force = false)
    {
        if (_connectionsList is null || force)
        {
            _connectionsList ??= [];
            _connectionsList.Clear();
            foreach (var (item, value) in generalApplicationData.LoginDataDic)
            {
                DatabaseTypeEnum type = DatabaseServiceHelpers.StringToDatabaseTypeEnum(value.Driver);

                var conItem = new ConnectionItem(item, type)
                {
                    DefaultDatabase = value.Database,
                    DatabaseList = []
                };
                if (!string.IsNullOrWhiteSpace(value.Database))
                {
                    conItem.DefaultDatabase = value.Database;
                    conItem.DatabaseList.Add(value.Database);
                }
                _connectionsList.Add(conItem);
            }
        }
        try
        {
            foreach (var item in generalApplicationData.GetDocumentsKeyValueCollection())
            {
                item.Value?.HotDocumentViewModelAsT<SqlDocumentViewModel>()?.RefreshConnectionList();
            }
        }
        catch (Exception ex1)
        {
            messageForUserTools.ShowSimpleMessageBoxInstance(ex1);
            simpleLogger.TrackError(ex1, isCrash: false);
        }
    }

    private static ObservableCollection<ConnectionItem> _connectionsList;
    public static ObservableCollection<ConnectionItem> ConnectionsList => _connectionsList;

    public static int GetConnectionIndex(ReadOnlySpan<char> word)
    {
        for (int i = 0; i < _connectionsList.Count; i++)
        {
            ConnectionItem item = _connectionsList[i];
            if (item.Name.AsSpan().Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }


    public static DbConnection OpenConnectionIfNeeded(IDatabaseService actualDatabaseService, DbConnection con, ISimpleLogger simpleLogger)
    {
        try
        {
            if (con.State != ConnectionState.Open)
            {
                con.Open();
            }
        }
        catch (Exception ex1)
        {
            simpleLogger.TrackError(ex1, isCrash: false);
            if (ConnectionRecoveryPolicy.IsBrokenConnection(ex1, con.State)
                && ConnectionRecoveryPolicy.CanAttemptReconnect(attemptsUsed: 0, isCancelled: false))
            {
                simpleLogger.TrackError(
                    new InvalidOperationException("Auto-recovery: reconnecting once after broken connection.", ex1),
                    isCrash: false);
                try
                {
                    con.Dispose();
                }
                catch
                {
                    // ignore dispose of broken connection
                }

                con = actualDatabaseService.GetConnection(null, pooling: false);
                con.Open();
            }
            else
            {
                throw;
            }
        }

        return con;
    }

}

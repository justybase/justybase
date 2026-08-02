using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommons;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Helpers.Shared;
using JustyBase.Services.Documents;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Tools;
using JustyBase.Views;

namespace JustyBase.Services;

public class SqlVariableProcessor : ISqlVariableProcessor
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly VariablesViewModel _variablesViewModel;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private readonly DataTable _tableToCompute = new();

    public SqlVariableProcessor(
        IGeneralApplicationData generalApplicationData,
        ISimpleLogger simpleLogger,
        VariablesViewModel variablesViewModel,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _simpleLogger = simpleLogger;
        _variablesViewModel = variablesViewModel;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _databaseServiceResolver = databaseServiceResolver;
    }

    private object Evaluate(string expression)
    {
        object result = expression;
        try
        {
            result = _tableToCompute.Compute(expression, "");
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }

        return result;
    }

    public string ReplaceVariablesP2(string query, List<string> toAsk)
    {
        // $DATA2, before $DATA
        toAsk.Sort(delegate (string x, string y)
        {
            if (x.Length != y.Length) return y.Length.CompareTo(x.Length);
            return string.Compare(y, x, StringComparison.Ordinal);
        });

        foreach (var variableTxt in toAsk)
        {
            _variablesViewModel.AddVariableFromEditorOrByPlus(variableTxt[1..], SqlDocumentViewModelHelper.KnownParams[variableTxt]);
        }

        return query.ReplaceVariablesInSql(toAsk, SqlDocumentViewModelHelper.KnownParams);
    }

    public string ReplaceSessionVariables(string query)
    {
        var tab = _variablesViewModel.UpdateVariablesCompletition();
        return query.ReplaceVariablesInSql(tab.Keys.ToList(), tab, variableStart: '&');
    }

    public async Task<(string Query, bool IsCancel)> AskAndReplaceVariablesFromUserAsync(string query)
    {
        List<string> toAsk = SqlDocumentViewModelHelper.GetVariableValuesP1(query);

        if (toAsk.Count > 0)
        {
            var parametrViewModel = new SqlParameterViewModel(toAsk, SqlDocumentViewModelHelper.KnownParams);
            var paramWindow = new SqlParameterWindow
            {
                DataContext = parametrViewModel
            };
            
            await paramWindow.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
            
            if (parametrViewModel.IsCancel)
            {
                return (query, true);
            }

            query = ReplaceVariablesP2(query, toAsk);
        }
        return (query, false);
    }

    public async ValueTask AddSessionVariableAsync(Match m, DbConnection? con, string localTitle, IDatabaseService? databaseService, string selectedConnectionName)
    {
        string variableValue = m.Groups["sessionValue"].Value;
        string val = ReplaceSessionVariables(variableValue);
        object val2 = val;
        try
        {
            if (!val.StartsWith("SQL_", StringComparison.Ordinal))
            {
                val2 = Evaluate(val);
            }
            else
            {
                if (con is not null)
                {
                    IDatabaseService service = await Task.Run(() => _databaseServiceResolver.GetDatabaseService(_generalApplicationData, selectedConnectionName));
                    con = service.GetConnection(null);
                    con.Open();
                }
            if (val.StartsWith("SQL_RESULT[", StringComparison.Ordinal))
                {
                    string sql = val["SQL_RESULT[".Length..^1];
                    using (var cmd = con.CreateCommand())
                    {
                        if (databaseService is not null)
                        {
                            SetTimeoutForCommand(localTitle, databaseService, cmd);
                        }
                        cmd.CommandText = sql;
                        val2 = await Task.Run(() => cmd.ExecuteScalar());
                    }
                }
            else if (val.StartsWith("SQL_RECORDS_AFFECTED[", StringComparison.Ordinal))
                {
                    string sql = val["SQL_RECORDS_AFFECTED[".Length..^1];
                    using (var cmd = con.CreateCommand())
                    {
                        if (databaseService is not null)
                        {
                            SetTimeoutForCommand(localTitle, databaseService, cmd);
                        }
                        cmd.CommandText = sql;
                        val2 = await Task.Run(() => cmd.ExecuteNonQuery());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
        _variablesViewModel.AddVariableFromEditorOrByPlus(m.Groups["sessionVar"].Value[1..], val2?.ToString() ?? "");
    }

    private void SetTimeoutForCommand(string? localTile, IDatabaseService? acutalDatabaseService, DbCommand cmd)
    {
        if (_generalApplicationData.Config.CommandTimeout == 0)
        {
            return;
        }

        try
        {
            cmd.CommandTimeout = _generalApplicationData.Config.CommandTimeout;
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
    }
}

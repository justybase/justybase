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
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Services;

public class SqlVariableProcessor : ISqlVariableProcessor
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly VariablesViewModel _variablesViewModel;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IMessageForUserTools _messageForUserTools;

    public SqlVariableProcessor(
        IGeneralApplicationData generalApplicationData,
        ISimpleLogger simpleLogger,
        VariablesViewModel variablesViewModel,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IMessageForUserTools messageForUserTools)
    {
        _generalApplicationData = generalApplicationData;
        _simpleLogger = simpleLogger;
        _variablesViewModel = variablesViewModel;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _messageForUserTools = messageForUserTools;
    }

    private object Evaluate(string expression)
    {
        object result = expression;
        try
        {
            using var tableToCompute = new DataTable();
            result = tableToCompute.Compute(expression, "");
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

    public async ValueTask AddSessionVariableAsync(
        Match m,
        DbConnection? con,
        string localTitle,
        IDatabaseService? databaseService,
        string selectedConnectionName,
        CancellationToken cancellationToken = default)
    {
        string variableValue = m.Groups["sessionValue"].Value;
        string val = ReplaceSessionVariables(variableValue);
        object val2 = val;

        cancellationToken.ThrowIfCancellationRequested();
        if (!val.StartsWith("SQL_", StringComparison.OrdinalIgnoreCase))
        {
            val2 = Evaluate(val);
        }
        else if (val.StartsWith("SQL_RESULT[", StringComparison.OrdinalIgnoreCase)
                 || val.StartsWith("SQL_RECORDS_AFFECTED[", StringComparison.OrdinalIgnoreCase))
        {
            if (con is null)
            {
                throw new InvalidOperationException(
                    "SQL session-variable commands require the active execution connection.");
            }

            bool scalar = val.StartsWith("SQL_RESULT[", StringComparison.OrdinalIgnoreCase);
            string prefix = scalar ? "SQL_RESULT[" : "SQL_RECORDS_AFFECTED[";
            if (!val.EndsWith(']') || val.Length <= prefix.Length)
            {
                throw new FormatException($"Invalid {prefix[..^1]} session-variable expression.");
            }

            string sql = val[prefix.Length..^1];
            var risks = new JustyBase.Core.Risk.SqlRiskAnalysisService().Analyze(
                sql,
                databaseService?.DatabaseType == DatabaseTypeEnum.NetezzaSQL ? "NetezzaSQL" : null);
            if (risks.Count > 0)
            {
                string warning = string.Join(
                    Environment.NewLine,
                    risks.Select(risk => $"• {risk.Message}"));
                bool confirmed = await _messageForUserTools.ShowConfirmationDialogAsync(
                    warning,
                    "SQL risk confirmation");
                if (!confirmed)
                {
                    // The execution service treats this as a non-error abort. In
                    // particular, do not continue with the outer script after a
                    // denied SQL_RESULT/SQL_RECORDS_AFFECTED command.
                    throw new OperationCanceledException("Nested SQL risk was not confirmed.");
                }
            }

            using DbCommand cmd = con.CreateCommand();
            if (databaseService is not null)
            {
                SetTimeoutForCommand(localTitle, databaseService, cmd);
            }

            cmd.CommandText = sql;
            val2 = scalar
                ? await Task.Run(() => cmd.ExecuteScalar(), cancellationToken).ConfigureAwait(false)
                : await Task.Run(() => cmd.ExecuteNonQuery(), cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
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

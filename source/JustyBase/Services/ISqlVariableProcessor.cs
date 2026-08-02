using System.Data.Common;
using System.Text.RegularExpressions;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

public interface ISqlVariableProcessor
{
    ValueTask AddSessionVariableAsync(Match m, DbConnection? con, string localTitle, IDatabaseService? databaseService, string selectedConnectionName);
    string ReplaceVariablesP2(string query, List<string> toAsk);
    string ReplaceSessionVariables(string query);
    Task<(string Query, bool IsCancel)> AskAndReplaceVariablesFromUserAsync(string query);
}

using System.Data.Common;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

/// <summary>
/// Interface for managing SQL query results (adding new results, closing previous results).
/// Implemented by DockFactory to handle result tabs.
/// </summary>
public interface ISqlResultManager
{
    /// <summary>
    /// Closes previous results for a document.
    /// </summary>
    void ClosePrevResults(string id);

    /// <summary>
    /// Adds a new result to the document.
    /// </summary>
    void AddNewResult((IDatabaseService? dbService, DbDataReader? rdr, string errorMessage) res, string id, int queryNum, ref int abortUbound, string? sql, DbCommand? command, string? title);
}

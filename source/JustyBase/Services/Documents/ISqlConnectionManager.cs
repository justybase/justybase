using System.Data.Common;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services.Documents;

public interface ISqlConnectionManager
{
    DbConnection GetOrCreateConnection(bool doPooling, bool keepOpen, IDatabaseService service);
    DbConnection? TryReconnectOnce(IDatabaseService service, DbConnection broken, bool doPooling, bool keepOpen, bool isCancelled, Exception? cause);
    void ResetReconnectCounter();
    void CloseConnection();
    void EmergencyCleanup(Func<Task> abortAction);
    bool HasOpenConnection { get; }
    int ReconnectAttemptsUsed { get; }
}

using System.Data;

namespace JustyBase.Services.Documents;

/// <summary>
/// Auto-recovery rules for a single reconnect after a broken connection (not keep-open).
/// </summary>
public static class ConnectionRecoveryPolicy
{
    public const int MaxReconnectAttempts = 1;

    public static bool IsBrokenConnection(Exception? ex, ConnectionState state)
    {
        if (state is ConnectionState.Broken)
        {
            return true;
        }

        if (ex is null)
        {
            return state is ConnectionState.Closed;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("The Connection is broken", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Connection is broken", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Timeout while getting a connection from pool", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Connection was closed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not in a valid state", StringComparison.OrdinalIgnoreCase)
            || message.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("server was not found", StringComparison.OrdinalIgnoreCase)
            || (state is ConnectionState.Closed && LooksLikeConnectivityFailure(message));
    }

    public static bool CanAttemptReconnect(int attemptsUsed, bool isCancelled)
        => !isCancelled && attemptsUsed < MaxReconnectAttempts;

    private static bool LooksLikeConnectivityFailure(string message)
        => message.Contains("network", StringComparison.OrdinalIgnoreCase)
           || message.Contains("socket", StringComparison.OrdinalIgnoreCase)
           || message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
}

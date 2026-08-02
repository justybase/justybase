using System;

namespace JustyBase.PluginDatabaseBase.Database;

public static class DatabaseFilterHelper
{
    public static bool MatchesPrefixOrUnderscore(string value, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return value.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
            || value.Contains("_" + filter, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesContains(string value, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

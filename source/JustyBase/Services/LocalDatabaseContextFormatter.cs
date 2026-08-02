using System.Text;

namespace JustyBase.Services;

public static class LocalDatabaseContextFormatter
{
    public static string BuildNoActiveConnectionContext()
        => "[DATABASE_CONTEXT]\nNo active database connection.\n[/DATABASE_CONTEXT]";

    public static string BuildDatabaseContext(string connectionName, string databaseName, IReadOnlyList<string> schemas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[DATABASE_CONTEXT]");
        sb.AppendLine($"Active Connection: {connectionName}");
        sb.AppendLine($"Active Database: {databaseName}");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: Always use qualified object names:");
        sb.AppendLine($"  Preferred: {databaseName}.SCHEMA.OBJECT");
        sb.AppendLine($"  If schema unknown: {databaseName}..OBJECT (double dot - valid but less preferred)");
        sb.AppendLine($"  Example: SELECT * FROM {databaseName}.ADMIN.USERS");
        sb.AppendLine();
        sb.AppendLine("Available schemas:");
        foreach (var schema in schemas)
        {
            sb.AppendLine($"  - {databaseName}.{schema}");
        }
        sb.AppendLine("[/DATABASE_CONTEXT]");
        return sb.ToString().TrimEnd();
    }

    public static string BuildFallbackContext(string connectionName, string databaseName)
        => $"[DATABASE_CONTEXT]\nActive Database: {databaseName}\nConnection: {connectionName}\nFormat: {databaseName}.SCHEMA.OBJECT or {databaseName}..OBJECT\n[/DATABASE_CONTEXT]";
}

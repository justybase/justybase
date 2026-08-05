using System.Globalization;
using System.Data.Common;
using JustyBase.NetezzaDriver;
using Xunit.Sdk;
using NetezzaService = NetezzaDotnetPlugin.Netezza;

namespace JustyBase.IntegrationTests;

/// <summary>
/// Shared helpers for environment-gated live NetezzaDotnetPlugin.Netezza tests. Requires NZ_DEV_* variables
/// (same contract as <see cref="NetezzaConnectivityTests"/> and scripts/test-netezza-integration.ps1).
/// </summary>
internal static class NetezzaLiveTestHost
{
    private sealed record Env(string Host, string Database, string User, string Password, int Port);

    private static readonly Lazy<Env> s_env = new(() =>
    {
        string host = GetRequiredVariable("NZ_DEV_HOST");
        string database = GetRequiredVariable("NZ_DEV_DATABASE");
        string user = GetRequiredVariable("NZ_DEV_USER");
        string password = GetRequiredVariable("NZ_DEV_PASSWORD");
        int port = GetRequiredPort();
        return new Env(host, database, user, password, port);
    });

    public static string Database => s_env.Value.Database;

    public static NzConnection OpenConnection()
    {
        Env env = s_env.Value;
        NzConnection connection = new(env.User, env.Password, env.Host, env.Database, env.Port);
        connection.Open();
        return connection;
    }

    /// <summary>Creates the real NetezzaDotnetPlugin.Netezza database service exactly as the app registers it.</summary>
    public static NetezzaService CreateService()
    {
        Env env = s_env.Value;
        return new NetezzaService(env.User, env.Password, env.Port.ToString(CultureInfo.InvariantCulture), env.Host, env.Database, connectionTimeout: 15);
    }

    public static string CreateLogDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jb-nz-live", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string CreateTableName()
        => "JB_RT_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    public static void Execute(NzConnection connection, string sql)
    {
        using NzCommand command = connection.CreateCommand(sql);
        command.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(NzConnection connection, string sql)
    {
        using NzCommand command = connection.CreateCommand(sql);
        return command.ExecuteScalar();
    }

    public static List<object?[]> ExecuteReaderRows(NzConnection connection, string sql, int fieldCount)
    {
        using NzCommand command = connection.CreateCommand(sql);
        using DbDataReader reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            int actual = Math.Min(fieldCount, reader.FieldCount);
            var cells = new object?[actual];
            for (int i = 0; i < actual; i++)
            {
                cells[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(cells);
        }
        return rows;
    }

    public static void TryDrop(NzConnection connection, string table)
    {
        try
        {
            Execute(connection, $"DROP TABLE {table}");
        }
        catch
        {
        }
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private static int GetRequiredPort()
    {
        string value = GetRequiredVariable("NZ_DEV_PORT");
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            throw new XunitException("NZ_DEV_PORT must be a valid TCP port number.");
        }

        return port;
    }

    private static string GetRequiredVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new XunitException($"Required environment variable {name} is missing.");
    }
}



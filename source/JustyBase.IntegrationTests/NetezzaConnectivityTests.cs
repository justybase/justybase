using System.Globalization;
using JustyBase.NetezzaDriver;
using Xunit.Sdk;

namespace JustyBase.IntegrationTests;

/// <summary>
/// Optional live Netezza connectivity check. Requires NZ_DEV_* environment variables.
/// Not part of the default PR CI gate — run via workflow_dispatch or scripts/test-netezza-integration.ps1.
/// </summary>
public sealed class NetezzaConnectivityTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void DevelopmentDatabase_CanExecuteReadOnlyScalarQuery()
    {
        NzConnectionStringBuilder builder = new()
        {
            Host = GetRequiredVariable("NZ_DEV_HOST"),
            Database = GetRequiredVariable("NZ_DEV_DATABASE"),
            UserName = GetRequiredVariable("NZ_DEV_USER"),
            Password = GetRequiredVariable("NZ_DEV_PASSWORD"),
            Port = GetRequiredPort(),
            Timeout = 10
        };

        try
        {
            using NzConnection connection = new(builder.ConnectionString);
            connection.Open();
            using NzCommand command = new("SELECT 1", connection)
            {
                CommandTimeout = 15
            };

            object? result = command.ExecuteScalar();

            Assert.NotNull(result);
            Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            // Provider diagnostics can contain connection details, so expose only
            // the exception type and keep credentials out of test output.
            throw new XunitException($"Netezza integration check failed ({exception.GetType().Name}).");
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

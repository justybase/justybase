using Microsoft.Data.Sqlite;

namespace JustyBase.Tests;

/// <summary>
/// CI-friendly embedded DB smoke (portfolio equivalent of NetezzaSQL e2e without secrets).
/// </summary>
public sealed class SqliteSmokeTests
{
    [Fact]
    public void InMemory_SelectOne_ReturnsExpectedScalar()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        var result = command.ExecuteScalar();

        Assert.NotNull(result);
        Assert.Equal(1L, Convert.ToInt64(result));
    }

    [Fact]
    public void InMemory_CreateTableInsertSelect_RoundTripsRow()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE demo(id INTEGER PRIMARY KEY, name TEXT NOT NULL);
                INSERT INTO demo(id, name) VALUES (1, 'justy');
                """;
            cmd.ExecuteNonQuery();
        }

        using var query = connection.CreateCommand();
        query.CommandText = "SELECT name FROM demo WHERE id = 1;";
        using var reader = query.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("justy", reader.GetString(0));
        Assert.False(reader.Read());
    }
}

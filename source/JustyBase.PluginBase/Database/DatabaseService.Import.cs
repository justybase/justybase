using JustyBase.PluginCommon.Contracts;
using System.Data.Common;

namespace JustyBase.PluginDatabaseBase.Database;

public abstract partial class DatabaseService
{
    public virtual async Task DbSpecificImportPart(IDbImportJob importJob, string randName, Action<string>? progress, bool tableExists = false)
    {
        await Task.Run(() =>
        {
            using var conn = GetConnection(null);
            if (conn is null)
            {
                return;
            }

            conn.Open();
            if (!tableExists)
            {
                string[] headers = importJob.ReturnHeadersWithDataTypes(this.DatabaseType);
                string SQL = $"CREATE TABLE {randName} ({string.Join(',', headers)});";
                using DbCommand tmpCmd = conn.CreateCommand();
                tmpCmd.CommandText = SQL;
                tmpCmd.ExecuteNonQuery();
            }

            var reader = importJob.AsReader;
            long rowCount = 0;
            int batchSize = 1000;
            List<string> valueBatch = new(batchSize);

            using var transaction = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();

            try
            {
                object[] values = null!;
                while (reader.Read())
                {
                    values ??= new object[reader.FieldCount];
                    reader.GetValues(values);
                    var valuesList = values.Select(v => v == DBNull.Value || v == null ? "NULL" :
                        v is string ? $"'{v.ToString()?.Replace("'", "''")}'" :
                        v is DateTime dt ? $"'{dt:yyyy-MM-dd HH:mm:ss}'" :
                        v.ToString()).ToList();

                    valueBatch.Add($"({string.Join(",", valuesList)})");
                    rowCount++;

                    if (valueBatch.Count >= batchSize)
                    {
                        ExecuteBatch(cmd, randName, valueBatch);
                        progress?.Invoke($"Copied {rowCount:N0}");
                        valueBatch.Clear();
                    }
                }

                // Insert remaining rows
                if (valueBatch.Count > 0)
                {
                    ExecuteBatch(cmd, randName, valueBatch);
                    progress?.Invoke($"Copied {rowCount:N0}");
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                progress?.Invoke($"ERROR! Message: {ex.Message}");
                throw;
            }
            finally
            {
                conn.Close();
            }
        });
    }

    private static void ExecuteBatch(DbCommand cmd, string tableName, List<string> valueBatch)
    {
        if (valueBatch.Count == 0) return;

        cmd.CommandText = $"INSERT INTO {tableName} VALUES {string.Join(",", valueBatch)}";
        cmd.ExecuteNonQuery();
    }

}

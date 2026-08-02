namespace JustyBase.Services.Documents;

public sealed record DatabaseListSyncPlan(
    IReadOnlyList<string> DatabasesToAdd,
    string? UpdatedSelectedDatabase);

public interface IDatabaseListSyncService
{
    DatabaseListSyncPlan BuildSyncPlan(IEnumerable<string> availableDatabases, IEnumerable<string> currentDatabases, string? selectedDatabase);
}

public sealed class DatabaseListSyncService : IDatabaseListSyncService
{
    public DatabaseListSyncPlan BuildSyncPlan(IEnumerable<string> availableDatabases, IEnumerable<string> currentDatabases, string? selectedDatabase)
    {
        var currentDatabaseList = currentDatabases.ToList();
        var databasesToAdd = availableDatabases
            .Where(database => !currentDatabaseList.Contains(database))
            .ToList();

        string? updatedSelectedDatabase = selectedDatabase;
        int totalDatabasesCount = currentDatabaseList.Count + databasesToAdd.Count;
        if (string.IsNullOrWhiteSpace(selectedDatabase) && totalDatabasesCount == 1)
        {
            updatedSelectedDatabase = databasesToAdd.FirstOrDefault() ?? currentDatabaseList[0];
        }

        return new(databasesToAdd, updatedSelectedDatabase);
    }
}

using JustyBase.Services.Documents;

namespace JustyBase.Tests;

public sealed class DatabaseListSyncServiceTests
{
    [Fact]
    public void BuildSyncPlan_AddsOnlyMissingDatabases()
    {
        var service = new DatabaseListSyncService();

        var plan = service.BuildSyncPlan(
            availableDatabases: ["MAIN", "REPORTING", "MAIN"],
            currentDatabases: ["MAIN"],
            selectedDatabase: "MAIN");

        Assert.Equal(["REPORTING"], plan.DatabasesToAdd);
        Assert.Equal("MAIN", plan.UpdatedSelectedDatabase);
    }

    [Fact]
    public void BuildSyncPlan_SelectsOnlyAddedDatabase_WhenSelectedDatabaseIsEmpty()
    {
        var service = new DatabaseListSyncService();

        var plan = service.BuildSyncPlan(
            availableDatabases: ["MAIN"],
            currentDatabases: [],
            selectedDatabase: null);

        Assert.Equal(["MAIN"], plan.DatabasesToAdd);
        Assert.Equal("MAIN", plan.UpdatedSelectedDatabase);
    }

    [Fact]
    public void BuildSyncPlan_SelectsOnlyExistingDatabase_WhenNothingNeedsToBeAdded()
    {
        var service = new DatabaseListSyncService();

        var plan = service.BuildSyncPlan(
            availableDatabases: ["MAIN"],
            currentDatabases: ["MAIN"],
            selectedDatabase: "");

        Assert.Empty(plan.DatabasesToAdd);
        Assert.Equal("MAIN", plan.UpdatedSelectedDatabase);
    }

    [Fact]
    public void BuildSyncPlan_KeepsExistingSelection_WhenAlreadySelected()
    {
        var service = new DatabaseListSyncService();

        var plan = service.BuildSyncPlan(
            availableDatabases: ["MAIN", "REPORTING"],
            currentDatabases: ["MAIN"],
            selectedDatabase: "MAIN");

        Assert.Equal(["REPORTING"], plan.DatabasesToAdd);
        Assert.Equal("MAIN", plan.UpdatedSelectedDatabase);
    }
}

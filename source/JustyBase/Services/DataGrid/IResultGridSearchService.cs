using JustyBase.Models;

namespace JustyBase.Services.DataGrid;

public interface IResultGridSearchService
{
    int ApplySearch(
        TableOfSqlResults resultsTable,
        string? searchText,
        Dictionary<int, AditionalOneFilter>? additionalValues,
        bool containsGeneralSearch);

    void ScheduleSearch(Action searchCallback);
}

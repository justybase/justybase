namespace JustyBase.Services.DataGrid;

public sealed class ResultGridSummaryRefreshService : IResultGridSummaryRefreshService
{
    public bool HaveColumnWidthsChanged(
        IReadOnlyList<double> currentColumnWidths,
        Dictionary<int, double> lastColumnWidths,
        double tolerance = 0.5)
    {
        ArgumentNullException.ThrowIfNull(currentColumnWidths);
        ArgumentNullException.ThrowIfNull(lastColumnWidths);

        bool changed = false;
        for (int i = 0; i < currentColumnWidths.Count; i++)
        {
            double currentWidth = currentColumnWidths[i];
            if (!lastColumnWidths.TryGetValue(i, out var lastWidth) || Math.Abs(lastWidth - currentWidth) > tolerance)
            {
                lastColumnWidths[i] = currentWidth;
                changed = true;
            }
        }

        var staleKeys = lastColumnWidths.Keys.Where(key => key >= currentColumnWidths.Count).ToList();
        foreach (int staleKey in staleKeys)
        {
            lastColumnWidths.Remove(staleKey);
            changed = true;
        }

        return changed;
    }

    public bool ShouldRefreshSummaryRow(bool showSummaryRow, int columnCount)
    {
        return showSummaryRow && columnCount > 0;
    }

    public bool ShouldRefreshGroupHeaderSummaries(int summaryCount, int groupCount)
    {
        return summaryCount > 0 && groupCount > 0;
    }
}

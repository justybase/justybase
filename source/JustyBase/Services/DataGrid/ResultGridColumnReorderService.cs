namespace JustyBase.Services.DataGrid;

/// <inheritdoc />
public sealed class ResultGridColumnReorderService : IResultGridColumnReorderService
{
    public bool CanReorderHeaders(string? sourceColumnHeader, string? targetColumnHeader)
    {
        if (string.IsNullOrEmpty(sourceColumnHeader) || string.IsNullOrEmpty(targetColumnHeader))
        {
            return false;
        }

        return !string.Equals(sourceColumnHeader, targetColumnHeader, StringComparison.Ordinal);
    }

    public int CalculateNewDisplayIndex(int sourceDisplayIndex, int targetDisplayIndex, bool insertAfter, int columnCount)
    {
        if (columnCount <= 0)
        {
            return 0;
        }

        int newDisplayIndex = insertAfter ? targetDisplayIndex + 1 : targetDisplayIndex;
        if (sourceDisplayIndex < newDisplayIndex)
        {
            newDisplayIndex--;
        }

        return Math.Max(0, Math.Min(newDisplayIndex, columnCount - 1));
    }
}

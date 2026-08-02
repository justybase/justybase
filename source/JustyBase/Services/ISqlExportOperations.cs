namespace JustyBase.Services;

/// <summary>
/// Service responsible for transforming clipboard data into SQL text
/// (PasteAsIn, PastClipAsSelectUnion).
/// </summary>
public interface ISqlExportOperations
{
    /// <summary>
    /// Transforms clipboard text into a SQL IN-clause using the given paste type (Text, Number, etc.).
    /// </summary>
    string BuildPasteAsIn(string pasteType, string clipboardText);

    /// <summary>
    /// Transforms clipboard table data into a SELECT ... UNION ALL SELECT ... SQL statement.
    /// </summary>
    string BuildSelectUnionFromClipboard(string clipboardText);
}

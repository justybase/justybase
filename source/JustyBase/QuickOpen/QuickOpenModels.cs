namespace JustyBase.QuickOpen;

[Flags]
public enum QuickOpenSource
{
    None = 0,
    Files = 1,
    Git = 2,
    Open = 4,
}

public enum QuickOpenHitKind
{
    FileName,
    Content,
    Symbol,
    Action,
    SchemaObject,
    History,
}

public sealed record QuickOpenCandidate(
    string DisplayName,
    string? FilePath,
    string? DocumentId,
    string? InMemoryText,
    QuickOpenSource Sources,
    string DisplayPath);

public sealed record QuickOpenHit(
    QuickOpenHitKind Kind,
    string DisplayName,
    string DisplayPath,
    string? FilePath,
    string? DocumentId,
    QuickOpenSource Sources,
    int Score,
    string? Query,
    int? LineNumber = null,
    int? MatchIndex = null,
    int? MatchLength = null,
    string? Snippet = null,
    string? ActionId = null,
    string? InsertText = null,
    string? Payload = null);

public sealed record QuickOpenListEntry(
    bool IsHeader,
    string? HeaderText,
    QuickOpenHit? Hit);

/// <summary>A line and column in the active editor, both 1-based. A bare ":42" targets column 1.</summary>
public readonly record struct QuickOpenGotoTarget(int Line, int Column = 1);

public enum QuickOpenSchemaObjectKind
{
    Table,
    View,
    Column,
}

/// <summary>A catalog object flattened for Go to Everything. The view model builds these from the
/// in-memory schema provider; the Quick Open layer only ranks and displays them.</summary>
public sealed record QuickOpenSchemaObject(
    string Name,
    QuickOpenSchemaObjectKind Kind,
    string Database,
    string Schema,
    string? TableName,
    string DisplayDetail,
    string InsertText);

/// <summary>The extra sources Go to Everything may search. Null means the source is not available;
/// leaving all of them null gives the classic file-and-content Quick Open.</summary>
public sealed record QuickOpenSearchProviders(
    Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? Symbols = null,
    Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? Actions = null,
    Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? Schema = null,
    Func<string, CancellationToken, Task<IReadOnlyList<QuickOpenHit>>>? History = null,
    bool SearchEverything = false);

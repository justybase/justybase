namespace JustyBase.Services;

/// <summary>Keyword casing for the SQL formatter.</summary>
public enum SqlKeywordCase
{
    Upper,
    Lower,
    Preserve,
}

/// <summary>Host-side formatter options (VS Code parity: tabWidth/keywordCase).</summary>
public sealed record SqlFormatOptions(
    int IndentWidth = 2,
    SqlKeywordCase KeywordCase = SqlKeywordCase.Upper);

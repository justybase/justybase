namespace JustyBase.Services.FileExplorer;

/// <summary>
/// Pure (UI-free) mapping of file path -&gt; VS Code style badge glyph + color.
/// No Avalonia types here so it stays unit-testable and keeps ViewModels clean.
/// Brushes are created in <c>Converters/FileExtensionBadgeConverters.cs</c>.
/// </summary>
public static class FileExtensionStyle
{
    /// <summary>Glyph shown on the file badge, e.g. "SQL", "JS", "{}". Empty when unknown.</summary>
    public static string GetGlyph(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string fileName = System.IO.Path.GetFileName(path.Trim());
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        // Dotfiles: .gitignore, .vscodeignore, .editorconfig ...
        if (fileName.StartsWith('.') && fileName.LastIndexOf('.') == 0)
        {
            return "◆";
        }

        string lowerName = fileName.ToLowerInvariant();
        string ext = System.IO.Path.GetExtension(lowerName);

        // Well-known files without (or with misleading) extension.
        if (string.IsNullOrEmpty(ext))
        {
            return lowerName switch
            {
                "license" or "licence" => "§",
                "dockerfile" => "D",
                "makefile" => "M",
                _ => string.Empty,
            };
        }

        // Special full names that deserve their own badge even with extension.
        if (lowerName is "package.json" or "package-lock.json")
        {
            return "{}";
        }

        return ext switch
        {
            ".sql" => "SQL",
            ".csv" => "CSV",
            ".tsv" => "TSV",
            ".xlsx" or ".xls" or ".xlsm" or ".xlsb" => "X",
            ".accdb" or ".mdb" => "DB",
            ".parquet" => "Pq",
            ".7z" or ".zip" or ".rar" or ".gz" or ".br" or ".zst" or ".vsix" or ".tar" => "≡",
            ".txt" or ".log" => "T",
            ".json" => "{}",
            ".md" or ".markdown" => "M",
            ".js" or ".mjs" or ".cjs" => "JS",
            ".ts" or ".mts" or ".cts" => "TS",
            ".cs" => "C#",
            ".py" => "Py",
            ".ps1" => "PS",
            ".vb" => "VB",
            ".xml" or ".dtsx" => "<>",
            ".html" or ".htm" => "<>",
            ".css" or ".scss" or ".less" => "#",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".svg" or ".webp" => "▦",
            ".pdf" => "PDF",
            _ => string.Empty,
        };
    }

    /// <summary>Hex color for the badge glyph. Always returns a value; unknown -&gt; grey.</summary>
    public static string GetColorHex(string? path)
    {
        string glyph = GetGlyph(path);
        if (string.IsNullOrEmpty(glyph))
        {
            return "#858585";
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return "#858585";
        }

        string fileName = System.IO.Path.GetFileName(path.Trim()).ToLowerInvariant();
        string ext = System.IO.Path.GetExtension(fileName);

        // Dotfiles grey.
        if (fileName.StartsWith('.') && fileName.LastIndexOf('.') == 0)
        {
            return "#858585";
        }

        return ext switch
        {
            ".sql" => "#569CD6",
            ".csv" or ".tsv" or ".parquet" => "#4EC9B0",
            ".xlsx" or ".xls" or ".xlsm" or ".xlsb" => "#21A366",
            ".accdb" or ".mdb" => "#C8423E",
            ".7z" or ".zip" or ".rar" or ".gz" or ".br" or ".zst" or ".vsix" or ".tar" => "#D7BA7D",
            ".txt" or ".log" => "#858585",
            ".json" => "#E8B900",
            ".md" or ".markdown" => "#519ABA",
            ".js" or ".mjs" or ".cjs" => "#E8D44D",
            ".ts" or ".mts" or ".cts" => "#519ABA",
            ".cs" => "#178600",
            ".py" => "#3572A5",
            ".ps1" => "#5391FE",
            ".vb" => "#00509E",
            ".xml" or ".dtsx" or ".html" or ".htm" => "#E44D26",
            ".css" or ".scss" or ".less" => "#42A5F5",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".svg" or ".webp" => "#A074C4",
            ".pdf" => "#E81123",
            _ => fileName is "license" or "licence" or "dockerfile" or "makefile" or "package.json" or "package-lock.json"
                ? "#D7BA7D"
                : "#858585",
        };
    }

    public static bool HasBadge(string? path) => !string.IsNullOrEmpty(GetGlyph(path));
}

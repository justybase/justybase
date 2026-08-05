using System.Runtime.CompilerServices;
using JustyBase.Common.Contracts;
using JustyBase.Core.Database;
using JustyBase.Editor;
using JustyBase.Editor.CompletionProviders;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

/// <summary>
/// Avalonia host adapter for the shared <see cref="ISqlDbWordListProvider"/>
/// contract. Delegates the query to the existing port-based word-list engine
/// (<see cref="AutocompleteService"/> over the plugin <see cref="IDatabaseService"/>)
/// and maps its UI items (<see cref="CompletionDataSql"/>) onto the neutral
/// <see cref="SqlWordListItem"/> contract. The hot completion path is unchanged.
/// </summary>
public sealed class DbWordListProvider : ISqlDbWordListProvider
{
    private readonly AutocompleteService _autocompleteService;
    private readonly Func<string, IDatabaseService?> _databaseServiceResolver;

    public DbWordListProvider(
        AutocompleteService autocompleteService,
        Func<string, IDatabaseService?> databaseServiceResolver)
    {
        _autocompleteService = autocompleteService ?? throw new ArgumentNullException(nameof(autocompleteService));
        _databaseServiceResolver = databaseServiceResolver ?? throw new ArgumentNullException(nameof(databaseServiceResolver));
    }

    public async IAsyncEnumerable<SqlWordListItem> GetWordsListAsync(
        SqlWordListRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.ConnectionName))
            yield break;

        IDatabaseService? databaseService = await Task.Run(
            () => _databaseServiceResolver(request.ConnectionName),
            cancellationToken).ConfigureAwait(false);
        if (databaseService is null)
            yield break;

        foreach (var item in _autocompleteService.GetWordsList(
                     request.Fragment,
                     ToMutable(request.AliasDbTable),
                     ToMutable(request.SubqueryHints),
                     ToMutable(request.WithHints),
                     ToMutable(request.TempTableHints),
                     databaseService,
                     request.DatabaseName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ToNeutral(item);
        }
    }

    internal static Dictionary<string, List<string>> ToMutable(
        IReadOnlyDictionary<string, IReadOnlyList<string>> source)
        => source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a host UI completion item onto the neutral contract. Glyph-less
    /// items (e.g. subquery/WITH/temp-table column hints rendered with
    /// <see cref="Glyph.None"/>) are classified as columns when their description
    /// mentions a column, otherwise as keywords.
    /// </summary>
    public static SqlWordListItem ToNeutral(CompletionDataSql item)
    {
        SqlWordListKind kind = item.Glyph switch
        {
            Glyph.Table => SqlWordListKind.Table,
            Glyph.Column => SqlWordListKind.Column,
            Glyph.View => SqlWordListKind.View,
            Glyph.Database => SqlWordListKind.Database,
            Glyph.Schema => SqlWordListKind.Schema,
            Glyph.Procedure => SqlWordListKind.Procedure,
            Glyph.Synonym => SqlWordListKind.Synonym,
            Glyph.ExternalTable => SqlWordListKind.ExternalTable,
            Glyph.Function => SqlWordListKind.Function,
            Glyph.SubQuery => SqlWordListKind.Subquery,
            Glyph.WithDb => SqlWordListKind.With,
            Glyph.TempTable => SqlWordListKind.TempTable,
            Glyph.Snippet => SqlWordListKind.Snippet,
            Glyph.None => LooksLikeColumn(item) ? SqlWordListKind.Column : SqlWordListKind.Keyword,
            _ => SqlWordListKind.Keyword
        };

        return new SqlWordListItem(
            item.Text,
            kind,
            item.DetailText,
            item.DescriptionText ?? item.Description?.ToString());
    }

    private static bool LooksLikeColumn(CompletionDataSql item)
    {
        string? hint = item.DescriptionText ?? item.Description?.ToString();
        return !string.IsNullOrWhiteSpace(hint)
               && hint.Contains("column", StringComparison.OrdinalIgnoreCase);
    }
}

using System.Text;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class ContentSearchServiceTests
{
    [Fact]
    public async Task Search_returns_line_context_and_respects_sql_comments_and_filters()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "one.sql"), "-- id in comment\r\nselect id, id from table;\r\n");
            File.WriteAllText(Path.Combine(root, "ignore.sql"), "select id;");
            var service = new ContentSearchService();
            var run = await service.SearchAsync([root],
                new ContentSearchOptions("id", SearchSqlComments: false, Include: "*.sql", Exclude: "ignore.*"),
                CancellationToken.None);

            var file = Assert.Single(run.Files);
            Assert.Equal(2, file.Hits.Count);
            Assert.All(file.Hits, hit => Assert.Equal(2, hit.Line));
            Assert.Equal(8, file.Hits[0].Column);
            Assert.Equal("select ", file.Hits[0].Before);
            Assert.Equal("id", file.Hits[0].Match);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Preview_and_apply_preserve_bom_and_newlines_and_reject_stale_file()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "one.sql");
            File.WriteAllText(path, "select id\r\n", new UTF8Encoding(true));
            var service = new ContentSearchService();
            var options = new ContentSearchOptions("id");
            var file = Assert.Single((await service.SearchAsync([root], options, CancellationToken.None)).Files);
            var replacement = service.Preview(file, options, "customer_id");
            Assert.NotNull(replacement);
            Assert.Equal(1, replacement.Count);
            service.Apply(replacement);
            Assert.Equal("select customer_id\r\n", File.ReadAllText(path));
            Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Throws<IOException>(() => service.Apply(replacement));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Regex_replacement_uses_capture_groups_and_can_target_one_match()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "one.sql"), "a1 a2");
            var service = new ContentSearchService();
            var options = new ContentSearchOptions("a(\\d)", UseRegex: true);
            var file = Assert.Single((await service.SearchAsync([root], options, CancellationToken.None)).Files);
            var preview = service.Preview(file, options, "b$1", [file.Hits[1].Offset]);
            Assert.NotNull(preview);
            Assert.Equal("a1 b2", preview.NewText);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Include_glob_with_double_star_matches_root_and_nested_files()
    {
        var root = NewRoot();
        try
        {
            var folder = Path.Combine(root, "src");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "first.cs"), "needle");
            File.WriteAllText(Path.Combine(folder, "nested", "second.cs"), "needle");
            var service = new ContentSearchService();
            var run = await service.SearchAsync([root],
                new ContentSearchOptions("needle", Include: "src/**/*.cs"), CancellationToken.None);
            Assert.Equal(2, run.Files.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Anchored_regex_replaces_match_on_second_line()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "two.sql"), "first\nsecond");
            var service = new ContentSearchService();
            var options = new ContentSearchOptions("^second$", UseRegex: true);
            var file = Assert.Single((await service.SearchAsync([root], options, CancellationToken.None)).Files);
            var preview = service.Preview(file, options, "changed");
            Assert.NotNull(preview);
            Assert.Equal("first\nchanged", preview.NewText);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Missing_root_is_reported_as_skipped()
    {
        var path = Path.Combine(Path.GetTempPath(), "jb-missing-" + Guid.NewGuid().ToString("N"));
        var run = await new ContentSearchService().SearchAsync([path],
            new ContentSearchOptions("needle"), CancellationToken.None);
        Assert.Empty(run.Files);
        Assert.Single(run.Skipped);
        Assert.Contains("directory not found", run.Skipped[0]);
    }

    [Fact]
    public async Task Comment_search_keeps_double_quoted_identifiers_intact()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "quoted.sql"), "select \"id--column\" from t; -- id comment");
            var run = await new ContentSearchService().SearchAsync([root],
                new ContentSearchOptions("id", SearchSqlComments: false), CancellationToken.None);
            var file = Assert.Single(run.Files);
            Assert.Single(file.Hits);
            Assert.Equal("id", file.Hits[0].Match);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "jb-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

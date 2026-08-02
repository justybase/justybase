using JustyBase.QuickOpen;

namespace JustyBase.Tests;

public sealed class QuickOpenSearchServiceTests
{
    [Fact]
    public async Task Collect_and_search_covers_files_git_open_and_content()
    {
        string root = Path.Combine(Path.GetTempPath(), "qo-" + Guid.NewGuid().ToString("N"));
        string git = Path.Combine(root, "repo");
        try
        {
            Directory.CreateDirectory(Path.Combine(git, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "extra"));
            File.WriteAllText(Path.Combine(git, "dim_customer.sql"), "select * from dim_customer;\nwhere id = 1;");
            File.WriteAllText(Path.Combine(root, "extra", "fact_sales.sql"), "select dim from fact;");
            File.WriteAllText(Path.Combine(root, "extra", "readme.txt"), "not sql");

            string[] known = Directory.GetFiles(root, "*.sql", SearchOption.AllDirectories);
            var svc = new QuickOpenSearchService();
            string openId = Guid.NewGuid().ToString("N");

            var candidates = svc.CollectCandidates(
                [root],
                known,
                git,
                [(openId, "untitled.sql", null, "create table dim_open (id int);")]);

            Assert.Contains(candidates, c => c.DisplayName.Equals("dim_customer.sql", StringComparison.OrdinalIgnoreCase)
                && c.Sources.HasFlag(QuickOpenSource.Git)
                && c.Sources.HasFlag(QuickOpenSource.Files));
            Assert.Contains(candidates, c => c.DisplayName.Equals("fact_sales.sql", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(candidates, c => c.DocumentId == openId && c.Sources.HasFlag(QuickOpenSource.Open));
            Assert.DoesNotContain(candidates, c => c.DisplayName.Equals("readme.txt", StringComparison.OrdinalIgnoreCase));

            var paths = candidates.Where(c => c.FilePath is not null).Select(c => c.FilePath!).ToArray();
            Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var names = svc.SearchByName(candidates, "dim");
            Assert.NotEmpty(names);
            Assert.Contains(names, h => h.DisplayName.Contains("dim", StringComparison.OrdinalIgnoreCase)
                || h.DisplayPath.Contains("dim", StringComparison.OrdinalIgnoreCase)
                || h.DisplayName.Equals("untitled.sql", StringComparison.OrdinalIgnoreCase));

            var content = await svc.SearchByContentAsync(candidates, "dim", TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotEmpty(content);
            Assert.Contains(content, h => h.LineNumber is > 0 && !string.IsNullOrWhiteSpace(h.Snippet));

            var list = QuickOpenSearchService.BuildList(names, content);
            Assert.Contains(list, e => e.IsHeader && e.HeaderText == "files");
            Assert.Contains(list, e => e.IsHeader && e.HeaderText == "in files");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

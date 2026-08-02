namespace JustyBase.QuickOpen;

public sealed class QuickOpenSearchService
{
    private const int MaxNameHits = 50;
    private const int MaxContentHits = 50;
    private const int MaxMatchesPerFile = 3;

    public IReadOnlyList<QuickOpenCandidate> CollectCandidates(
        IReadOnlyList<string> filesRootPaths,
        IReadOnlyList<string> knownFiles,
        string? gitRepoPath,
        IEnumerable<(string Id, string Title, string? FilePath, string Text)> openDocuments)
    {
        var byKey = new Dictionary<string, QuickOpenCandidate>(StringComparer.OrdinalIgnoreCase);
        var roots = NormalizeRoots(filesRootPaths);

        void Upsert(string? filePath, string? documentId, string displayName, string? inMemoryText, QuickOpenSource source)
        {
            string key = !string.IsNullOrWhiteSpace(filePath)
                ? "p:" + NormalizePath(filePath!)
                : "d:" + documentId!;

            if (byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = existing with
                {
                    Sources = existing.Sources | source,
                    DocumentId = existing.DocumentId ?? documentId,
                    InMemoryText = existing.InMemoryText ?? inMemoryText,
                    DisplayName = string.IsNullOrWhiteSpace(existing.DisplayName) ? displayName : existing.DisplayName,
                };
                return;
            }

            string displayPath = BuildDisplayPath(filePath, displayName, roots, gitRepoPath, source);
            byKey[key] = new QuickOpenCandidate(
                displayName,
                string.IsNullOrWhiteSpace(filePath) ? null : NormalizePath(filePath!),
                documentId,
                inMemoryText,
                source,
                displayPath);
        }

        foreach (string path in knownFiles)
        {
            if (!IsSqlPath(path))
                continue;
            var source = QuickOpenSource.Files;
            if (!string.IsNullOrWhiteSpace(gitRepoPath) && IsUnderRoot(path, gitRepoPath))
                source |= QuickOpenSource.Git;
            Upsert(path, null, Path.GetFileName(path), null, source);
        }

        // Fall back to a filesystem walk only when the Files panel list is empty.
        if (knownFiles.Count == 0)
        {
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string path in EnumerateSqlFiles(root))
                    Upsert(path, null, Path.GetFileName(path), null, QuickOpenSource.Files);
            }
        }

        if (!string.IsNullOrWhiteSpace(gitRepoPath) && Directory.Exists(gitRepoPath))
        {
            bool anyUnderGit = byKey.Values.Any(c =>
                !string.IsNullOrWhiteSpace(c.FilePath) && IsUnderRoot(c.FilePath!, gitRepoPath));

            if (!anyUnderGit)
            {
                foreach (string path in EnumerateSqlFiles(gitRepoPath))
                    Upsert(path, null, Path.GetFileName(path), null, QuickOpenSource.Git);
            }
            else
            {
                foreach (var pair in byKey.ToArray())
                {
                    var candidate = pair.Value;
                    if (string.IsNullOrWhiteSpace(candidate.FilePath))
                        continue;
                    if (!IsUnderRoot(candidate.FilePath!, gitRepoPath))
                        continue;
                    byKey[pair.Key] = candidate with { Sources = candidate.Sources | QuickOpenSource.Git };
                }
            }
        }

        foreach (var doc in openDocuments)
        {
            bool hasPath = !string.IsNullOrWhiteSpace(doc.FilePath);
            if (hasPath && !IsSqlPath(doc.FilePath!))
                continue;

            Upsert(
                hasPath ? doc.FilePath : null,
                doc.Id,
                string.IsNullOrWhiteSpace(doc.Title) ? "Untitled" : doc.Title.TrimEnd('*'),
                doc.Text,
                QuickOpenSource.Open);
        }

        return byKey.Values
            .OrderByDescending(c => (c.Sources & QuickOpenSource.Open) != 0)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<IReadOnlyList<QuickOpenCandidate>> CollectCandidatesAsync(
        IReadOnlyList<string> filesRootPaths,
        IReadOnlyList<string> knownFiles,
        string? gitRepoPath,
        IEnumerable<(string Id, string Title, string? FilePath, string Text)> openDocuments,
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CollectCandidates(filesRootPaths, knownFiles, gitRepoPath, openDocuments);
        }, cancellationToken);

    public IReadOnlyList<QuickOpenHit> SearchByName(IReadOnlyList<QuickOpenCandidate> candidates, string query)
    {
        string q = query?.Trim() ?? string.Empty;
        if (q.Length == 0)
        {
            return candidates
                .OrderByDescending(c => (c.Sources & QuickOpenSource.Open) != 0)
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxNameHits)
                .Select(c => ToNameHit(c, q, ScoreEmpty(c)))
                .ToArray();
        }

        var scored = new List<QuickOpenHit>(Math.Min(candidates.Count, MaxNameHits));
        foreach (var candidate in candidates)
        {
            int score = ScoreName(candidate, q);
            if (score < 0)
                continue;
            scored.Add(ToNameHit(candidate, q, score));
        }

        return scored
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxNameHits)
            .ToArray();
    }

    public async Task<IReadOnlyList<QuickOpenHit>> SearchByContentAsync(
        IReadOnlyList<QuickOpenCandidate> candidates,
        string query,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string q = query?.Trim() ?? string.Empty;
        if (q.Length == 0)
            return [];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
            timeoutCts.CancelAfter(timeout);

        var hits = new List<QuickOpenHit>();

        // In-memory open documents (including untitled / dirty buffers).
        foreach (var candidate in candidates)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            if (hits.Count >= MaxContentHits)
                break;
            if (string.IsNullOrEmpty(candidate.InMemoryText))
                continue;

            int matchesInFile = 0;
            foreach (var match in FindInText(candidate.InMemoryText, q))
            {
                if (hits.Count >= MaxContentHits || matchesInFile >= MaxMatchesPerFile)
                    break;

                hits.Add(new QuickOpenHit(
                    QuickOpenHitKind.Content,
                    candidate.DisplayName,
                    candidate.DisplayPath,
                    candidate.FilePath,
                    candidate.DocumentId,
                    candidate.Sources,
                    Score: 0,
                    Query: q,
                    LineNumber: match.LineNumber,
                    MatchIndex: match.MatchIndex,
                    MatchLength: match.MatchLength,
                    Snippet: TrimSnippet(match.LineText, match.MatchIndex, match.MatchLength)));
                matchesInFile++;
            }
        }

        if (hits.Count >= MaxContentHits)
            return OrderContentHits(hits);

        var diskPaths = candidates
            .Select(c => c.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Skip paths already covered by in-memory open docs to avoid duplicate noise.
        var openPaths = new HashSet<string>(
            candidates
                .Where(c => (c.Sources & QuickOpenSource.Open) != 0 && !string.IsNullOrWhiteSpace(c.FilePath))
                .Select(c => NormalizePath(c.FilePath!)),
            StringComparer.OrdinalIgnoreCase);

        var pathsToSearch = diskPaths
            .Where(p => !openPaths.Contains(NormalizePath(p!)))
            .Cast<string>()
            .ToArray();

        if (pathsToSearch.Length == 0)
            return OrderContentHits(hits);

        var byPath = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.FilePath))
            .GroupBy(c => NormalizePath(c.FilePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            foreach (string path in pathsToSearch)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                if (hits.Count >= MaxContentHits)
                    break;

                if (!byPath.TryGetValue(NormalizePath(path), out var candidate))
                {
                    candidate = new QuickOpenCandidate(
                        Path.GetFileName(path),
                        NormalizePath(path),
                        null,
                        null,
                        QuickOpenSource.Files,
                        path);
                }

                try
                {
                    int matchesInFile = 0;
                    using var reader = new StreamReader(path);
                    string? line;
                    int lineNumber = 0;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        timeoutCts.Token.ThrowIfCancellationRequested();
                        lineNumber++;
                        int index = line.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                        if (index < 0)
                            continue;

                        hits.Add(new QuickOpenHit(
                            QuickOpenHitKind.Content,
                            candidate.DisplayName,
                            candidate.DisplayPath,
                            candidate.FilePath,
                            candidate.DocumentId,
                            candidate.Sources,
                            Score: 0,
                            Query: q,
                            LineNumber: lineNumber,
                            MatchIndex: index,
                            MatchLength: q.Length,
                            Snippet: TrimSnippet(line, index, q.Length)));

                        matchesInFile++;
                        if (hits.Count >= MaxContentHits || matchesInFile >= MaxMatchesPerFile)
                            break;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip unreadable files.
                }
            }
        }, timeoutCts.Token).ConfigureAwait(false);

        return OrderContentHits(hits);
    }

    public static IReadOnlyList<QuickOpenListEntry> BuildList(
        IReadOnlyList<QuickOpenHit> nameHits,
        IReadOnlyList<QuickOpenHit> contentHits)
    {
        var list = new List<QuickOpenListEntry>(nameHits.Count + contentHits.Count + 2);
        if (nameHits.Count > 0)
        {
            list.Add(new QuickOpenListEntry(true, "files", null));
            foreach (var hit in nameHits)
                list.Add(new QuickOpenListEntry(false, null, hit));
        }

        if (contentHits.Count > 0)
        {
            list.Add(new QuickOpenListEntry(true, "in files", null));
            foreach (var hit in contentHits)
                list.Add(new QuickOpenListEntry(false, null, hit));
        }

        return list;
    }

    private static IReadOnlyList<QuickOpenHit> OrderContentHits(IEnumerable<QuickOpenHit> hits)
        => hits
            .OrderBy(h => h.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.LineNumber ?? 0)
            .Take(MaxContentHits)
            .ToArray();

    private static QuickOpenHit ToNameHit(QuickOpenCandidate candidate, string query, int score)
        => new(
            QuickOpenHitKind.FileName,
            candidate.DisplayName,
            candidate.DisplayPath,
            candidate.FilePath,
            candidate.DocumentId,
            candidate.Sources,
            score,
            query);

    private static int ScoreEmpty(QuickOpenCandidate candidate)
        => (candidate.Sources & QuickOpenSource.Open) != 0 ? 1_000 : 0;

    private static int ScoreName(QuickOpenCandidate candidate, string query)
    {
        string name = candidate.DisplayName;
        string path = candidate.FilePath ?? candidate.DisplayPath ?? string.Empty;

        int nameIndex = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        int pathIndex = path.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (nameIndex < 0 && pathIndex < 0)
            return -1;

        int score = 0;
        if ((candidate.Sources & QuickOpenSource.Open) != 0)
            score += 500;

        if (nameIndex >= 0)
        {
            score += 200;
            if (nameIndex == 0)
                score += 100;
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(name), query, StringComparison.OrdinalIgnoreCase))
                score += 150;
            score -= nameIndex;
        }
        else if (pathIndex >= 0)
        {
            score += 50;
            score -= Math.Min(pathIndex, 80);
        }

        return score;
    }

    private static IEnumerable<(int LineNumber, string LineText, int MatchIndex, int MatchLength)> FindInText(
        string text,
        string query)
    {
        using var reader = new StringReader(text);
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            int index = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            yield return (lineNumber, line, index, query.Length);
        }
    }

    private static string TrimSnippet(string line, int matchIndex, int matchLength)
    {
        const int max = 120;
        string trimmed = line.Trim();
        if (trimmed.Length <= max)
            return trimmed;

        int start = Math.Max(0, matchIndex - 40);
        if (start > 0 && start + max < line.Length)
            return "…" + line.Substring(start, max).Trim() + "…";
        return trimmed[..max] + "…";
    }

    private static IEnumerable<string> EnumerateSqlFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string sub in subDirs)
            {
                string name = Path.GetFileName(sub);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Push(sub);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.sql");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
                yield return file;
        }
    }

    private static IReadOnlyList<string> NormalizeRoots(IReadOnlyList<string> roots)
        => roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildDisplayPath(
        string? filePath,
        string displayName,
        IReadOnlyList<string> roots,
        string? gitRepoPath,
        QuickOpenSource source)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "open editors";

        string full = NormalizePath(filePath);
        string? relative = null;

        if (!string.IsNullOrWhiteSpace(gitRepoPath))
        {
            string git = NormalizePath(gitRepoPath);
            if (full.StartsWith(git + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, git, StringComparison.OrdinalIgnoreCase))
            {
                relative = Path.GetRelativePath(git, full);
            }
        }

        if (relative is null)
        {
            foreach (string root in roots)
            {
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                {
                    relative = Path.GetRelativePath(root, full);
                    break;
                }
            }
        }

        relative ??= full;
        string dir = Path.GetDirectoryName(relative) ?? string.Empty;
        if (string.IsNullOrEmpty(dir) || dir == ".")
            return SourceLabel(source);

        return dir.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string SourceLabel(QuickOpenSource source)
    {
        if ((source & QuickOpenSource.Open) != 0)
            return "open editors";
        if ((source & QuickOpenSource.Git) != 0)
            return "git";
        return "files";
    }

    private static bool IsUnderRoot(string path, string root)
    {
        string full = NormalizePath(path);
        string rootFull = NormalizePath(root);
        return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqlPath(string path)
        => path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

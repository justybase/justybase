namespace JustyBase.Services.Git;

/// <summary>Placeholder when embedded FIM is not compiled into this build.</summary>
public sealed class UnavailableGitCommitMessageAiService : IGitCommitMessageAiService
{
    public bool IsAvailable => false;

    public Task<string?> GenerateAsync(string changeContext, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

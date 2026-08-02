namespace JustyBase.Services;

public interface IGitDiffPresentationService
{
    void ShowGitDiff(string title, string oldText, string newText);
}

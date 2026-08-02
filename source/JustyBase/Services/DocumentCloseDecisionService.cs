namespace JustyBase.Services;

public sealed class DocumentCloseDecisionService : IDocumentCloseDecisionService
{
    public bool ShouldConfirmClose(bool confirmDocumentClosing, string? title, bool skipCloseQuestion, bool isLastDocument)
    {
        if (skipCloseQuestion)
        {
            return false;
        }

        if (isLastDocument)
        {
            return true;
        }

        return confirmDocumentClosing && title?.EndsWith('*') == true;
    }
}

namespace JustyBase.Services;

public interface IDocumentCloseDecisionService
{
    bool ShouldConfirmClose(bool confirmDocumentClosing, string? title, bool skipCloseQuestion, bool isLastDocument);
}

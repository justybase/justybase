using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class DocumentCloseDecisionServiceTests
{
    [Fact]
    public void ShouldConfirmClose_ReturnsFalse_WhenSkipCloseQuestionIsTrue()
    {
        var service = new DocumentCloseDecisionService();

        var result = service.ShouldConfirmClose(
            confirmDocumentClosing: true,
            title: "query*",
            skipCloseQuestion: true,
            isLastDocument: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldConfirmClose_ReturnsTrue_WhenIsLastDocumentAndNotSkipped()
    {
        var service = new DocumentCloseDecisionService();

        var result = service.ShouldConfirmClose(
            confirmDocumentClosing: false,
            title: "query",
            skipCloseQuestion: false,
            isLastDocument: true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldConfirmClose_ReturnsTrue_WhenConfirmCloseEnabledAndTitleMarkedAsEdited()
    {
        var service = new DocumentCloseDecisionService();

        var result = service.ShouldConfirmClose(
            confirmDocumentClosing: true,
            title: "query*",
            skipCloseQuestion: false,
            isLastDocument: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldConfirmClose_ReturnsFalse_WhenConfirmCloseDisabledAndNotLastDocument()
    {
        var service = new DocumentCloseDecisionService();

        var result = service.ShouldConfirmClose(
            confirmDocumentClosing: false,
            title: "query*",
            skipCloseQuestion: false,
            isLastDocument: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldConfirmClose_ReturnsFalse_WhenTitleIsNotMarkedAsEdited()
    {
        var service = new DocumentCloseDecisionService();

        var result = service.ShouldConfirmClose(
            confirmDocumentClosing: true,
            title: "query",
            skipCloseQuestion: false,
            isLastDocument: false);

        Assert.False(result);
    }
}

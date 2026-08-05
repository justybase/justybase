using JustyBase.Common;
using JustyBase.Common.Models;
using JustyBase.Services;
using JustyBase.Services.Chat;
using System.Text.Json;

namespace JustyBase.Tests;

public sealed class CodexChatIntegrationTests
{
    [Fact]
    public void AppOptions_DefaultsToProviderNeutralCodexModel()
    {
        var options = new AppOptions();

        Assert.Equal("gpt-5.6-luna", options.AiChatDefaultModel);
        Assert.Equal("low", options.AiChatDefaultReasoningEffort);
    }

    [Fact]
    public void ChatSession_PersistsCodexThreadId()
    {
        var session = new ChatSession { CodexThreadId = "thread-123" };
        var json = JsonSerializer.Serialize(session, MyJsonContextAppOptions.Default.ChatSession);
        var restored = JsonSerializer.Deserialize(json, MyJsonContextAppOptions.Default.ChatSession);

        Assert.Equal("thread-123", restored?.CodexThreadId);
    }

    [Fact]
    public void CodexAccountInfo_UsesNonSecretAccountFieldsOnly()
    {
        using var document = JsonDocument.Parse("""
        { "account": { "email": "user@example.com", "planType": "plus", "type": "chatgpt", "accessToken": "must-not-be-used" } }
        """);

        var account = CodexAccountInfo.FromJson(document.RootElement);

        Assert.NotNull(account);
        Assert.True(account!.IsAuthenticated);
        Assert.Equal("user@example.com", account.Email);
        Assert.Equal("plus", account.Plan);
    }

    [Fact]
    public void CodexAccountInfo_LoginDescriptorIsNotAuthenticatedYet()
    {
        using var document = JsonDocument.Parse("""
        { "type": "chatgpt", "loginId": "login-123", "authUrl": "https://chatgpt.com/auth" }
        """);

        var account = CodexAccountInfo.FromJson(document.RootElement);

        Assert.NotNull(account);
        Assert.False(account!.IsAuthenticated);
    }

    [Fact]
    public void CodexReasoningEffortParser_ReadsCurrentObjectShape()
    {
        using var document = JsonDocument.Parse("""
        { "reasoningEffort": "medium", "description": "Balanced reasoning" }
        """);

        var effort = CodexAppServerClient.ReadReasoningEffortValue(document.RootElement);

        Assert.Equal("medium", effort);
    }

    [Fact]
    public void CodexToolSchemas_RespectChatModeBoundaries()
    {
        var simple = CodexToolSchemas.Create(ChatMode.Simple).Select(tool => tool.Name).ToHashSet();
        var sqlFix = CodexToolSchemas.Create(ChatMode.SqlFix).Select(tool => tool.Name).ToHashSet();
        var expert = CodexToolSchemas.Create(ChatMode.Expert).Select(tool => tool.Name).ToHashSet();

        Assert.Empty(simple);
        Assert.Contains("get_diagnostics", sqlFix);
        Assert.Contains("apply_sql_document_change", sqlFix);
        Assert.DoesNotContain("execute_sql", sqlFix);
        Assert.Contains("execute_sql", expert);
        Assert.Contains("get_object_definition", expert);
    }

    [Fact]
    public void CodexToolOperationKey_RecognizesTheSameSqlChangeWithDifferentFormatting()
    {
        var first = CodexAppServerClient.BuildToolOperationKey(
            "apply_sql_document_change",
            "{\"proposedSql\":\"select 1;\\r\\n\"}");
        var repeated = CodexAppServerClient.BuildToolOperationKey(
            "apply_sql_document_change",
            "{\"proposedSql\":\"select 1;\\n\"}");

        Assert.Equal(first, repeated);
    }

    [Fact]
    public void SqlExecutionErrorStore_IsolatedFromGeneralApplicationErrors()
    {
        var store = new SqlExecutionErrorStore();

        store.Record("syntax error", "query.sql", "warehouse", "analytics");
        var error = store.LastError;

        Assert.NotNull(error);
        Assert.Equal("syntax error", error!.Message);
        Assert.Equal("query.sql", error.DocumentTitle);
        Assert.Equal("warehouse", error.ConnectionName);
        Assert.Equal("analytics", error.DatabaseName);

        store.Clear();

        Assert.Null(store.LastError);
    }
}

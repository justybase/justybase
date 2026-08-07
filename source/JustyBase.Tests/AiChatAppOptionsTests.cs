using JustyBase.Ai.Models;
using JustyBase.Common;
using JustyBase.Common.Models;
using System.Text.Json;

namespace JustyBase.Tests;

/// <summary>AppOptions persistence contract for AI chat settings.</summary>
public sealed class AiChatAppOptionsTests
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
}

namespace JustyBase.Services.Ai;

/// <summary>
/// F1: Thin navigation facade over <c>AiChatViewModel</c>.
/// Lets document/diagnostic VMs request "send to AI chat" via DI
/// instead of <c>Program.ServiceProvider.GetService&lt;AiChatViewModel&gt;</c>.
/// </summary>
public interface IAiChatNavigator
{
    Task SendToAiChatAsync();
    bool IsAiChatEnabled { get; }
}

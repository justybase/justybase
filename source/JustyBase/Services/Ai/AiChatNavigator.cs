using JustyBase.Common.Contracts;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services.Ai;

/// <summary>
/// F1: DI-owned adapter forwarding to the singleton <see cref="AiChatViewModel"/>.
/// Registered as singleton; the target VM is also a singleton so no cycle occurs
/// (AiChatViewModel never depends on document VMs).
/// </summary>
public sealed class AiChatNavigator : IAiChatNavigator
{
    private readonly AiChatViewModel _aiChat;
    private readonly IGeneralApplicationData _generalApplicationData;

    public AiChatNavigator(AiChatViewModel aiChat, IGeneralApplicationData generalApplicationData)
    {
        _aiChat = aiChat;
        _generalApplicationData = generalApplicationData;
    }

    public bool IsAiChatEnabled => _generalApplicationData.Config.EnableAiChat;

    public Task SendToAiChatAsync() => _aiChat.SendToAiChatAsync();
}

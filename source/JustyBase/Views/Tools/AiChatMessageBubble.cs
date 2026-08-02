using Avalonia;
using Avalonia.Controls;

namespace JustyBase.Views.Tools;

/// <summary>
/// Message bubble that exposes the role as a style class so its palette can
/// come from the active light/dark resource dictionary.
/// </summary>
public sealed class AiChatMessageBubble : Border
{
    public static readonly StyledProperty<string?> RoleProperty =
        AvaloniaProperty.Register<AiChatMessageBubble, string?>(nameof(Role));

    public string? Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RoleProperty)
        {
            var isUser = string.Equals(change.GetNewValue<string?>(), "user", StringComparison.OrdinalIgnoreCase);
            Classes.Set("ai-user", isUser);
            Classes.Set("ai-assistant", !isUser);
        }
    }
}

using Avalonia.Controls.Notifications;

namespace JustyBase.Helpers.Interactions;

public interface INotificationManagerProvider
{
    void SetWindow(Window window);
    void Show(INotification notification);
}

public sealed class NotificationManagerProvider : INotificationManagerProvider
{
    private WindowNotificationManager? _manager;
    private Window? _window;

    public void SetWindow(Window window)
    {
        _window = window;

        _manager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.TopCenter,
            MaxItems = 5,
            IsEnabled = true
        };
    }

    public void Show(INotification notification)
    {
        _manager?.Show(notification);
    }
}


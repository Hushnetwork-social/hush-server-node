namespace Olimpo.Android.Notifications;

public interface INotificationService
{
    void ShowNotification(string title, string message, string? notificationId = null, Dictionary<string, string>? data = null);
    // Consider adding a method to trigger the permission request if not handled elsewhere:
    // void EnsureNotificationPermission(Action<bool> onResult);
}

using System.Reactive.Subjects;

namespace Olimpo.Android.Notifications;

public interface INotificationHandler
{
    Subject<string> NotificationRequestSteam { get; }

    // void RegisterHandler();
}

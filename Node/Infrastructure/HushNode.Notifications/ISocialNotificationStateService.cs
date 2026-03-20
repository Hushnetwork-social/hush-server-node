using HushNode.Notifications.Models;

namespace HushNode.Notifications;

public interface ISocialNotificationStateService
{
    Task StoreNotificationAsync(SocialNotificationItem item, CancellationToken cancellationToken = default);

    Task<SocialNotificationInboxResult> GetInboxAsync(
        string userId,
        int limit,
        bool includeRead,
        CancellationToken cancellationToken = default);

    Task<int> MarkAsReadAsync(
        string userId,
        string? notificationId,
        bool markAll,
        CancellationToken cancellationToken = default);

    Task<SocialNotificationPreferences> GetPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<SocialNotificationPreferences> UpdatePreferencesAsync(
        string userId,
        SocialNotificationPreferenceUpdate update,
        CancellationToken cancellationToken = default);
}

using System.Text.Json;
using HushNode.Notifications.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Notifications;

/// <summary>
/// Data-layer state service for FEAT-091 social notification contracts.
/// This phase provides the repository seam and deterministic default behaviors;
/// delivery rules and event production are added in later phases.
/// </summary>
public sealed class SocialNotificationStateService : ISocialNotificationStateService
{
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<SocialNotificationStateService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SocialNotificationStateService(
        RedisConnectionManager redis,
        ILogger<SocialNotificationStateService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public Task StoreNotificationAsync(SocialNotificationItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(item.RecipientUserId))
        {
            throw new ArgumentException("RecipientUserId is required", nameof(item));
        }

        if (string.IsNullOrWhiteSpace(item.NotificationId))
        {
            throw new ArgumentException("NotificationId is required", nameof(item));
        }

        return ExecuteRedisOperationAsync(async () =>
        {
            var list = await LoadInboxAsync(item.RecipientUserId);
            var existingIndex = list.FindIndex(x => x.NotificationId == item.NotificationId);
            if (existingIndex >= 0)
            {
                list[existingIndex] = CloneItem(item);
            }
            else
            {
                list.Add(CloneItem(item));
            }

            await SaveInboxAsync(item.RecipientUserId, list);
        });
    }

    public Task<SocialNotificationInboxResult> GetInboxAsync(
        string userId,
        int limit,
        bool includeRead,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteRedisOperationAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new SocialNotificationInboxResult();
            }

            var effectiveLimit = limit > 0 ? limit : 50;
            var snapshot = (await LoadInboxAsync(userId))
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.NotificationId, StringComparer.Ordinal)
                .Where(x => includeRead || !x.IsRead)
                .Select(CloneItem)
                .ToList();

            var items = snapshot.Take(effectiveLimit).ToList();
            return new SocialNotificationInboxResult
            {
                Items = items,
                HasMore = snapshot.Count > items.Count
            };
        });
    }

    public Task<int> MarkAsReadAsync(
        string userId,
        string? notificationId,
        bool markAll,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteRedisOperationAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return 0;
            }

            var list = await LoadInboxAsync(userId);
            var updated = 0;
            foreach (var item in list)
            {
                if (item.IsRead)
                {
                    continue;
                }

                if (markAll || string.Equals(item.NotificationId, notificationId, StringComparison.Ordinal))
                {
                    item.IsRead = true;
                    updated++;
                }
            }

            if (updated > 0)
            {
                await SaveInboxAsync(userId, list);
            }

            return updated;
        });
    }

    public Task<SocialNotificationPreferences> GetPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteRedisOperationAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return CreateDefaultPreferences();
            }

            var preferences = await LoadPreferencesAsync(userId);
            return ClonePreferences(preferences);
        });
    }

    public Task<SocialNotificationPreferences> UpdatePreferencesAsync(
        string userId,
        SocialNotificationPreferenceUpdate update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("UserId is required", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(update);

        return ExecuteRedisOperationAsync(async () =>
        {
            var current = await LoadPreferencesAsync(userId);
            var preferences = ApplyUpdate(current, update);
            await SavePreferencesAsync(userId, preferences);
            return ClonePreferences(preferences);
        });
    }

    private static SocialNotificationPreferences ApplyUpdate(
        SocialNotificationPreferences current,
        SocialNotificationPreferenceUpdate update)
    {
        var next = ClonePreferences(current);
        if (update.OpenActivityEnabled.HasValue)
        {
            next.OpenActivityEnabled = update.OpenActivityEnabled.Value;
        }

        if (update.CloseActivityEnabled.HasValue)
        {
            next.CloseActivityEnabled = update.CloseActivityEnabled.Value;
        }

        if (update.CircleMutes is not null)
        {
            next.CircleMutes = update.CircleMutes
                .Where(x => !string.IsNullOrWhiteSpace(x.CircleId))
                .Select(x => new SocialCircleMuteState
                {
                    CircleId = x.CircleId,
                    IsMuted = x.IsMuted
                })
                .DistinctBy(x => x.CircleId, StringComparer.Ordinal)
                .OrderBy(x => x.CircleId, StringComparer.Ordinal)
                .ToList();
        }

        next.UpdatedAtUtc = DateTime.UtcNow;
        return next;
    }

    private static SocialNotificationPreferences CreateDefaultPreferences()
    {
        return new SocialNotificationPreferences
        {
            OpenActivityEnabled = true,
            CloseActivityEnabled = true,
            CircleMutes = [],
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static SocialNotificationPreferences ClonePreferences(SocialNotificationPreferences preferences)
    {
        return new SocialNotificationPreferences
        {
            OpenActivityEnabled = preferences.OpenActivityEnabled,
            CloseActivityEnabled = preferences.CloseActivityEnabled,
            CircleMutes = preferences.CircleMutes
                .Select(x => new SocialCircleMuteState
                {
                    CircleId = x.CircleId,
                    IsMuted = x.IsMuted
                })
                .ToList(),
            UpdatedAtUtc = preferences.UpdatedAtUtc
        };
    }

    private static SocialNotificationItem CloneItem(SocialNotificationItem item)
    {
        return new SocialNotificationItem
        {
            NotificationId = item.NotificationId,
            RecipientUserId = item.RecipientUserId,
            Kind = item.Kind,
            VisibilityClass = item.VisibilityClass,
            TargetType = item.TargetType,
            TargetId = item.TargetId,
            PostId = item.PostId,
            ParentCommentId = item.ParentCommentId,
            ActorUserId = item.ActorUserId,
            ActorDisplayName = item.ActorDisplayName,
            Title = item.Title,
            Body = item.Body,
            IsRead = item.IsRead,
            IsPrivatePreviewSuppressed = item.IsPrivatePreviewSuppressed,
            CreatedAtUtc = item.CreatedAtUtc,
            DeepLinkPath = item.DeepLinkPath,
            MatchedCircleIds = item.MatchedCircleIds.ToList()
        };
    }

    private async Task<List<SocialNotificationItem>> LoadInboxAsync(string userId)
    {
        var key = _redis.GetSocialNotificationInboxKey(userId);
        var value = await _redis.Database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SocialNotificationItem>>(value!, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize social notification inbox for user {UserId}", userId);
            return [];
        }
    }

    private Task SaveInboxAsync(string userId, List<SocialNotificationItem> items)
    {
        var key = _redis.GetSocialNotificationInboxKey(userId);
        var json = JsonSerializer.Serialize(items, JsonOptions);
        return _redis.Database.StringSetAsync(key, json);
    }

    private async Task<SocialNotificationPreferences> LoadPreferencesAsync(string userId)
    {
        var key = _redis.GetSocialNotificationPreferencesKey(userId);
        var value = await _redis.Database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return CreateDefaultPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<SocialNotificationPreferences>(value!, JsonOptions) ?? CreateDefaultPreferences();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize social notification preferences for user {UserId}", userId);
            return CreateDefaultPreferences();
        }
    }

    private Task SavePreferencesAsync(string userId, SocialNotificationPreferences preferences)
    {
        var key = _redis.GetSocialNotificationPreferencesKey(userId);
        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        return _redis.Database.StringSetAsync(key, json);
    }

    private async Task ExecuteRedisOperationAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error while updating FEAT-091 social notification state");
        }
    }

    private async Task<T> ExecuteRedisOperationAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error while reading FEAT-091 social notification state");
            if (typeof(T) == typeof(SocialNotificationPreferences))
            {
                return (T)(object)CreateDefaultPreferences();
            }

            if (typeof(T) == typeof(SocialNotificationInboxResult))
            {
                return (T)(object)new SocialNotificationInboxResult();
            }

            if (typeof(T) == typeof(int))
            {
                return (T)(object)0;
            }

            throw;
        }
    }
}

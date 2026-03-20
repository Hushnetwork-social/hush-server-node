using FluentAssertions;
using HushNode.Notifications.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Notifications.Tests;

public sealed class SocialNotificationStateServiceTests
{
    [Fact]
    public async Task GetPreferencesAsync_ForNewUser_ReturnsDefaultEnabledPreferences()
    {
        var service = CreateService();

        var preferences = await service.GetPreferencesAsync("user-a");

        preferences.OpenActivityEnabled.Should().BeTrue();
        preferences.CloseActivityEnabled.Should().BeTrue();
        preferences.CircleMutes.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatePreferencesAsync_RoundTripsOpenCloseAndCircleMuteState()
    {
        var service = CreateService();

        var updated = await service.UpdatePreferencesAsync(
            "user-a",
            new SocialNotificationPreferenceUpdate
            {
                OpenActivityEnabled = false,
                CloseActivityEnabled = true,
                CircleMutes =
                [
                    new SocialCircleMuteState { CircleId = "circle-b", IsMuted = true },
                    new SocialCircleMuteState { CircleId = "circle-a", IsMuted = false },
                    new SocialCircleMuteState { CircleId = "circle-a", IsMuted = true }
                ]
            });

        updated.OpenActivityEnabled.Should().BeFalse();
        updated.CloseActivityEnabled.Should().BeTrue();
        updated.CircleMutes.Should().HaveCount(2);
        updated.CircleMutes.Select(x => x.CircleId).Should().ContainInOrder("circle-a", "circle-b");
        updated.CircleMutes.Single(x => x.CircleId == "circle-a").IsMuted.Should().BeFalse();
        updated.CircleMutes.Single(x => x.CircleId == "circle-b").IsMuted.Should().BeTrue();
    }

    [Fact]
    public async Task GetInboxAsync_ExcludesReadItems_WhenIncludeReadIsFalse()
    {
        var service = CreateService();

        await service.StoreNotificationAsync(new SocialNotificationItem
        {
            NotificationId = "n-1",
            RecipientUserId = "user-a",
            Kind = SocialNotificationKind.Comment,
            VisibilityClass = SocialNotificationVisibilityClass.Open,
            TargetType = SocialNotificationTargetType.Post,
            TargetId = "post-1",
            Title = "New comment",
            Body = "Alice commented",
            CreatedAtUtc = new DateTime(2026, 03, 20, 12, 00, 00, DateTimeKind.Utc),
            IsRead = true
        });
        await service.StoreNotificationAsync(new SocialNotificationItem
        {
            NotificationId = "n-2",
            RecipientUserId = "user-a",
            Kind = SocialNotificationKind.Reply,
            VisibilityClass = SocialNotificationVisibilityClass.Close,
            TargetType = SocialNotificationTargetType.Comment,
            TargetId = "comment-2",
            Title = "New reply",
            Body = "Bob replied",
            IsPrivatePreviewSuppressed = true,
            CreatedAtUtc = new DateTime(2026, 03, 20, 12, 01, 00, DateTimeKind.Utc),
            IsRead = false
        });

        var inbox = await service.GetInboxAsync("user-a", 20, includeRead: false);

        inbox.HasMore.Should().BeFalse();
        inbox.Items.Should().ContainSingle();
        inbox.Items[0].NotificationId.Should().Be("n-2");
        inbox.Items[0].IsPrivatePreviewSuppressed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsReadAsync_MarkAll_UpdatesUnreadItemsOnly()
    {
        var service = CreateService();

        await service.StoreNotificationAsync(new SocialNotificationItem
        {
            NotificationId = "n-1",
            RecipientUserId = "user-a",
            Kind = SocialNotificationKind.NewPost,
            VisibilityClass = SocialNotificationVisibilityClass.Open,
            TargetType = SocialNotificationTargetType.Post,
            TargetId = "post-1",
            IsRead = false
        });
        await service.StoreNotificationAsync(new SocialNotificationItem
        {
            NotificationId = "n-2",
            RecipientUserId = "user-a",
            Kind = SocialNotificationKind.NewPost,
            VisibilityClass = SocialNotificationVisibilityClass.Open,
            TargetType = SocialNotificationTargetType.Post,
            TargetId = "post-2",
            IsRead = true
        });

        var updatedCount = await service.MarkAsReadAsync("user-a", null, markAll: true);
        var inbox = await service.GetInboxAsync("user-a", 20, includeRead: true);

        updatedCount.Should().Be(1);
        inbox.Items.Should().OnlyContain(x => x.IsRead);
    }

    private static SocialNotificationStateService CreateService()
    {
        var storage = new Dictionary<string, RedisValue>(StringComparer.Ordinal);
        var databaseMock = new Mock<IDatabase>();

        databaseMock
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) =>
                storage.TryGetValue(key!, out var value) ? value : RedisValue.Null);

        databaseMock
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? expiry, bool keepTtl, When when, CommandFlags flags) =>
            {
                storage[key!] = value;
                return true;
            });

        var redis = new TestRedisConnectionManager(databaseMock.Object);
        return new SocialNotificationStateService(redis, NullLogger<SocialNotificationStateService>.Instance);
    }

    private sealed class TestRedisConnectionManager : RedisConnectionManager
    {
        private readonly IDatabase _database;

        public TestRedisConnectionManager(IDatabase database)
            : base(
                Options.Create(new RedisSettings
                {
                    ConnectionString = "localhost",
                    InstanceName = "test:"
                }),
                NullLogger<RedisConnectionManager>.Instance)
        {
            _database = database;
        }

        public override IDatabase Database => _database;
        public override string KeyPrefix => "test:";
    }
}

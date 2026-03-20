using FluentAssertions;
using Grpc.Core;
using HushNode.Notifications.gRPC;
using HushNode.Notifications.Models;
using Moq;
using Moq.AutoMock;
using Xunit;
using ProtoTypes = HushNetwork.proto;

namespace HushNode.Notifications.Tests;

public sealed class NotificationGrpcServiceSocialContractsTests
{
    [Fact]
    public async Task GetSocialNotificationInbox_MapsStoredItemsToProtoResponse()
    {
        var mocker = new AutoMocker();
        var stateServiceMock = mocker.GetMock<ISocialNotificationStateService>();
        stateServiceMock
            .Setup(x => x.GetInboxAsync("user-a", 10, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationInboxResult
            {
                HasMore = true,
                Items =
                [
                    new SocialNotificationItem
                    {
                        NotificationId = "n-1",
                        RecipientUserId = "user-a",
                        Kind = SocialNotificationKind.Reaction,
                        VisibilityClass = SocialNotificationVisibilityClass.Close,
                        TargetType = SocialNotificationTargetType.Comment,
                        TargetId = "comment-1",
                        PostId = "post-1",
                        ParentCommentId = "comment-0",
                        ActorUserId = "actor-a",
                        ActorDisplayName = "Alice",
                        Title = "New reaction",
                        Body = "Someone reacted to your comment",
                        IsRead = false,
                        IsPrivatePreviewSuppressed = true,
                        CreatedAtUtc = new DateTime(2026, 03, 20, 12, 30, 00, DateTimeKind.Utc),
                        DeepLinkPath = "/social/notifications/n-1",
                        MatchedCircleIds = ["circle-a"]
                    }
                ]
            });

        var service = mocker.CreateInstance<NotificationGrpcService>();

        var response = await service.GetSocialNotificationInbox(
            new ProtoTypes.GetSocialNotificationInboxRequest
            {
                UserId = "user-a",
                Limit = 10,
                IncludeRead = true
            },
            TestServerCallContext.Create("user-a"));

        response.HasMore.Should().BeTrue();
        response.Items.Should().ContainSingle();
        response.Items[0].NotificationId.Should().Be("n-1");
        response.Items[0].VisibilityClass.Should().Be(ProtoTypes.SocialNotificationVisibilityClass.Close);
        response.Items[0].Kind.Should().Be(ProtoTypes.SocialNotificationKind.Reaction);
        response.Items[0].MatchedCircleIds.Should().ContainSingle("circle-a");
        response.Items[0].IsPrivatePreviewSuppressed.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSocialNotificationPreferences_ReturnsValidationFailure_WhenUserIdMissing()
    {
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();

        var response = await service.UpdateSocialNotificationPreferences(
            new ProtoTypes.UpdateSocialNotificationPreferencesRequest(),
            TestServerCallContext.Create());

        response.Success.Should().BeFalse();
        response.Message.Should().Be("UserId is required");
    }

    [Fact]
    public async Task UpdateSocialNotificationPreferences_MapsRequestToStateService()
    {
        var mocker = new AutoMocker();
        var stateServiceMock = mocker.GetMock<ISocialNotificationStateService>();
        stateServiceMock
            .Setup(x => x.UpdatePreferencesAsync(
                "user-a",
                It.IsAny<SocialNotificationPreferenceUpdate>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationPreferences
            {
                OpenActivityEnabled = false,
                CloseActivityEnabled = true,
                CircleMutes =
                [
                    new SocialCircleMuteState { CircleId = "circle-a", IsMuted = true }
                ],
                UpdatedAtUtc = new DateTime(2026, 03, 20, 12, 45, 00, DateTimeKind.Utc)
            });

        var service = mocker.CreateInstance<NotificationGrpcService>();

        var response = await service.UpdateSocialNotificationPreferences(
            new ProtoTypes.UpdateSocialNotificationPreferencesRequest
            {
                UserId = "user-a",
                HasOpenActivityEnabled = true,
                OpenActivityEnabled = false,
                HasCloseActivityEnabled = true,
                CloseActivityEnabled = true,
                CircleMutes =
                {
                    new ProtoTypes.SocialCircleMuteState
                    {
                        CircleId = "circle-a",
                        IsMuted = true
                    }
                }
            },
            TestServerCallContext.Create("user-a"));

        response.Success.Should().BeTrue();
        response.Preferences.OpenActivityEnabled.Should().BeFalse();
        response.Preferences.CircleMutes.Should().ContainSingle();

        stateServiceMock.Verify(x => x.UpdatePreferencesAsync(
            "user-a",
            It.Is<SocialNotificationPreferenceUpdate>(update =>
                update.OpenActivityEnabled == false &&
                update.CloseActivityEnabled == true &&
                update.CircleMutes != null &&
                update.CircleMutes.Count == 1 &&
                update.CircleMutes[0].CircleId == "circle-a" &&
                update.CircleMutes[0].IsMuted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSocialNotificationInbox_RejectsCrossUserAccess_WhenMetadataUserDiffers()
    {
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();

        var action = async () => await service.GetSocialNotificationInbox(
            new ProtoTypes.GetSocialNotificationInboxRequest
            {
                UserId = "user-a",
                Limit = 10,
                IncludeRead = true
            },
            TestServerCallContext.Create("user-b"));

        var exception = await Assert.ThrowsAsync<RpcException>(action);
        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task GetSocialNotificationPreferences_RejectsWhenAuthenticatedMetadataMissing()
    {
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();

        var action = async () => await service.GetSocialNotificationPreferences(
            new ProtoTypes.GetSocialNotificationPreferencesRequest
            {
                UserId = "user-a"
            },
            TestServerCallContext.Create());

        var exception = await Assert.ThrowsAsync<RpcException>(action);
        exception.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        public static TestServerCallContext Create(
            string? authenticatedUserId = null,
            CancellationToken cancellationToken = default)
        {
            return new TestServerCallContext(authenticatedUserId, cancellationToken);
        }

        private readonly Metadata _requestHeaders = new();

        private TestServerCallContext(string? authenticatedUserId, CancellationToken cancellationToken)
        {
            CancellationTokenCore = cancellationToken;
            if (!string.IsNullOrWhiteSpace(authenticatedUserId))
            {
                _requestHeaders.Add("x-hush-userid", authenticatedUserId);
            }
        }

        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore => _requestHeaders;
        protected override CancellationToken CancellationTokenCore { get; }
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}

using FluentAssertions;
using HushNode.Feeds.gRPC;
using HushNode.Notifications;
using Moq;
using Xunit;

namespace HushNode.Feeds.Tests;

public sealed class SocialPostNotificationServiceTests
{
    [Fact]
    public async Task NotifyPostCreatedAsync_DelegatesToSocialNotificationRouter()
    {
        var router = new Mock<ISocialNotificationRoutingService>(MockBehavior.Strict);
        var postId = Guid.NewGuid();

        router
            .Setup(x => x.RoutePostCreatedAsync(postId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new SocialPostNotificationService(router.Object);

        await sut.NotifyPostCreatedAsync(postId);

        router.VerifyAll();
    }
}

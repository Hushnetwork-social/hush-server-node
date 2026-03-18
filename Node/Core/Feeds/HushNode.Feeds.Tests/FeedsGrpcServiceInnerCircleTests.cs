using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;

namespace HushNode.Feeds.Tests;

public class FeedsGrpcServiceInnerCircleTests
{
    [Fact]
    public async Task GetInnerCircle_WhenExists_ShouldReturnFeedIdAndExistsTrue()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var innerCircle = new GroupFeed(
            feedId,
            "Inner Circle",
            "Auto-managed inner circle",
            false,
            new BlockIndex(10),
            0,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(innerCircle);

        var service = CreateService(mocker);
        var response = await service.GetInnerCircle(
            new GetInnerCircleRequest { OwnerPublicAddress = owner },
            CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.Exists.Should().BeTrue();
        response.FeedId.Should().Be(feedId.ToString());
    }

    [Fact]
    public async Task CreateInnerCircle_WhenAlreadyExists_ShouldReturnExistingFeedAndCode()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var existing = new GroupFeed(
            feedId,
            "Inner Circle",
            "Auto-managed inner circle",
            false,
            new BlockIndex(10),
            0,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(existing);

        var service = CreateService(mocker);
        var response = await service.CreateInnerCircle(
            new CreateInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = owner
            },
            CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.FeedId.Should().Be(feedId.ToString());
        response.ErrorCode.Should().Be("INNER_CIRCLE_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateInnerCircle_WhenRequesterDiffersFromOwner_ShouldReturnUnauthorized()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var requester = TestDataFactory.CreateAddress();
        var service = CreateService(mocker);

        var response = await service.CreateInnerCircle(
            new CreateInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = requester
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("INNER_CIRCLE_UNAUTHORIZED");
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WhenOnlyDuplicates_ShouldReturnFailureAndNotPersist()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var innerCircle = new GroupFeed(
            feedId,
            "Inner Circle",
            "Auto-managed inner circle",
            false,
            new BlockIndex(10),
            0,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(innerCircle);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync(new GroupFeedParticipantEntity(feedId, member, ParticipantType.Member, new BlockIndex(10)));

        var memberEncrypt = new EncryptKeys().PublicKey;

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(member))
            .ReturnsAsync(new Profile("member", "mb", member, memberEncrypt, true, new BlockIndex(1)));

        var service = CreateService(mocker);
        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = owner,
                Members =
                {
                    new InnerCircleMemberProto
                    {
                        PublicAddress = member,
                        PublicEncryptAddress = memberEncrypt
                    }
                }
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("INNER_CIRCLE_DUPLICATE_MEMBERS");
        response.DuplicateMembers.Should().Contain(member);

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                It.IsAny<FeedId>(),
                It.IsAny<IReadOnlyList<GroupFeedParticipantEntity>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()), Times.Never);
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WhenRequesterDiffersFromOwner_ShouldReturnUnauthorized()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var requester = TestDataFactory.CreateAddress();
        var service = CreateService(mocker);

        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = requester,
                Members =
                {
                    new InnerCircleMemberProto
                    {
                        PublicAddress = TestDataFactory.CreateAddress(),
                        PublicEncryptAddress = new EncryptKeys().PublicKey
                    }
                }
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("INNER_CIRCLE_UNAUTHORIZED");
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WhenMixedValidAndInvalid_ShouldFailAndNotPersist()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var validMember = TestDataFactory.CreateAddress();
        var invalidMember = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var ownerEncrypt = new EncryptKeys().PublicKey;
        var validEncrypt = new EncryptKeys().PublicKey;
        var wrongEncrypt = new EncryptKeys().PublicKey;

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(feedId, "Inner Circle", "", false, new BlockIndex(10), 0, IsInnerCircle: true, OwnerPublicAddress: owner));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, validMember))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, invalidMember))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("owner", "ow", owner, ownerEncrypt, true, new BlockIndex(1)));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(validMember))
            .ReturnsAsync(new Profile("valid", "vd", validMember, validEncrypt, true, new BlockIndex(1)));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(invalidMember))
            .ReturnsAsync(new Profile("invalid", "iv", invalidMember, validEncrypt, true, new BlockIndex(1)));

        var service = CreateService(mocker);
        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = owner,
                Members =
                {
                    new InnerCircleMemberProto { PublicAddress = validMember, PublicEncryptAddress = validEncrypt },
                    new InnerCircleMemberProto { PublicAddress = invalidMember, PublicEncryptAddress = wrongEncrypt }
                }
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("INNER_CIRCLE_INVALID_MEMBERS");
        response.InvalidMembers.Should().Contain(invalidMember);

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                It.IsAny<FeedId>(),
                It.IsAny<IReadOnlyList<GroupFeedParticipantEntity>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()), Times.Never);
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WithValidMember_ShouldPersistAndRotate()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var ownerEncrypt = new EncryptKeys().PublicKey;
        var memberEncrypt = new EncryptKeys().PublicKey;
        var feedId = TestDataFactory.CreateFeedId();

        var innerCircle = new GroupFeed(
            feedId,
            "Inner Circle",
            "Auto-managed inner circle",
            false,
            new BlockIndex(10),
            0,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(innerCircle);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { owner });

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                feedId,
                It.IsAny<IReadOnlyList<GroupFeedParticipantEntity>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(member))
            .ReturnsAsync(new Profile("member", "mb", member, memberEncrypt, true, new BlockIndex(1)));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("owner", "ow", owner, ownerEncrypt, true, new BlockIndex(1)));

        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.InvalidateKeyGenerationsAsync(feedId))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.AddParticipantAsync(feedId, member))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IGroupMembersCacheService>()
            .Setup(x => x.InvalidateGroupMembersAsync(feedId))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.AddFeedToUserCacheAsync(member, feedId))
            .Returns(Task.CompletedTask);

        var service = CreateService(mocker);
        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = owner,
                Members =
                {
                    new InnerCircleMemberProto
                    {
                        PublicAddress = member,
                        PublicEncryptAddress = memberEncrypt
                    }
                }
            },
            CreateMockServerCallContext());

        response.Success.Should().BeTrue();

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                feedId,
                It.Is<IReadOnlyList<GroupFeedParticipantEntity>>(list => list.Count == 1 && list[0].ParticipantPublicAddress == member),
                It.Is<IReadOnlyList<string>>(list => list.Count == 0),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()), Times.Once);

        VerifyLogContains(mocker.GetMock<ILogger<InnerCircleApplicationService>>(), LogLevel.Information, "inner_circle.add_members.requested", Times.Once());
        VerifyLogContains(mocker.GetMock<ILogger<InnerCircleApplicationService>>(), LogLevel.Information, "inner_circle.key_rotation.succeeded", Times.Once());
        VerifyLogContains(mocker.GetMock<ILogger<InnerCircleApplicationService>>(), LogLevel.Information, "inner_circle.add_members.succeeded", Times.Once());
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WhenValidationFails_ShouldEmitFailedTelemetry()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var memberEncrypt = new EncryptKeys().PublicKey;

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(feedId, "Inner Circle", "", false, new BlockIndex(10), 0, IsInnerCircle: true, OwnerPublicAddress: owner));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync(new GroupFeedParticipantEntity(feedId, member, ParticipantType.Member, new BlockIndex(10)));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(member))
            .ReturnsAsync(new Profile("member", "mb", member, memberEncrypt, true, new BlockIndex(1)));

        var service = CreateService(mocker);

        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
                RequesterPublicAddress = owner,
                Members =
                {
                    new InnerCircleMemberProto { PublicAddress = member, PublicEncryptAddress = memberEncrypt }
                }
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("INNER_CIRCLE_DUPLICATE_MEMBERS");

        VerifyLogContains(mocker.GetMock<ILogger<InnerCircleApplicationService>>(), LogLevel.Warning, "inner_circle.add_members.failed reason=validation", Times.Once());
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WhenUnhandledException_ShouldReturnSanitizedInternalError()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var appServiceMock = mocker.GetMock<IInnerCircleApplicationService>();
        appServiceMock
            .Setup(x => x.AddMembersToInnerCircleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<InnerCircleMemberProto>>()))
            .ThrowsAsync(new InvalidOperationException("sensitive internals"));

        var service = mocker.CreateInstance<FeedsGrpcService>();

        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = TestDataFactory.CreateAddress(),
                RequesterPublicAddress = TestDataFactory.CreateAddress(),
                Members = { new InnerCircleMemberProto { PublicAddress = TestDataFactory.CreateAddress(), PublicEncryptAddress = new EncryptKeys().PublicKey } }
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("INNER_CIRCLE_INTERNAL_ERROR");
        response.Message.Should().Be("Internal server error");
        response.Message.Should().NotContain("sensitive internals");
    }

    [Fact]
    public async Task FollowSocialAuthor_WhenRequesterDiffersFromViewer_ShouldReturnUnauthorized()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var viewer = TestDataFactory.CreateAddress();
        var author = TestDataFactory.CreateAddress();
        var requester = TestDataFactory.CreateAddress();
        var service = CreateService(mocker);

        var response = await service.FollowSocialAuthor(
            new FollowSocialAuthorRequest
            {
                ViewerPublicAddress = viewer,
                AuthorPublicAddress = author,
                RequesterPublicAddress = requester
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_FOLLOW_UNAUTHORIZED");
    }

    [Fact]
    public async Task FollowSocialAuthor_WhenViewerMatchesAuthor_ShouldRejectSelfFollow()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var viewer = TestDataFactory.CreateAddress();
        var service = CreateService(mocker);

        var response = await service.FollowSocialAuthor(
            new FollowSocialAuthorRequest
            {
                ViewerPublicAddress = viewer,
                AuthorPublicAddress = viewer,
                RequesterPublicAddress = viewer
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_FOLLOW_SELF_FOLLOW");
        response.RequiresSyncRefresh.Should().BeFalse();
    }

    [Fact]
    public async Task FollowSocialAuthor_WhenAlreadyFollowing_ShouldRejectAndRequireRefresh()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var viewer = TestDataFactory.CreateAddress();
        var author = TestDataFactory.CreateAddress();
        var innerCircleFeedId = TestDataFactory.CreateFeedId();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialFollowBootstrapStateAsync(viewer, author))
            .ReturnsAsync(new SocialFollowBootstrapState(true, true, true, innerCircleFeedId));

        var service = CreateService(mocker);
        var response = await service.FollowSocialAuthor(
            new FollowSocialAuthorRequest
            {
                ViewerPublicAddress = viewer,
                AuthorPublicAddress = author,
                RequesterPublicAddress = viewer
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_FOLLOW_ALREADY_FOLLOWING");
        response.AlreadyFollowing.Should().BeTrue();
        response.RequiresSyncRefresh.Should().BeTrue();
        response.InnerCircleFeedId.Should().Be(innerCircleFeedId.ToString());

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplySocialFollowBootstrapAsync(It.IsAny<SocialFollowBootstrapMutation>()), Times.Never);
    }

    [Fact]
    public async Task FollowSocialAuthor_WhenBootstrapIsValid_ShouldPersistAtomicallyAndRefreshCaches()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        var viewer = TestDataFactory.CreateAddress();
        var author = TestDataFactory.CreateAddress();
        var viewerEncrypt = new EncryptKeys().PublicKey;
        var authorEncrypt = new EncryptKeys().PublicKey;

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(25));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialFollowBootstrapStateAsync(viewer, author))
            .ReturnsAsync(new SocialFollowBootstrapState(false, false, false, null));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.ApplySocialFollowBootstrapAsync(It.IsAny<SocialFollowBootstrapMutation>()))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(viewer))
            .ReturnsAsync(new Profile("viewer-alias", "vw", viewer, viewerEncrypt, true, new BlockIndex(1)));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(author))
            .ReturnsAsync(new Profile("author-alias", "au", author, authorEncrypt, true, new BlockIndex(1)));

        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.InvalidateKeyGenerationsAsync(It.IsAny<FeedId>()))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.AddParticipantAsync(It.IsAny<FeedId>(), author))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IGroupMembersCacheService>()
            .Setup(x => x.InvalidateGroupMembersAsync(It.IsAny<FeedId>()))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.AddFeedToUserCacheAsync(It.IsAny<string>(), It.IsAny<FeedId>()))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IFeedMetadataCacheService>()
            .Setup(x => x.SetFeedMetadataAsync(It.IsAny<string>(), It.IsAny<FeedId>(), It.IsAny<FeedMetadataEntry>()))
            .Returns(Task.FromResult(true));

        var service = CreateService(mocker);
        var response = await service.FollowSocialAuthor(
            new FollowSocialAuthorRequest
            {
                ViewerPublicAddress = viewer,
                AuthorPublicAddress = author,
                RequesterPublicAddress = viewer
            },
            CreateMockServerCallContext());

        response.Success.Should().BeTrue($"{response.ErrorCode}:{response.Message}");
        response.ErrorCode.Should().Be("SOCIAL_FOLLOW_ACCEPTED");
        response.RequiresSyncRefresh.Should().BeTrue();
        response.InnerCircleFeedId.Should().NotBeNullOrWhiteSpace();

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplySocialFollowBootstrapAsync(
                It.Is<SocialFollowBootstrapMutation>(m =>
                    m.InnerCircleToCreate != null &&
                    m.DirectChatToCreate != null &&
                    m.InnerCircleParticipantsToAdd.Count == 0 &&
                    m.InnerCircleParticipantsToRejoin.Count == 0 &&
                    m.InnerCircleKeyGeneration == null)),
                Times.Once);
    }

    [Fact]
    public async Task FollowSocialAuthor_WhenUnhandledException_ShouldReturnSanitizedInternalError()
    {
        var mocker = new AutoMocker();
        SetupConfigurationMock(mocker);

        mocker.GetMock<IInnerCircleApplicationService>()
            .Setup(x => x.FollowSocialAuthorAsync(It.IsAny<FollowSocialAuthorRequest>()))
            .ThrowsAsync(new InvalidOperationException("sensitive follow internals"));

        var service = mocker.CreateInstance<FeedsGrpcService>();

        var response = await service.FollowSocialAuthor(
            new FollowSocialAuthorRequest
            {
                ViewerPublicAddress = TestDataFactory.CreateAddress(),
                AuthorPublicAddress = TestDataFactory.CreateAddress(),
                RequesterPublicAddress = TestDataFactory.CreateAddress()
            },
            CreateMockServerCallContext());

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_FOLLOW_INTERNAL_ERROR");
        response.Message.Should().Be("Internal server error");
        response.Message.Should().NotContain("sensitive follow internals");
    }

    private static void SetupConfigurationMock(AutoMocker mocker, int maxMessagesPerResponse = 100)
    {
        var mockConfigSection = new Mock<IConfigurationSection>();
        mockConfigSection.Setup(s => s.Value).Returns(maxMessagesPerResponse.ToString());

        var mockConfiguration = mocker.GetMock<IConfiguration>();
        mockConfiguration
            .Setup(c => c.GetSection("Feeds:MaxMessagesPerResponse"))
            .Returns(mockConfigSection.Object);
    }

    private static ServerCallContext CreateMockServerCallContext()
    {
        return new MockServerCallContext();
    }

    private static FeedsGrpcService CreateService(AutoMocker mocker)
    {
        var appService = mocker.CreateInstance<InnerCircleApplicationService>();
        mocker.Use<IInnerCircleApplicationService>(appService);
        return mocker.CreateInstance<FeedsGrpcService>();
    }

    private static void VerifyLogContains(Mock<ILogger<InnerCircleApplicationService>> loggerMock, LogLevel level, string contains, Times times)
    {
        loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(contains, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    private class MockServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }
    }
}

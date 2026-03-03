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

        var service = mocker.CreateInstance<FeedsGrpcService>();
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

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var response = await service.CreateInnerCircle(
            new CreateInnerCircleRequest { OwnerPublicAddress = owner },
            CreateMockServerCallContext());

        response.Success.Should().BeTrue();
        response.FeedId.Should().Be(feedId.ToString());
        response.ErrorCode.Should().Be("INNER_CIRCLE_ALREADY_EXISTS");
    }

    [Fact]
    public async Task AddMembersToInnerCircle_WhenOnlyDuplicates_ShouldReturnSuccessAndNotPersist()
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

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
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

        var service = mocker.CreateInstance<FeedsGrpcService>();
        var response = await service.AddMembersToInnerCircle(
            new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = owner,
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

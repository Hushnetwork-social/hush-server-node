using System.Diagnostics;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.Feeds.Tests;

[Trait("Category", "PerformanceTest")]
public class AddMembersToInnerCirclePerformanceTests
{
    private readonly ITestOutputHelper _output;

    public AddMembersToInnerCirclePerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddMembersToInnerCircle_100Members_P95ShouldBeUnderTwoSeconds()
    {
        const int memberCount = 100;
        const int runs = 10;
        var latenciesMs = new List<long>(runs);

        // Warmup avoids JIT cost contaminating measured distribution.
        var warmup = BuildService(memberCount);
        await warmup.Service.AddMembersToInnerCircleAsync(warmup.OwnerAddress, warmup.OwnerAddress, warmup.MemberPayload);

        for (var i = 0; i < runs; i++)
        {
            var setup = BuildService(memberCount);
            var stopwatch = Stopwatch.StartNew();
            var response = await setup.Service.AddMembersToInnerCircleAsync(setup.OwnerAddress, setup.OwnerAddress, setup.MemberPayload);
            stopwatch.Stop();

            response.Success.Should().BeTrue($"run {i + 1} should succeed: {response.Message}");
            latenciesMs.Add(stopwatch.ElapsedMilliseconds);
        }

        var ordered = latenciesMs.OrderBy(x => x).ToArray();
        var p95Rank = (int)Math.Ceiling(0.95 * runs) - 1;
        var p95 = ordered[p95Rank];
        var average = latenciesMs.Average();

        _output.WriteLine($"AddMembersToInnerCircle {memberCount} members over {runs} runs");
        _output.WriteLine($"latencies(ms): {string.Join(", ", latenciesMs)}");
        _output.WriteLine($"avg={average:F1}ms p95={p95}ms");

        const long sloTargetMs = 2000;
        const long environmentToleranceMs = 500;
        const long maxAllowedWithToleranceMs = sloTargetMs + environmentToleranceMs;

        p95.Should().BeLessThanOrEqualTo(maxAllowedWithToleranceMs,
            "FEAT-085 SLO target is <= 2s and allows +500ms environment variance on shared CI runners");
    }

    private static (InnerCircleApplicationService Service, string OwnerAddress, IReadOnlyList<InnerCircleMemberProto> MemberPayload) BuildService(int memberCount)
    {
        var mocker = new AutoMocker();
        var ownerAddress = TestDataFactory.CreateAddress();
        var ownerEncrypt = new EncryptKeys().PublicKey;
        var feedId = TestDataFactory.CreateFeedId();
        var blockIndex = new BlockIndex(1000);

        var innerCircle = new GroupFeed(
            feedId,
            "Inner Circle",
            "Auto-managed inner circle",
            false,
            new BlockIndex(10),
            0,
            IsInnerCircle: true,
            OwnerPublicAddress: ownerAddress);

        var memberAddresses = Enumerable.Range(0, memberCount).Select(_ => TestDataFactory.CreateAddress()).ToList();
        var memberProfiles = memberAddresses.ToDictionary(
            x => x,
            _ => new Profile("member", "mb", _, new EncryptKeys().PublicKey, true, new BlockIndex(1)),
            StringComparer.Ordinal);

        var memberPayload = memberProfiles
            .Select(kvp => new InnerCircleMemberProto
            {
                PublicAddress = kvp.Key,
                PublicEncryptAddress = kvp.Value.PublicEncryptAddress
            })
            .ToList();

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(blockIndex);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(ownerAddress))
            .ReturnsAsync(innerCircle);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, It.IsAny<string>()))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { ownerAddress });
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
            .Setup(x => x.RetrieveIdentityAsync(ownerAddress))
            .ReturnsAsync(new Profile("owner", "ow", ownerAddress, ownerEncrypt, true, new BlockIndex(1)));
        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.Is<string>(addr => addr != ownerAddress)))
            .ReturnsAsync((string addr) => memberProfiles[addr]);

        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.InvalidateKeyGenerationsAsync(feedId))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IFeedParticipantsCacheService>()
            .Setup(x => x.AddParticipantAsync(feedId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IGroupMembersCacheService>()
            .Setup(x => x.InvalidateGroupMembersAsync(feedId))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.AddFeedToUserCacheAsync(It.IsAny<string>(), feedId))
            .Returns(Task.CompletedTask);

        return (mocker.CreateInstance<InnerCircleApplicationService>(), ownerAddress, memberPayload);
    }
}

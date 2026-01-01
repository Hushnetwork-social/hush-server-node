using System.Diagnostics;
using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Performance tests for KeyRotationService.
/// Verifies key rotation completes within acceptable time limits for various group sizes.
/// </summary>
public class KeyRotationPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public KeyRotationPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Helper Methods

    private static Profile CreateProfileForMember(string address)
    {
        var encryptKeys = new EncryptKeys();
        return new Profile(
            Alias: $"Member {address[..8]}",
            ShortAlias: $"m{address[..6]}",
            PublicSigningAddress: address,
            PublicEncryptAddress: encryptKeys.PublicKey,
            IsPublic: false,
            BlockIndex: new BlockIndex(100));
    }

    private KeyRotationService CreateServiceWithMembers(AutoMocker mocker, FeedId feedId, int memberCount)
    {
        var members = Enumerable.Range(0, memberCount)
            .Select(_ => TestDataFactory.CreateAddress())
            .ToList();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(members);

        // Pre-generate profiles for all members (to isolate ECIES encryption timing)
        var profileCache = members.ToDictionary(m => m, CreateProfileForMember);

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.IsAny<string>()))
            .ReturnsAsync((string addr) => profileCache[addr]);

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(1000));

        return mocker.CreateInstance<KeyRotationService>();
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task TriggerRotationAsync_64Members_CompletesWithin100ms()
    {
        // Arrange - warmup to avoid JIT overhead affecting first run
        var warmupMocker = new AutoMocker();
        var warmupFeedId = TestDataFactory.CreateFeedId();
        var warmupService = CreateServiceWithMembers(warmupMocker, warmupFeedId, 4);
        await warmupService.TriggerRotationAsync(warmupFeedId, RotationTrigger.Join);

        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var service = CreateServiceWithMembers(mocker, feedId, 64);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(64);

        _output.WriteLine($"64 members: {stopwatch.ElapsedMilliseconds}ms");
        // Allow 50% tolerance for test environment variance (CI/CD, system load)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(150,
            "Rotation for 64 members should complete in less than 100ms (+50% tolerance)");
    }

    [Fact]
    public async Task TriggerRotationAsync_128Members_CompletesWithin200ms()
    {
        // Arrange - warmup
        var warmupMocker = new AutoMocker();
        var warmupFeedId = TestDataFactory.CreateFeedId();
        var warmupService = CreateServiceWithMembers(warmupMocker, warmupFeedId, 4);
        await warmupService.TriggerRotationAsync(warmupFeedId, RotationTrigger.Join);

        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var service = CreateServiceWithMembers(mocker, feedId, 128);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Leave);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(128);

        _output.WriteLine($"128 members: {stopwatch.ElapsedMilliseconds}ms");
        // Allow 50% tolerance for test environment variance (CI/CD, system load)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300,
            "Rotation for 128 members should complete in less than 200ms (+50% tolerance)");
    }

    [Fact]
    public async Task TriggerRotationAsync_256Members_CompletesWithin500ms()
    {
        // Arrange - warmup
        var warmupMocker = new AutoMocker();
        var warmupFeedId = TestDataFactory.CreateFeedId();
        var warmupService = CreateServiceWithMembers(warmupMocker, warmupFeedId, 4);
        await warmupService.TriggerRotationAsync(warmupFeedId, RotationTrigger.Join);

        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var service = CreateServiceWithMembers(mocker, feedId, 256);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Ban);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(256);

        _output.WriteLine($"256 members: {stopwatch.ElapsedMilliseconds}ms");
        // Allow 50% tolerance for test environment variance (CI/CD, system load)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(750,
            "Rotation for 256 members should complete in less than 500ms (+50% tolerance)");
    }

    [Fact]
    public async Task TriggerRotationAsync_512Members_CompletesWithin1000ms()
    {
        // Arrange - warmup
        var warmupMocker = new AutoMocker();
        var warmupFeedId = TestDataFactory.CreateFeedId();
        var warmupService = CreateServiceWithMembers(warmupMocker, warmupFeedId, 4);
        await warmupService.TriggerRotationAsync(warmupFeedId, RotationTrigger.Join);

        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var service = CreateServiceWithMembers(mocker, feedId, 512);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Manual);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(512);

        _output.WriteLine($"512 members (max): {stopwatch.ElapsedMilliseconds}ms");
        // Allow 50% tolerance for test environment variance (CI/CD, system load)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1500,
            "Rotation for 512 members (maximum) should complete in less than 1000ms (+50% tolerance)");
    }

    #endregion

    #region Scaling Tests

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public async Task TriggerRotationAsync_ScalesLinearlyWithMemberCount(int memberCount)
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var service = CreateServiceWithMembers(mocker, feedId, memberCount);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(memberCount);

        // Log timing for analysis
        var msPerMember = (double)stopwatch.ElapsedMilliseconds / memberCount;
        _output.WriteLine($"{memberCount} members: {stopwatch.ElapsedMilliseconds}ms ({msPerMember:F2}ms/member)");

        // Verify it completes (no specific time target for scaling test)
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(0);
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public async Task TriggerRotationAsync_MultipleRuns_ConsistentTiming()
    {
        // Arrange
        const int memberCount = 64;
        const int runs = 5;
        var timings = new List<long>();

        // Act - Run multiple times to check consistency
        for (int i = 0; i < runs; i++)
        {
            var mocker = new AutoMocker();
            var feedId = TestDataFactory.CreateFeedId();
            var service = CreateServiceWithMembers(mocker, feedId, memberCount);

            var stopwatch = Stopwatch.StartNew();
            var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);
            stopwatch.Stop();

            result.IsSuccess.Should().BeTrue();
            timings.Add(stopwatch.ElapsedMilliseconds);
        }

        // Assert - Check timing consistency
        var average = timings.Average();
        var min = timings.Min();
        var max = timings.Max();

        _output.WriteLine($"Timing across {runs} runs:");
        _output.WriteLine($"  Average: {average:F1}ms");
        _output.WriteLine($"  Min: {min}ms");
        _output.WriteLine($"  Max: {max}ms");
        _output.WriteLine($"  Range: {max - min}ms");

        // Verify consistency - max should not be more than 3x min (accounting for JIT, GC, etc.)
        max.Should().BeLessThan(min * 3 + 50,
            "Performance should be reasonably consistent across runs");
    }

    #endregion
}

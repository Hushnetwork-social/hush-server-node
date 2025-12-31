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

namespace HushNode.Feeds.Tests;

/// <summary>
/// Unit tests for KeyRotationService.
/// Tests cover key generation, ECIES encryption, member inclusion/exclusion, and error handling.
/// </summary>
public class KeyRotationServiceTests
{
    #region Helper Methods

    private static string CreateValidPublicEncryptKey()
    {
        // Generate a valid secp256k1 public key for testing
        var encryptKeys = new EncryptKeys();
        return encryptKeys.PublicKey;
    }

    private static Profile CreateProfile(string address)
    {
        return new Profile(
            Alias: "Test User",
            ShortAlias: "testuser",
            PublicSigningAddress: address,
            PublicEncryptAddress: CreateValidPublicEncryptKey(),
            IsPublic: false,
            BlockIndex: new BlockIndex(100));
    }

    #endregion

    #region Success Scenarios

    [Fact]
    public async Task TriggerRotationAsync_WithValidGroup_ShouldReturnSuccessWithNewKeyGeneration()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(3);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(500));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.NewKeyGeneration.Should().Be(4); // 3 + 1
        result.Payload.Should().NotBeNull();
        result.Payload!.NewKeyGeneration.Should().Be(4);
        result.Payload.PreviousKeyGeneration.Should().Be(3);
    }

    [Fact]
    public async Task TriggerRotationAsync_WithMultipleMembers_ShouldEncryptForAllMembers()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var member1 = TestDataFactory.CreateAddress();
        var member2 = TestDataFactory.CreateAddress();
        var member3 = TestDataFactory.CreateAddress();
        var member4 = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { member1, member2, member3, member4 });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.IsAny<string>()))
            .ReturnsAsync((string addr) => CreateProfile(addr));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Leave);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(4);
        result.Payload.EncryptedKeys.Select(e => e.MemberPublicAddress)
            .Should().BeEquivalentTo(new[] { member1, member2, member3, member4 });
    }

    [Fact]
    public async Task TriggerRotationAsync_GeneratesValidAesKey_ShouldHaveBase64EncodedKey()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(1);

        // The encrypted key should be Base64-encoded ECIES ciphertext
        var encryptedKey = result.Payload.EncryptedKeys[0].EncryptedAesKey;
        var action = () => Convert.FromBase64String(encryptedKey);
        action.Should().NotThrow("Encrypted key should be valid Base64");
    }

    [Fact]
    public async Task TriggerRotationAsync_KeyGenerationIsMonotonicallyIncreasing()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(7);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Manual);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.NewKeyGeneration.Should().Be(8); // Exactly previous + 1
        result.Payload!.PreviousKeyGeneration.Should().Be(7);
        result.Payload.NewKeyGeneration.Should().Be(8);
    }

    #endregion

    #region Member Exclusion/Inclusion Tests

    [Fact]
    public async Task TriggerRotationAsync_WithLeavingMember_ShouldExcludeThatMember()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var alice = TestDataFactory.CreateAddress();
        var bob = TestDataFactory.CreateAddress();
        var charlie = TestDataFactory.CreateAddress(); // Leaving member

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(2);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { alice, bob, charlie });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.IsAny<string>()))
            .ReturnsAsync((string addr) => CreateProfile(addr));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(
            feedId,
            RotationTrigger.Leave,
            leavingMemberAddress: charlie);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(2);
        result.Payload.EncryptedKeys.Select(e => e.MemberPublicAddress)
            .Should().BeEquivalentTo(new[] { alice, bob });
        result.Payload.EncryptedKeys.Select(e => e.MemberPublicAddress)
            .Should().NotContain(charlie);
    }

    [Fact]
    public async Task TriggerRotationAsync_WithJoiningMember_ShouldIncludeThatMember()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var alice = TestDataFactory.CreateAddress();
        var bob = TestDataFactory.CreateAddress();
        var dave = TestDataFactory.CreateAddress(); // Joining member

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(1);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { alice, bob }); // Dave not yet in list

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.IsAny<string>()))
            .ReturnsAsync((string addr) => CreateProfile(addr));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(
            feedId,
            RotationTrigger.Join,
            joiningMemberAddress: dave);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(3);
        result.Payload.EncryptedKeys.Select(e => e.MemberPublicAddress)
            .Should().BeEquivalentTo(new[] { alice, bob, dave });
    }

    [Fact]
    public async Task TriggerRotationAsync_WithBannedMember_ShouldExcludeThatMember()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var admin = TestDataFactory.CreateAddress();
        var bannedMember = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(5);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { admin, bannedMember });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.IsAny<string>()))
            .ReturnsAsync((string addr) => CreateProfile(addr));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(
            feedId,
            RotationTrigger.Ban,
            leavingMemberAddress: bannedMember);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(1);
        result.Payload.EncryptedKeys[0].MemberPublicAddress.Should().Be(admin);
        result.Payload.RotationTrigger.Should().Be(RotationTrigger.Ban);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task TriggerRotationAsync_WithNonExistentGroup_ShouldReturnFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync((int?)null);

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Manual);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not exist");
        result.NewKeyGeneration.Should().BeNull();
        result.Payload.Should().BeNull();
    }

    [Fact]
    public async Task TriggerRotationAsync_WithNoActiveMembers_ShouldReturnFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string>()); // Empty list

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Leave);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no active members");
    }

    [Fact]
    public async Task TriggerRotationAsync_WithMemberCountExceedingLimit_ShouldReturnFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        // Create 600 member addresses (exceeds 512 limit)
        var tooManyMembers = Enumerable.Range(0, 600)
            .Select(_ => TestDataFactory.CreateAddress())
            .ToList();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(tooManyMembers);

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("600");
        result.ErrorMessage.Should().Contain("512");
    }

    [Fact]
    public async Task TriggerRotationAsync_WithMissingMemberIdentity_ShouldReturnFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberWithNoIdentity = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberWithNoIdentity });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberWithNoIdentity))
            .ReturnsAsync(new NonExistingProfile());

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Could not retrieve identity");
        result.ErrorMessage.Should().Contain(memberWithNoIdentity);
    }

    [Fact]
    public async Task TriggerRotationAsync_WithMemberHavingInvalidPublicKey_ShouldReturnFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        // Return a profile with an invalid/malformed public encrypt key
        var invalidProfile = new Profile(
            Alias: "Invalid User",
            ShortAlias: "invalid",
            PublicSigningAddress: memberAddress,
            PublicEncryptAddress: "not-a-valid-public-key",
            IsPublic: false,
            BlockIndex: new BlockIndex(100));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(invalidProfile);

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("encryption failed");
        result.ErrorMessage.Should().Contain("invalid public key format");
    }

    [Fact]
    public async Task TriggerRotationAsync_WithMemberHavingEmptyPublicKey_ShouldReturnFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        // Return a profile with an empty public encrypt key
        var emptyKeyProfile = new Profile(
            Alias: "Empty Key User",
            ShortAlias: "empty",
            PublicSigningAddress: memberAddress,
            PublicEncryptAddress: "",
            IsPublic: false,
            BlockIndex: new BlockIndex(100));

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(emptyKeyProfile);

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty public encrypt key");
    }

    #endregion

    #region Payload Validation Tests

    [Fact]
    public async Task TriggerRotationAsync_PayloadContainsCorrectTrigger()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(250));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Unban);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.RotationTrigger.Should().Be(RotationTrigger.Unban);
        result.Payload.FeedId.Should().Be(feedId);
        result.Payload.ValidFromBlock.Should().Be(250);
    }

    [Fact]
    public async Task TriggerRotationAsync_EncryptedKeysHaveValidStructure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var encryptedKey = result.Payload!.EncryptedKeys[0];

        encryptedKey.MemberPublicAddress.Should().Be(memberAddress);
        encryptedKey.EncryptedAesKey.Should().NotBeNullOrEmpty();

        // ECIES ciphertext: 65 (ephemeral pub) + 12 (nonce) + data + 16 (tag)
        // Should be at least 93 bytes for minimal message, base64 encoded
        var encryptedBytes = Convert.FromBase64String(encryptedKey.EncryptedAesKey);
        encryptedBytes.Length.Should().BeGreaterThan(93);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task TriggerRotationAsync_WithSingleMemberAdmin_ShouldSucceedWithOneKey()
    {
        // Arrange - Group with only the admin (single member)
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { adminAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(adminAddress))
            .ReturnsAsync(CreateProfile(adminAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Manual);

        // Assert - Single member rotation should succeed
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(1);
        result.Payload.EncryptedKeys[0].MemberPublicAddress.Should().Be(adminAddress);
        result.NewKeyGeneration.Should().Be(1);
    }

    [Fact]
    public async Task TriggerRotationAsync_WithExactly512Members_ShouldSucceed()
    {
        // Arrange - Maximum allowed group size
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var members = Enumerable.Range(0, 512)
            .Select(_ => TestDataFactory.CreateAddress())
            .ToList();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(members);

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(It.IsAny<string>()))
            .ReturnsAsync((string addr) => CreateProfile(addr));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert - Exactly 512 members should succeed
        result.IsSuccess.Should().BeTrue();
        result.Payload!.EncryptedKeys.Should().HaveCount(512);
    }

    [Fact]
    public async Task TriggerRotationAsync_With513Members_ShouldFail()
    {
        // Arrange - One over the limit
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var tooManyMembers = Enumerable.Range(0, 513)
            .Select(_ => TestDataFactory.CreateAddress())
            .ToList();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(tooManyMembers);

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert - 513 members should fail
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("513");
        result.ErrorMessage.Should().Contain("512");
    }

    [Fact]
    public async Task TriggerRotationAsync_ConcurrentCalls_ShouldMaintainKeyGenerationMonotonicity()
    {
        // Arrange - Simulate concurrent rotation attempts
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        // Track how many times GetMaxKeyGenerationAsync is called
        var callCount = 0;

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(() =>
            {
                // Each call gets the "current" key generation, simulating sequential state
                return callCount++;
            });

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act - Launch multiple concurrent rotation attempts
        var tasks = new[]
        {
            service.TriggerRotationAsync(feedId, RotationTrigger.Join),
            service.TriggerRotationAsync(feedId, RotationTrigger.Join),
            service.TriggerRotationAsync(feedId, RotationTrigger.Join)
        };

        var results = await Task.WhenAll(tasks);

        // Assert - All should succeed with increasing key generations
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());

        var keyGenerations = results.Select(r => r.NewKeyGeneration!.Value).ToList();
        keyGenerations.Should().OnlyHaveUniqueItems("Each rotation should produce a unique KeyGeneration");

        // Verify monotonically increasing (each is exactly previous + 1)
        var payloads = results.Select(r => r.Payload!).OrderBy(p => p.NewKeyGeneration).ToList();
        for (int i = 0; i < payloads.Count; i++)
        {
            payloads[i].NewKeyGeneration.Should().Be(i + 1);
            payloads[i].PreviousKeyGeneration.Should().Be(i);
        }
    }

    [Fact]
    public async Task TriggerRotationAsync_WhenAllMembersLeave_ShouldReturnFailure()
    {
        // Arrange - All members leave (leaving member is the only member)
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var lastMember = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(2);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { lastMember });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(lastMember))
            .ReturnsAsync(CreateProfile(lastMember));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act - Last member leaves
        var result = await service.TriggerRotationAsync(
            feedId,
            RotationTrigger.Leave,
            leavingMemberAddress: lastMember);

        // Assert - Should fail because no members remain
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no active members");
    }

    [Fact]
    public async Task TriggerRotationAsync_KeyGenerationStartsFromZero_ShouldBeOne()
    {
        // Arrange - First rotation after group creation
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0); // KeyGeneration 0 was set during group creation

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveGroupMemberAddressesAsync(feedId))
            .ReturnsAsync(new List<string> { memberAddress });

        mocker.GetMock<IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(memberAddress))
            .ReturnsAsync(CreateProfile(memberAddress));

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        var service = mocker.CreateInstance<KeyRotationService>();

        // Act
        var result = await service.TriggerRotationAsync(feedId, RotationTrigger.Join);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Payload!.PreviousKeyGeneration.Should().Be(0);
        result.Payload.NewKeyGeneration.Should().Be(1);
    }

    #endregion
}

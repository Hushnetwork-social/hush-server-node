using FluentAssertions;
using HushNode.Identity.Storage;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using HushShared.Reactions.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for MembershipService - Merkle tree and commitment management.
/// </summary>
public class MembershipServiceTests
{
    private readonly Mock<IUnitOfWorkProvider<ReactionsDbContext>> _unitOfWorkProviderMock;
    private readonly Mock<IReadOnlyUnitOfWork<ReactionsDbContext>> _readOnlyUnitOfWorkMock;
    private readonly Mock<IWritableUnitOfWork<ReactionsDbContext>> _writableUnitOfWorkMock;
    private readonly Mock<IUnitOfWorkProvider<IdentityDbContext>> _identityUnitOfWorkProviderMock;
    private readonly Mock<IReadOnlyUnitOfWork<IdentityDbContext>> _identityReadOnlyUnitOfWorkMock;
    private readonly Mock<ICommitmentRepository> _commitmentRepoMock;
    private readonly Mock<IMerkleTreeRepository> _merkleRepoMock;
    private readonly Mock<IIdentityRepository> _identityRepoMock;
    private readonly Mock<IUserCommitmentService> _userCommitmentServiceMock;
    private readonly IPoseidonHash _poseidon;
    private readonly Mock<ILogger<MembershipService>> _loggerMock;
    private readonly MembershipService _service;

    public MembershipServiceTests()
    {
        _unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<ReactionsDbContext>>();
        _readOnlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<ReactionsDbContext>>();
        _writableUnitOfWorkMock = new Mock<IWritableUnitOfWork<ReactionsDbContext>>();
        _identityUnitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<IdentityDbContext>>();
        _identityReadOnlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<IdentityDbContext>>();
        _commitmentRepoMock = MockRepositories.CreateCommitmentRepository();
        _merkleRepoMock = MockRepositories.CreateMerkleTreeRepository();
        _identityRepoMock = new Mock<IIdentityRepository>();
        _userCommitmentServiceMock = new Mock<IUserCommitmentService>();
        _poseidon = new PoseidonHash();
        _loggerMock = new Mock<ILogger<MembershipService>>();

        // Setup unit of work to return repositories
        _readOnlyUnitOfWorkMock.Setup(x => x.GetRepository<ICommitmentRepository>())
            .Returns(_commitmentRepoMock.Object);
        _readOnlyUnitOfWorkMock.Setup(x => x.GetRepository<IMerkleTreeRepository>())
            .Returns(_merkleRepoMock.Object);
        _writableUnitOfWorkMock.Setup(x => x.GetRepository<ICommitmentRepository>())
            .Returns(_commitmentRepoMock.Object);
        _writableUnitOfWorkMock.Setup(x => x.GetRepository<IMerkleTreeRepository>())
            .Returns(_merkleRepoMock.Object);
        _identityReadOnlyUnitOfWorkMock.Setup(x => x.GetRepository<IIdentityRepository>())
            .Returns(_identityRepoMock.Object);

        _unitOfWorkProviderMock.Setup(x => x.CreateReadOnly())
            .Returns(_readOnlyUnitOfWorkMock.Object);
        _unitOfWorkProviderMock.Setup(x => x.CreateWritable(It.IsAny<System.Data.IsolationLevel>()))
            .Returns(_writableUnitOfWorkMock.Object);
        _unitOfWorkProviderMock.Setup(x => x.CreateWritable())
            .Returns(_writableUnitOfWorkMock.Object);
        _identityUnitOfWorkProviderMock.Setup(x => x.CreateReadOnly())
            .Returns(_identityReadOnlyUnitOfWorkMock.Object);
        _identityRepoMock.Setup(x => x.GetAllProfilesAsync())
            .ReturnsAsync(Array.Empty<Profile>());
        _userCommitmentServiceMock.Setup(x => x.DeriveCommitmentFromAddress(It.IsAny<string>()))
            .Returns((string address) => System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(address)));

        _service = new MembershipService(
            _unitOfWorkProviderMock.Object,
            _identityUnitOfWorkProviderMock.Object,
            _poseidon,
            _userCommitmentServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task IsCommitmentRegisteredAsync_NotRegistered_ShouldReturnFalse()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();

        var result = await _service.IsCommitmentRegisteredAsync(feedId, commitment);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCommitmentRegisteredAsync_Registered_ShouldReturnTrue()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();
        var memberCommitment = new FeedMemberCommitment(feedId, commitment, DateTime.UtcNow);

        _commitmentRepoMock.SetupCommitments(feedId, memberCommitment);

        var result = await _service.IsCommitmentRegisteredAsync(feedId, commitment);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentRootsAsync_NoRoots_ShouldReturnEmpty()
    {
        var feedId = TestDataFactory.CreateFeedId();

        var result = await _service.GetRecentRootsAsync(feedId, 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentRootsAsync_WithRoots_ShouldReturnThem()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var root1 = TestDataFactory.CreateMerkleRootHistory(feedId, 100);
        var root2 = TestDataFactory.CreateMerkleRootHistory(feedId, 99);
        var root3 = TestDataFactory.CreateMerkleRootHistory(feedId, 98);

        _merkleRepoMock.SetupMerkleRoots(feedId, root1, root2, root3);

        var result = await _service.GetRecentRootsAsync(feedId, 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecentRootsAsync_RequestMoreThanAvailable_ShouldReturnAvailable()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var root1 = TestDataFactory.CreateMerkleRootHistory(feedId, 100);

        _merkleRepoMock.SetupMerkleRoots(feedId, root1);

        var result = await _service.GetRecentRootsAsync(feedId, 5);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterCommitmentAsync_NewCommitment_ShouldSucceed()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();

        // Setup empty feed - both commitment repo and merkle repo
        _commitmentRepoMock.Setup(x => x.GetCommitmentsForFeedAsync(feedId))
            .ReturnsAsync(new List<FeedMemberCommitment>());
        _merkleRepoMock.Setup(x => x.GetCommitmentsAsync(feedId))
            .ReturnsAsync(new List<byte[]>());
        _merkleRepoMock.Setup(x => x.GetCommitmentCountAsync(feedId))
            .ReturnsAsync(0);

        var result = await _service.RegisterCommitmentAsync(feedId, commitment);

        result.Success.Should().BeTrue();
        result.MerkleRoot.Should().NotBeNull();
        result.MerkleRoot!.Length.Should().Be(32);
    }

    [Fact]
    public async Task RegisterCommitmentAsync_AlreadyRegistered_ShouldReturnAlreadyExists()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();
        var existing = new FeedMemberCommitment(feedId, commitment, DateTime.UtcNow);

        _commitmentRepoMock.SetupCommitments(feedId, existing);

        var result = await _service.RegisterCommitmentAsync(feedId, commitment);

        result.Success.Should().BeTrue();
        result.AlreadyRegistered.Should().BeTrue();
    }

    [Fact]
    public async Task GetMembershipProofAsync_NotMember_ShouldReturnNotMember()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();

        // Setup feed with different commitments
        var otherCommitment = new FeedMemberCommitment(
            feedId,
            TestDataFactory.CreateCommitment(),
            DateTime.UtcNow);
        _commitmentRepoMock.Setup(x => x.GetCommitmentsForFeedAsync(feedId))
            .ReturnsAsync(new List<FeedMemberCommitment> { otherCommitment });

        var result = await _service.GetMembershipProofAsync(feedId, commitment);

        result.IsMember.Should().BeFalse();
    }

    [Fact]
    public async Task GetMembershipProofAsync_IsMember_ShouldReturnProof()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();
        var memberCommitment = new FeedMemberCommitment(feedId, commitment, DateTime.UtcNow);

        // Setup commitment repo - SetupCommitments also sets up IsCommitmentRegisteredAsync
        _commitmentRepoMock.SetupCommitments(feedId, memberCommitment);

        // Also set up merkle repo for GetCommitmentsAsync (used by MembershipService)
        _merkleRepoMock.Setup(x => x.GetCommitmentsAsync(feedId))
            .ReturnsAsync(new List<byte[]> { commitment });

        var root = TestDataFactory.CreateMerkleRootHistory(feedId, 100);
        _merkleRepoMock.SetupMerkleRoots(feedId, root);

        var result = await _service.GetMembershipProofAsync(feedId, commitment);

        result.IsMember.Should().BeTrue();
        result.MerkleRoot.Should().NotBeNull();
        result.PathElements.Should().NotBeNull();
        result.PathIndices.Should().NotBeNull();
        result.TreeDepth.Should().Be(20); // Expected tree depth
    }

    [Fact]
    public async Task GetMembershipProofAsync_ProofShouldHaveCorrectDepth()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();
        var memberCommitment = new FeedMemberCommitment(feedId, commitment, DateTime.UtcNow);

        // Setup commitment repo - SetupCommitments also sets up IsCommitmentRegisteredAsync
        _commitmentRepoMock.SetupCommitments(feedId, memberCommitment);

        // Also set up merkle repo for GetCommitmentsAsync
        _merkleRepoMock.Setup(x => x.GetCommitmentsAsync(feedId))
            .ReturnsAsync(new List<byte[]> { commitment });

        var root = TestDataFactory.CreateMerkleRootHistory(feedId, 100);
        _merkleRepoMock.SetupMerkleRoots(feedId, root);

        var result = await _service.GetMembershipProofAsync(feedId, commitment);

        result.PathElements!.Length.Should().Be(20);
        result.PathIndices!.Length.Should().Be(20);
    }

    [Fact]
    public async Task UpdateMerkleRootAsync_EmptyFeed_ShouldReturnZeroRoot()
    {
        var feedId = TestDataFactory.CreateFeedId();

        // Set up merkle repo for GetCommitmentsAsync (empty)
        _merkleRepoMock.Setup(x => x.GetCommitmentsAsync(feedId))
            .ReturnsAsync(new List<byte[]>());

        var result = await _service.UpdateMerkleRootAsync(feedId, 100);

        result.Should().NotBeNull();
        result.Length.Should().Be(32);
    }

    [Fact]
    public async Task UpdateMerkleRootAsync_WithCommitments_ShouldComputeRoot()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment1 = TestDataFactory.CreateCommitment();
        var commitment2 = TestDataFactory.CreateCommitment();

        // Set up merkle repo for GetCommitmentsAsync
        _merkleRepoMock.Setup(x => x.GetCommitmentsAsync(feedId))
            .ReturnsAsync(new List<byte[]> { commitment1, commitment2 });

        var result = await _service.UpdateMerkleRootAsync(feedId, 100);

        result.Should().NotBeNull();
        result.Length.Should().Be(32);

        // Root should be saved
        _merkleRepoMock.Verify(x => x.SaveRootAsync(
            It.Is<MerkleRootHistory>(r =>
                r.FeedId == feedId &&
                r.MerkleRoot.Length == 32 &&
                r.BlockHeight == 100)), Times.Once);
    }

    [Fact]
    public async Task UpdateMerkleRootAsync_SameCommitments_ShouldProduceSameRoot()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();

        // Set up merkle repo for GetCommitmentsAsync
        _merkleRepoMock.Setup(x => x.GetCommitmentsAsync(feedId))
            .ReturnsAsync(new List<byte[]> { commitment });

        var result1 = await _service.UpdateMerkleRootAsync(feedId, 100);
        var result2 = await _service.UpdateMerkleRootAsync(feedId, 101);

        result1.Should().BeEquivalentTo(result2);
    }

    [Fact]
    public async Task IsCommitmentRegisteredAsync_GlobalScope_WhenProfileCommitmentExists_ShouldReturnTrue()
    {
        var profile = new Profile(
            Alias: "alice",
            ShortAlias: "ali",
            PublicSigningAddress: "addr-1",
            PublicEncryptAddress: "enc-1",
            IsPublic: true,
            BlockIndex: new HushShared.Blockchain.BlockModel.BlockIndex(1));
        var expectedCommitment = TestDataFactory.CreateCommitment();

        _identityRepoMock.Setup(x => x.GetAllProfilesAsync())
            .ReturnsAsync(new[] { profile });
        _userCommitmentServiceMock.Setup(x => x.DeriveCommitmentFromAddress("addr-1"))
            .Returns(expectedCommitment);

        var result = await _service.IsCommitmentRegisteredAsync(PublicReactionScopes.GlobalHushMembers, expectedCommitment);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentRootsAsync_GlobalScopeWithoutPersistedRoot_ShouldMaterializeRootFromProfiles()
    {
        var profile = new Profile(
            Alias: "alice",
            ShortAlias: "ali",
            PublicSigningAddress: "addr-1",
            PublicEncryptAddress: "enc-1",
            IsPublic: true,
            BlockIndex: new HushShared.Blockchain.BlockModel.BlockIndex(1));
        var expectedCommitment = TestDataFactory.CreateCommitment();

        _identityRepoMock.Setup(x => x.GetAllProfilesAsync())
            .ReturnsAsync(new[] { profile });
        _userCommitmentServiceMock.Setup(x => x.DeriveCommitmentFromAddress("addr-1"))
            .Returns(expectedCommitment);

        MerkleRootHistory? savedRoot = null;
        _merkleRepoMock.Setup(x => x.SaveRootAsync(It.IsAny<MerkleRootHistory>()))
            .Callback<MerkleRootHistory>(root => savedRoot = root)
            .Returns(Task.CompletedTask);
        _merkleRepoMock.Setup(x => x.GetRecentRootsAsync(PublicReactionScopes.GlobalHushMembers, It.IsAny<int>()))
            .ReturnsAsync(() => savedRoot is null ? Array.Empty<MerkleRootHistory>() : new[] { savedRoot });

        var roots = (await _service.GetRecentRootsAsync(PublicReactionScopes.GlobalHushMembers, 3)).ToList();

        roots.Should().ContainSingle();
        roots[0].FeedId.Should().Be(PublicReactionScopes.GlobalHushMembers);
        roots[0].MerkleRoot.Should().HaveCount(32);
    }

    [Fact]
    public async Task GlobalScope_ProofAndRecentRoots_ShouldRemainStable_WhenProfileEnumerationOrderChanges()
    {
        var alice = new Profile(
            Alias: "alice",
            ShortAlias: "ali",
            PublicSigningAddress: "addr-b",
            PublicEncryptAddress: "enc-b",
            IsPublic: true,
            BlockIndex: new HushShared.Blockchain.BlockModel.BlockIndex(1));
        var bob = new Profile(
            Alias: "bob",
            ShortAlias: "bob",
            PublicSigningAddress: "addr-a",
            PublicEncryptAddress: "enc-a",
            IsPublic: true,
            BlockIndex: new HushShared.Blockchain.BlockModel.BlockIndex(2));

        var aliceCommitment = TestDataFactory.CreateCommitment();
        var bobCommitment = TestDataFactory.CreateCommitment();

        var callCount = 0;
        _identityRepoMock.Setup(x => x.GetAllProfilesAsync())
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount % 2 == 1
                    ? new[] { alice, bob }
                    : new[] { bob, alice };
            });

        _userCommitmentServiceMock.Setup(x => x.DeriveCommitmentFromAddress("addr-b"))
            .Returns(aliceCommitment);
        _userCommitmentServiceMock.Setup(x => x.DeriveCommitmentFromAddress("addr-a"))
            .Returns(bobCommitment);

        MerkleRootHistory? savedRoot = null;
        _merkleRepoMock.Setup(x => x.SaveRootAsync(It.IsAny<MerkleRootHistory>()))
            .Callback<MerkleRootHistory>(root => savedRoot = root)
            .Returns(Task.CompletedTask);
        _merkleRepoMock.Setup(x => x.GetRecentRootsAsync(PublicReactionScopes.GlobalHushMembers, It.IsAny<int>()))
            .ReturnsAsync(() => savedRoot is null ? Array.Empty<MerkleRootHistory>() : new[] { savedRoot });

        var proof = await _service.GetMembershipProofAsync(PublicReactionScopes.GlobalHushMembers, bobCommitment);
        var recentRoots = (await _service.GetRecentRootsAsync(PublicReactionScopes.GlobalHushMembers, 1)).ToList();

        proof.IsMember.Should().BeTrue();
        proof.MerkleRoot.Should().NotBeNull();
        recentRoots.Should().ContainSingle();
        recentRoots[0].MerkleRoot.Should().BeEquivalentTo(proof.MerkleRoot!);
    }
}

using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Feeds.Model;
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
    private readonly Mock<ICommitmentRepository> _commitmentRepoMock;
    private readonly Mock<IMerkleTreeRepository> _merkleRepoMock;
    private readonly IPoseidonHash _poseidon;
    private readonly Mock<ILogger<MembershipService>> _loggerMock;
    private readonly MembershipService _service;

    public MembershipServiceTests()
    {
        _unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<ReactionsDbContext>>();
        _readOnlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<ReactionsDbContext>>();
        _writableUnitOfWorkMock = new Mock<IWritableUnitOfWork<ReactionsDbContext>>();
        _commitmentRepoMock = MockRepositories.CreateCommitmentRepository();
        _merkleRepoMock = MockRepositories.CreateMerkleTreeRepository();
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

        _unitOfWorkProviderMock.Setup(x => x.CreateReadOnly())
            .Returns(_readOnlyUnitOfWorkMock.Object);
        _unitOfWorkProviderMock.Setup(x => x.CreateWritable(It.IsAny<System.Data.IsolationLevel>()))
            .Returns(_writableUnitOfWorkMock.Object);
        _unitOfWorkProviderMock.Setup(x => x.CreateWritable())
            .Returns(_writableUnitOfWorkMock.Object);

        _service = new MembershipService(
            _unitOfWorkProviderMock.Object,
            _poseidon,
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
}

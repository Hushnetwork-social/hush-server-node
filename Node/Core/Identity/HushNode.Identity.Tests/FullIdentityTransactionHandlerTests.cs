// FEAT-011 Task 3.6 — deterministic indexing, one-profile persistence, and
// cache-coherence tests for the FullIdentity index path.

using FluentAssertions;
using HushNode.Caching;
using HushNode.Events;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Identity.Model;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class FullIdentityTransactionHandlerTests
{
    private sealed class Harness
    {
        public Harness()
        {
            WritableUnitOfWork = new Mock<IWritableUnitOfWork<IdentityDbContext>>();
            ReadOnlyUnitOfWork = new Mock<IReadOnlyUnitOfWork<IdentityDbContext>>();
            Repository = new Mock<IIdentityRepository>();
            WritableUnitOfWork.Setup(x => x.GetRepository<IIdentityRepository>()).Returns(Repository.Object);
            ReadOnlyUnitOfWork.Setup(x => x.GetRepository<IIdentityRepository>()).Returns(Repository.Object);
            var provider = new Mock<IUnitOfWorkProvider<IdentityDbContext>>();
            provider.Setup(x => x.CreateReadOnly()).Returns(ReadOnlyUnitOfWork.Object);
            provider.Setup(x => x.CreateWritable()).Returns(WritableUnitOfWork.Object);
            Provider = provider.Object;

            Cache = new Mock<IBlockchainCache>();
            Cache.Setup(x => x.LastBlockIndex).Returns(new BlockIndex(42));
            Events = new Mock<IEventAggregator>();
        }

        public Mock<IWritableUnitOfWork<IdentityDbContext>> WritableUnitOfWork { get; }
        public Mock<IReadOnlyUnitOfWork<IdentityDbContext>> ReadOnlyUnitOfWork { get; }
        public Mock<IIdentityRepository> Repository { get; }
        public IUnitOfWorkProvider<IdentityDbContext> Provider { get; }
        public Mock<IBlockchainCache> Cache { get; }
        public Mock<IEventAggregator> Events { get; }

        public FullIdentityTransactionHandler CreateSut() =>
            new(Provider, Cache.Object, Events.Object, new Microsoft.Extensions.Logging.Abstractions.NullLogger<FullIdentityTransactionHandler>());
    }

    private readonly Harness _harness = new();

    private static HushShared.Blockchain.TransactionModel.States.ValidatedTransaction<FullIdentityPayload> BuildValidated(
        HushShared.Blockchain.TransactionModel.States.SignedTransaction<FullIdentityPayload> signed) =>
        new(
            signed,
            new HushShared.Blockchain.Model.SignatureInfo("validator", "sig"));

    [Fact]
    public async Task IndexCommit_PersistsOneProfile_ThenPublishesCacheCoherenceAfterCommit()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        await sut.HandleFullIdentityTransaction(BuildValidated(FullIdentityTestData.BuildSigned()));

        _harness.Repository.Verify(x => x.AddFullIdentity(It.Is<Profile>(p =>
            p.PublicSigningAddress == FullIdentityTestData.K001SigningAddress &&
            p.PublicEncryptAddress == FullIdentityTestData.K001EncryptAddress &&
            p.Alias == FullIdentityTestData.Alias)), Times.Once);
        _harness.WritableUnitOfWork.Verify(x => x.CommitAsync(), Times.Once);
        // Cache coherence is published AFTER the commit (no stale success/absence).
        _harness.Events.Verify(x => x.PublishAsync(It.Is<IdentityUpdatedEvent>(e =>
            e.PublicSigningAddress == FullIdentityTestData.K001SigningAddress)), Times.Once);
    }

    [Fact]
    public async Task AlreadyIndexedIdentity_IsSkipped_NoInsertNoEvent()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(true);

        await sut.HandleFullIdentityTransaction(BuildValidated(FullIdentityTestData.BuildSigned()));

        _harness.Repository.Verify(x => x.AddFullIdentity(It.IsAny<Profile>()), Times.Never);
        _harness.Events.Verify(x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateRace_TypedUniqueViolation_ConvergesWithoutEvent()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);
        _harness.Repository
            .Setup(x => x.AddFullIdentity(It.IsAny<Profile>()))
            .ThrowsAsync(new DbUpdateException("dup", new PostgresException(
                "duplicate key value violates unique constraint \"PK_Profile\"",
                "ERROR",
                "PK_Profile",
                "23505")));

        var act = () => sut.HandleFullIdentityTransaction(BuildValidated(FullIdentityTestData.BuildSigned()));

        await act.Should().NotThrowAsync();
        _harness.Events.Verify(x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()), Times.Never);
    }

    [Fact]
    public async Task NonUniqueDbError_IsNotSwallowed_AndPropagates()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);
        _harness.Repository
            .Setup(x => x.AddFullIdentity(It.IsAny<Profile>()))
            .ThrowsAsync(new DbUpdateException("boom", new InvalidOperationException("connection lost")));

        var act = () => sut.HandleFullIdentityTransaction(BuildValidated(FullIdentityTestData.BuildSigned()));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task EmptyAliasOrAddresses_AreRejectedBeforeAnyWrite()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        await sut.HandleFullIdentityTransaction(BuildValidated(FullIdentityTestData.BuildSigned(alias: "  ")));
        await sut.HandleFullIdentityTransaction(BuildValidated(FullIdentityTestData.BuildSigned(signingAddress: "")));

        _harness.Repository.Verify(x => x.AddFullIdentity(It.IsAny<Profile>()), Times.Never);
    }
}

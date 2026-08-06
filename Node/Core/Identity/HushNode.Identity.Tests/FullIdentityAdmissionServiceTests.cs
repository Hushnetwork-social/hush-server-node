// FEAT-011 Task 3.4 — atomic reservation, concurrency, fault, and restart
// tests for FullIdentity admission.

using FluentAssertions;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Microsoft.EntityFrameworkCore;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class FullIdentityAdmissionServiceTests
{
    private sealed class Harness
    {
        public Harness()
        {
            var unitOfWork = new Mock<IReadOnlyUnitOfWork<IdentityDbContext>>();
            Repository = new Mock<IIdentityRepository>();
            unitOfWork.Setup(x => x.GetRepository<IIdentityRepository>()).Returns(Repository.Object);
            var provider = new Mock<IUnitOfWorkProvider<IdentityDbContext>>();
            provider.Setup(x => x.CreateReadOnly()).Returns(unitOfWork.Object);
            Provider = provider.Object;
        }

        public IUnitOfWorkProvider<IdentityDbContext> Provider { get; }
        public Mock<IIdentityRepository> Repository { get; }

        public FullIdentityAdmissionService CreateSut() =>
            new(new FullIdentityValidator(new FullIdentityCanonicalSerializer(), new FullIdentitySignatureVerifier()), Provider);
    }

    private readonly Harness _harness = new();

    [Fact]
    public async Task FirstValidRegistration_IsAccepted_WithOneReservation()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
    }

    [Fact]
    public async Task ExactRetry_IsPending_WithoutSecondAdmission()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        var first = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);
        var second = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        first.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
        second.Outcome.Should().Be(FullIdentitySubmitOutcome.Pending);
    }

    [Fact]
    public async Task ConflictingSameSigningTransaction_IsConflict()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        var first = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);
        var conflicting = await sut.AdmitAsync(
            FullIdentityTestData.BuildSigned(
                transactionId: Guid.Parse("99999999-9999-4999-8999-999999999999")),
            CancellationToken.None);

        first.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
        conflicting.Outcome.Should().Be(FullIdentitySubmitOutcome.Conflict);
    }

    [Fact]
    public async Task IndexedIdentity_IsAlreadyExists_WithoutReservation()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(FullIdentityTestData.K001SigningAddress)).ReturnsAsync(true);

        var result = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.AlreadyExists);
    }

    [Fact]
    public async Task InvalidContent_IsRejected_WithStableCode_AndNoReservation()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        var forged = await sut.AdmitAsync(
            FullIdentityTestData.BuildSigned(signatureOverride: "bad"),
            CancellationToken.None);
        var afterForged = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        forged.Outcome.Should().Be(FullIdentitySubmitOutcome.RejectedTerminal);
        forged.ValidationCode.Should().Be(FullIdentityValidationCodes.UnsupportedSignatureEncoding);
        // The forged attempt must not have reserved the identity.
        afterForged.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
    }

    [Fact]
    public async Task Release_AllowsReAdmission()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        var first = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);
        await sut.ReleaseAsync(FullIdentityTestData.K001SigningAddress, CancellationToken.None);
        var second = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        first.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
        second.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
    }

    [Fact]
    public async Task MarkIndexed_ThenAnyAdmission_IsAlreadyExists()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);

        await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);
        await sut.MarkIndexedAsync(FullIdentityTestData.K001SigningAddress, CancellationToken.None);

        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(true);
        var after = await sut.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        after.Outcome.Should().Be(FullIdentitySubmitOutcome.AlreadyExists);
    }

    [Fact]
    public async Task ConcurrentSameKeySubmissions_YieldExactlyOneAccepted()
    {
        var sut = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);
        var transaction = FullIdentityTestData.BuildSigned();

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => sut.ReserveAsync(
                FullIdentityTestData.K001SigningAddress,
                transaction.TransactionId.Value.ToString(),
                FullIdentityAdmissionService.ComputeDigest(transaction),
                CancellationToken.None)));

        outcomes.Count(o => o.Outcome == FullIdentitySubmitOutcome.Accepted).Should().Be(1);
        outcomes.Count(o => o.Outcome == FullIdentitySubmitOutcome.Pending).Should().Be(7);
    }

    [Fact]
    public async Task RestartConvergence_NewInstance_ReAdmitsOnce_ThenPending()
    {
        // After a process restart the reservation registry is empty; the exact
        // transaction is re-admitted once (index-time dedupe guarantees one
        // final profile).
        var firstInstance = _harness.CreateSut();
        _harness.Repository.Setup(x => x.AnyAsync(It.IsAny<string>())).ReturnsAsync(false);
        await firstInstance.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        var restarted = _harness.CreateSut();
        var readmitted = await restarted.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);
        var duplicate = await restarted.AdmitAsync(FullIdentityTestData.BuildSigned(), CancellationToken.None);

        readmitted.Outcome.Should().Be(FullIdentitySubmitOutcome.Accepted);
        duplicate.Outcome.Should().Be(FullIdentitySubmitOutcome.Pending);
    }

    [Fact]
    public async Task WrongPayloadKind_FailsClosedWithUnsupportedKind()
    {
        var sut = _harness.CreateSut();
        // An unsigned FullIdentity transaction is AbstractTransaction but NOT
        // SignedTransaction<FullIdentityPayload> — the admission gate rejects it.
        var unsigned = FullIdentityPayloadHandler.CreateNew(
            FullIdentityTestData.Alias,
            FullIdentityTestData.K001SigningAddress,
            FullIdentityTestData.K001EncryptAddress,
            true);

        var result = await sut.AdmitAsync(unsigned, CancellationToken.None);

        result.Outcome.Should().Be(FullIdentitySubmitOutcome.RejectedTerminal);
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.UnsupportedKind);
    }
}

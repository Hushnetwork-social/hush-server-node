// FEAT-015 Task 3.6 — reservation/idempotency/competition contract tests (pure core).
//
// Proves the deterministic admission semantics on the pure classifier:
// exact retry -> PENDING; same tx id different bytes -> idempotency mismatch; no pending ->
// accepted; higher valid rank supersedes; equal rank first-valid; lower rank rejected. Unknown
// outcomes never fabricate acceptance.

using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class HushVotingLicenceReservationCompetitionTests
{
    private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly Guid Subject = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
    private static readonly Guid TxA = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid TxB = Guid.Parse("22222222-3333-4444-8555-666666666666");

    private static HushVotingLicenceReservationClaim Claim(Guid tx, string digest, int rank = 0) =>
        new(
            Subject,
            tx,
            digest,
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            HushVotingLicenceTestData.Veritas2000,
            "hushvoting-licence-catalogue/v1.0.0",
            null,
            HushVotingLicenceTestData.DirectFree,
            rank);

    [Fact]
    public void Exact_retry_returns_pending_without_insert()
    {
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: true,
            ExistingFingerprint: DigestA,
            HasPendingForSubject: true,
            PendingUpgradeRank: 2);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, Claim(TxA, DigestA, rank: 2));

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Pending);
        decision.ShouldInsert.Should().BeFalse();
        decision.ShouldSupersedeExistingPending.Should().BeFalse();
    }

    [Fact]
    public void Transaction_id_reuse_with_different_bytes_is_idempotency_mismatch()
    {
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: true,
            ExistingFingerprint: DigestA,
            HasPendingForSubject: true,
            PendingUpgradeRank: 2);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, Claim(TxA, DigestB, rank: 2));

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Rejected);
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransactionIdempotencyMismatch);
    }

    [Fact]
    public void No_pending_accepts_and_inserts()
    {
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: false,
            ExistingFingerprint: null,
            HasPendingForSubject: false,
            PendingUpgradeRank: null);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, Claim(TxA, DigestA, rank: 0));

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);
        decision.ShouldInsert.Should().BeTrue();
    }

    [Fact]
    public void Higher_valid_rank_supersedes_lower_pending()
    {
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: false,
            ExistingFingerprint: null,
            HasPendingForSubject: true,
            PendingUpgradeRank: 1);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, Claim(TxB, DigestA, rank: 3));

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);
        decision.ShouldInsert.Should().BeTrue();
        decision.ShouldSupersedeExistingPending.Should().BeTrue();
    }

    [Fact]
    public void Lower_rank_cannot_replace_higher_pending()
    {
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: false,
            ExistingFingerprint: null,
            HasPendingForSubject: true,
            PendingUpgradeRank: 3);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, Claim(TxB, DigestA, rank: 1));

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Rejected);
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionPending);
    }

    [Fact]
    public void Same_rank_competition_is_first_valid_pending_retained()
    {
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: false,
            ExistingFingerprint: null,
            HasPendingForSubject: true,
            PendingUpgradeRank: 2);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, Claim(TxB, DigestA, rank: 2));

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Pending);
        decision.ShouldInsert.Should().BeFalse();
        decision.ValidationCode.Should().BeNull(); // same-rank first-valid is PENDING, not a rejection
    }

    [Fact]
    public void Baseline_claim_competes_at_direct_free_rank_zero()
    {
        // A baseline (rank 0) cannot displace any pending upgrade and is rejected while one exists.
        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: false,
            ExistingFingerprint: null,
            HasPendingForSubject: true,
            PendingUpgradeRank: 2);

        var claim = Claim(TxB, DigestA, rank: 0) with
        {
            TransitionIntent = HushVotingLicenceTransitionIntent.BaselineFree,
            RequestedPlanId = HushVotingLicenceTestData.DirectFree,
        };

        var decision = HushVotingLicenceReservationCompetition.Decide(state, claim);

        decision.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Rejected);
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionPending);
    }
}

// EPIC-002 AT-LIC-010 -> FEAT-015 AC-015-011/013/014 -> Phase 6 Tasks 6.3/6.4.
using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class LicenceBlockTimeExpiryTests
{
    [Theory]
    [InlineData("pending", "resolved")]
    [InlineData("superseded", "superseded")]
    [InlineData("resolved", "resolved")]
    public void Indexed_reservation_releases_competition_without_rewriting_retained_history(string before, string after)
    {
        var originalResolution = new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var blockTime = originalResolution.AddDays(1);
        var reservation = new LicencePendingReservationEntity
        {
            LifecycleStatus = before,
            ResolvedAtUtc = before == "pending" ? null : originalResolution,
            RequestedUpgradeRank = 2,
            CanonicalPayloadFingerprintSha256 = new string('a', 64),
        };

        LicenceBlockIndexWriterDecisions.ResolveIndexedReservation(reservation, blockTime);

        reservation.LifecycleStatus.Should().Be(after);
        reservation.ResolvedAtUtc.Should().Be(before == "pending" ? blockTime : originalResolution);
        reservation.RequestedUpgradeRank.Should().Be(2);
        reservation.CanonicalPayloadFingerprintSha256.Should().Be(new string('a', 64));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Baseline_uses_upper_exclusive_expiry_at_containing_block_time(long expiryOffsetTicks, bool allowed)
    {
        // Arrange: the read path leaves an expired row's lifecycle untouched.
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var effective = new DateTime(2032, 2, 29, 0, 0, 0, DateTimeKind.Utc);
        var expiry = effective.AddYears(1);
        var assignment = new LicenceAssignmentEntity
        {
            PlanId = "hushvoting.veritas.2000",
            AssignedCatalogueVersion = catalogue.Version.Value,
            LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
            EffectiveFromUtc = effective,
            ExpiresAtUtc = expiry,
            OriginatingTransactionId = Guid.NewGuid(),
        };
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree, "hushvoting.direct.free", catalogue.Version.Value);

        // Act: replay time is explicit, independent of the executing machine's clock.
        var state = LicenceBlockIndexWriterDecisions.CurrentlyActiveState(
            catalogue, assignment, expiry.AddTicks(expiryOffsetTicks));
        var decision = HushVotingLicenceTransitionDecisionCore.Decide(catalogue, payload, state);

        // Assert: expiry allows the signed baseline, without a query-time lifecycle mutation.
        decision.IsValid.Should().Be(allowed);
        if (!allowed)
            decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.BaselineRequiresNoActiveEntitlement);
        assignment.LifecycleStatus.Should().Be(LicencePersistenceVocabulary.LifecycleActive);
    }

    [Fact]
    public void Perpetual_assignment_still_blocks_a_second_baseline_at_later_block_time()
    {
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var effective = new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var assignment = new LicenceAssignmentEntity
        {
            PlanId = "hushvoting.direct.free",
            AssignedCatalogueVersion = catalogue.Version.Value,
            EffectiveFromUtc = effective,
            ExpiresAtUtc = null,
            OriginatingTransactionId = Guid.NewGuid(),
        };
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree, "hushvoting.direct.free", catalogue.Version.Value);

        var state = LicenceBlockIndexWriterDecisions.CurrentlyActiveState(catalogue, assignment, effective.AddYears(10));
        var decision = HushVotingLicenceTransitionDecisionCore.Decide(catalogue, payload, state);

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.BaselineRequiresNoActiveEntitlement);
    }
}

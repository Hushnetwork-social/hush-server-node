using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.gRPC;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingEntitlement;

// EPIC-002 -> FEAT-018 AC-006/009 -> P018-2-01/02 -> T018-2-01/02.
public sealed class HushVotingEntitlementContractTests
{
    [Theory]
    [InlineData(ElectionEntitlementReason.NotActive, "ENTITLEMENT_NOT_ACTIVE", ElectionCommandErrorCode.ValidationFailed)]
    [InlineData(ElectionEntitlementReason.LimitExceeded, "ENTITLEMENT_LIMIT_EXCEEDED", ElectionCommandErrorCode.ValidationFailed)]
    [InlineData(ElectionEntitlementReason.ProfileNotAllowed, "ENTITLEMENT_PROFILE_NOT_ALLOWED", ElectionCommandErrorCode.ValidationFailed)]
    [InlineData(ElectionEntitlementReason.AuthorityUnavailable, "ENTITLEMENT_AUTHORITY_UNAVAILABLE", ElectionCommandErrorCode.DependencyBlocked)]
    [InlineData(ElectionEntitlementReason.CaptureUnavailable, "ENTITLEMENT_CAPTURE_UNAVAILABLE", ElectionCommandErrorCode.DependencyBlocked)]
    [InlineData(ElectionEntitlementReason.SemanticsUnsupported, "ENTITLEMENT_SEMANTICS_UNSUPPORTED", ElectionCommandErrorCode.NotSupported)]
    [InlineData(ElectionEntitlementReason.RosterReplacementAfterLink, "ROSTER_REPLACEMENT_AFTER_LINK", ElectionCommandErrorCode.Conflict)]
    public void RejectionPreservesExactSafeWireMeaning(ElectionEntitlementReason reason, string expected, ElectionCommandErrorCode category)
    {
        var result = ElectionEntitlementResults.Reject(reason);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(category);
        result.ToProto().EntitlementReason.Should().Be(expected);
        ((int)result.ToProto().ErrorCode).Should().Be((int)category);
    }

    [Fact]
    public void UnknownReasonCannotBecomeSuccessOrBaselineIssuance()
    {
        var result = ElectionEntitlementResults.Reject((ElectionEntitlementReason)int.MaxValue);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.NotSupported);
        result.ToProto().EntitlementReason.Should().Be("ENTITLEMENT_SEMANTICS_UNSUPPORTED");
        new ElectionCommandResult().ToProto().EntitlementReason.Should().BeEmpty();
    }

    [Fact]
    public void SupportedCaptureRetainsOpenAuthorizationWithoutTodaysClock()
    {
        var capture = ValidCapture();
        capture.HasSupportedSemantics().Should().BeTrue();
        (capture with { OpenBlockTimeUtc = capture.ExpiresAtUtc!.Value.AddTicks(-1) }).HasSupportedSemantics().Should().BeTrue();
        (capture with { OpenBlockTimeUtc = capture.ExpiresAtUtc!.Value }).HasSupportedSemantics().Should().BeFalse();
        (capture with { OpenBlockTimeUtc = capture.EffectiveFromUtc.AddTicks(-1) }).HasSupportedSemantics().Should().BeFalse();
        // Expired today is intentionally irrelevant to stored Open authorization.
        capture.ExpiresAtUtc.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void UnsupportedOrIncompleteCaptureFailsClosedWithoutTruncation()
    {
        var good = ValidCapture();
        var invalid = new[] {
            good with { SchemaVersion = 2 }, good with { PolicyVersion = "future" },
            good with { PlanId = "unknown" }, good with { AllowedGovernanceOptionIdsJson = "[\"unknown\"]" },
            good with { AllowedGovernanceOptionIdsJson = "null" }, good with { AllowedGovernanceOptionIdsJson = "{" },
            good with { OpenTransactionId = Guid.Empty }, good with { OpenBlockId = Guid.Empty },
            good with { LicenceSubjectId = Guid.Empty }, good with { OriginatingLicenceTransactionId = Guid.Empty },
            good with { FrozenRosterBasisId = Guid.Empty }, good with { OpenTransactionPosition = -1 },
            good with { AssignedCatalogueDigestSha256 = "bad" }, good with { FrozenEligibleVoterCount = 501 },
            good with { AllowedGovernanceOptionIdsJson = new string('x', 16_385) },
            good with { OpenBlockTimeUtc = DateTime.SpecifyKind(good.OpenBlockTimeUtc, DateTimeKind.Unspecified) }
        };
        foreach (var capture in invalid) capture.HasSupportedSemantics().Should().BeFalse();
        good.HasSupportedSemantics().Should().BeTrue();
    }

    [Fact]
    public void RejectedOpenRetainsOnlySupportedBusinessOutcomesAndCanonicalProvenance()
    {
        var good = new ElectionOpenRejection(Guid.NewGuid(), ElectionId.NewElectionId,
            Guid.NewGuid(), 10, 2, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null, (int)ElectionCommandErrorCode.ValidationFailed, ElectionEntitlementReason.NotActive);
        good.HasSupportedSemantics().Should().BeTrue();
        (good with { Reason = ElectionEntitlementReason.None, ErrorCategory = (int)ElectionCommandErrorCode.Forbidden })
            .HasSupportedSemantics().Should().BeTrue();
        var invalid = new[] {
            good with { TransactionId = Guid.Empty }, good with { ElectionId = ElectionId.Empty },
            good with { BlockId = Guid.Empty }, good with { GovernedProposalId = Guid.Empty },
            good with { BlockHeight = -1 }, good with { TransactionPosition = -1 },
            good with { BlockTimeUtc = DateTime.SpecifyKind(good.BlockTimeUtc, DateTimeKind.Unspecified) },
            good with { SchemaVersion = 2 }, good with { ErrorCategory = 0 }, good with { ErrorCategory = 8 },
            good with { Reason = (ElectionEntitlementReason)99 },
            good with { Reason = ElectionEntitlementReason.AuthorityUnavailable },
            good with { Reason = ElectionEntitlementReason.CaptureUnavailable },
            good with { Reason = ElectionEntitlementReason.SemanticsUnsupported },
            good with { ErrorCategory = (int)ElectionCommandErrorCode.Conflict }
        };
        foreach (var outcome in invalid) outcome.HasSupportedSemantics().Should().BeFalse();
    }

    internal static ElectionEntitlementCapture ValidCapture() => new(
        ElectionId.NewElectionId, Guid.NewGuid(), Guid.NewGuid(), "hushvoting.veritas.500", "veritas", 1,
        500, true, "annual", 1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), "[\"no-customer-trustees\"]",
        "hushvoting-licence-catalogue/v1.0.0", new string('a', 64), 1, "admin-only", "no-customer-trustees", 500,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, 0, new DateTime(2020, 12, 31, 0, 0, 0, DateTimeKind.Utc));
}

using System.Reflection;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionRetentionLogPrivacyProofContractTests
{
    private static readonly HashSet<string> IdentityFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "OrganizationVoterId",
        "LinkedActorPublicAddress",
        "ContactValue",
        "DisplayLabel",
    };

    private static readonly HashSet<string> BallotFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PreparedBallotId",
        "PreparedBallotHash",
        "AcceptedBallotId",
        "FinalAcceptedBallotId",
        "BallotNullifier",
        "ReceiptCommitment",
        "ReceiptCommitmentScheme",
        "ReceiptSecret",
        "AcceptedBallotReference",
    };

    private static readonly HashSet<Type> IdentityRecordTypes =
    [
        typeof(ElectionRosterEntryRecord),
        typeof(ElectionParticipationRecord),
        typeof(ElectionCheckoffConsumptionRecord),
        typeof(ElectionCommitmentRegistrationRecord),
        typeof(ElectionVoterCeremonyRecord),
        typeof(ElectionSp04RestrictedCeremonyRecord),
        typeof(ElectionSp05RestrictedRosterEntryArtifactRecord),
        typeof(ElectionSp05RestrictedLinkEvidenceRecord),
        typeof(ElectionSp05RestrictedCheckoffLedgerEntryRecord),
    ];

    private static readonly HashSet<Type> BallotRecordTypes =
    [
        typeof(ElectionPreparedBallotCommitmentRecord),
        typeof(ElectionSpoiledPreparedBallotRecord),
        typeof(ElectionAcceptedBallotRecord),
        typeof(ElectionBoundReceiptRecord),
        typeof(ElectionSp04ReceiptCommitmentRecord),
        typeof(ElectionSp04RestrictedPreparedBallotRecord),
        typeof(ElectionSp04RestrictedSpoilMarkerRecord),
    ];

    public static TheoryData<Type> DurableAndExportTypes => new()
    {
        typeof(ElectionVoterCeremonyRecord),
        typeof(ElectionPreparedBallotCommitmentRecord),
        typeof(ElectionSpoiledPreparedBallotRecord),
        typeof(ElectionAcceptedBallotRecord),
        typeof(ElectionCastIdempotencyRecord),
        typeof(ElectionCheckoffConsumptionRecord),
        typeof(ElectionBoundReceiptRecord),
        typeof(ElectionSp04RestrictedCeremonyRecord),
        typeof(ElectionSp04RestrictedPreparedBallotRecord),
        typeof(ElectionSp05RestrictedCheckoffLedgerEntryRecord),
    };

    public static TheoryData<Type> LifecycleResultTypes => new()
    {
        typeof(ElectionPreparedBallotCommitmentResult),
        typeof(ElectionSpoilPreparedBallotResult),
        typeof(ElectionCastAcceptanceResult),
        typeof(ElectionCommandResult),
    };

    [Theory]
    [MemberData(nameof(DurableAndExportTypes))]
    public void DurableAndExportContracts_ShouldNotContainIdentityToBallotPairs(Type contractType)
    {
        FindForbiddenPairs(contractType).Should().BeEmpty(
            "durable and exported HushVoting evidence must not directly join voter identity to ballot artifacts");
    }

    [Theory]
    [MemberData(nameof(LifecycleResultTypes))]
    public void LifecycleResults_ShouldNotExposeIdentityToBallotPairs(Type contractType)
    {
        FindForbiddenPairs(contractType).Should().BeEmpty(
            "lifecycle result objects can be logged or serialized by callers and must stay privacy-safe");
    }

    [Fact]
    public void Scanner_ShouldDetectDeliberateForbiddenFixture()
    {
        var violations = FindForbiddenPairs(typeof(ForbiddenIdentityToBallotFixture));

        violations.Should().ContainSingle();
        violations[0].IdentityProperties.Should().Contain(nameof(ForbiddenIdentityToBallotFixture.OrganizationVoterId));
        violations[0].BallotProperties.Should().Contain(nameof(ForbiddenIdentityToBallotFixture.AcceptedBallotId));
    }

    private static IReadOnlyList<ForbiddenJoinViolation> FindForbiddenPairs(Type contractType)
    {
        var identityProperties = new List<string>();
        var ballotProperties = new List<string>();

        foreach (var property in contractType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsIdentityProperty(property))
            {
                identityProperties.Add(property.Name);
            }

            if (IsBallotProperty(property))
            {
                ballotProperties.Add(property.Name);
            }
        }

        return identityProperties.Count > 0 && ballotProperties.Count > 0
            ?
            [
                new ForbiddenJoinViolation(
                    contractType.Name,
                    identityProperties.Order(StringComparer.Ordinal).ToArray(),
                    ballotProperties.Order(StringComparer.Ordinal).ToArray()),
            ]
            : [];
    }

    private static bool IsIdentityProperty(PropertyInfo property) =>
        IdentityFieldNames.Contains(property.Name) ||
        IdentityRecordTypes.Contains(UnwrapNullable(property.PropertyType));

    private static bool IsBallotProperty(PropertyInfo property) =>
        BallotFieldNames.Contains(property.Name) ||
        BallotRecordTypes.Contains(UnwrapNullable(property.PropertyType));

    private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private sealed record ForbiddenJoinViolation(
        string ContractType,
        IReadOnlyList<string> IdentityProperties,
        IReadOnlyList<string> BallotProperties);

    private sealed record ForbiddenIdentityToBallotFixture(
        string OrganizationVoterId,
        Guid AcceptedBallotId);
}

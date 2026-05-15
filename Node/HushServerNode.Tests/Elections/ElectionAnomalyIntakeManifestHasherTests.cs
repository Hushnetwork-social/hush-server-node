using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyIntakeManifestHasherTests
{
    [Fact]
    public void ComputeHash_WithEquivalentManifestOrdering_ReturnsStableHash()
    {
        var projection = CreateProjection();
        var reordered = projection with
        {
            PackageReadinessBlockerIds = projection.PackageReadinessBlockerIds.Reverse().ToArray(),
            Threads = projection.Threads.Reverse().ToArray(),
        };

        var firstHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(projection));
        var secondHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(reordered));

        secondHash.Should().Be(firstHash);
        firstHash.Should().StartWith("sha256:");
    }

    [Fact]
    public void ComputeHash_WithAppendedRedactionChange_ReturnsDifferentHash()
    {
        var projection = CreateProjection();
        var changedRedaction = projection.Threads[0].Redactions[0] with
        {
            ReasonCodeId = ElectionAnomalyRedactionReasonIds.OperationalSafety,
        };
        var changed = projection with
        {
            Threads =
            [
                projection.Threads[0] with
                {
                    Redactions = [changedRedaction],
                },
            ],
        };

        var firstHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(projection));
        var secondHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(changed));

        secondHash.Should().NotBe(firstHash);
    }

    [Fact]
    public void ComputeHash_WithAppendedAttachmentChange_ReturnsDifferentHash()
    {
        var projection = CreateProjection();
        var appendedAttachment = projection.Threads[0].AttachmentManifests[0] with
        {
            AttachmentManifestId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            EventId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            EventHash = Sha256Ref('4'),
        };
        var changed = projection with
        {
            Threads =
            [
                projection.Threads[0] with
                {
                    AttachmentManifests =
                    [
                        .. projection.Threads[0].AttachmentManifests,
                        appendedAttachment,
                    ],
                },
            ],
        };

        var firstHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(projection));
        var secondHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(changed));

        secondHash.Should().NotBe(firstHash);
    }

    private static ElectionAnomalyEvidenceManifestProjection CreateProjection()
    {
        var electionId = ElectionId.NewElectionId;
        var threadId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var attachmentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var eventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var redactionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var sourceTransactionId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var recordedAt = new DateTime(2026, 5, 14, 12, 30, 0, DateTimeKind.Utc);

        return new ElectionAnomalyEvidenceManifestProjection(
            electionId,
            ScopeId: "package",
            ElectionAnomalyManifestCanonicalizationIds.Current,
            ManifestHash: string.Empty,
            ElectionAnomalyPackageReadinessStatusIds.Blocked,
            PackageReadinessBlockerIds:
            [
                ElectionAnomalyEvidenceScannerStatusIds.Pending,
                ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
            ],
            Threads:
            [
                new ElectionAnomalyEvidenceManifestThreadProjection(
                    threadId,
                    electionId,
                    ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
                    ElectionAnomalyCaseStateIds.Submitted,
                    "sha256:thread",
                    GovernedDecisionRef: null,
                    HasOpenClarificationRequest: false,
                    OpenClarificationRequestId: null,
                    recordedAt.AddMinutes(-10),
                    recordedAt,
                    AttachmentManifests:
                    [
                        new ElectionAnomalyAttachmentManifestProjection(
                            attachmentId,
                            threadId,
                            eventId,
                            Sha256Ref('e'),
                            ElectionAnomalyAttachmentKindIds.AuthorityEvidence,
                            ElectionAnomalyRestrictedPayloadReferences.Create(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
                            Sha256Ref('a'),
                            Sha256Ref('b'),
                            1024,
                            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
                            ElectionAnomalyAttachmentValidationStatusIds.PendingScan,
                            ElectionAnomalyEvidenceScannerStatusIds.Pending,
                            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
                            ClarificationRequestId: null,
                            ElectionAnomalyRecipientRoleIds.ElectionOwner,
                            recordedAt,
                            sourceTransactionId),
                    ],
                    Redactions:
                    [
                        new ElectionAnomalyEvidenceRedactionProjection(
                            redactionId,
                            threadId,
                            Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Sha256Ref('f'),
                            ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
                            attachmentId.ToString(),
                            ElectionAnomalyRedactionReasonIds.PersonalData,
                            Sha256Ref('b'),
                            ReplacementManifestHash: null,
                            TombstoneStatusId: "redacted",
                            recordedAt.AddMinutes(1),
                            Guid.Parse("22222222-2222-2222-2222-222222222222")),
                    ],
                    RecipientWraps:
                    [
                        new ElectionAnomalyRestrictedRecipientWrapProjection(
                            ElectionAnomalyRecipientRoleIds.ElectionOwner,
                            ElectionAnomalyRecipientWrapStatusIds.Available),
                    ]),
            ]);
    }

    private static string Sha256Ref(char fill) =>
        $"sha256:{new string(fill, 64)}";
}

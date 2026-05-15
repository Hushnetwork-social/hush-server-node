using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyPersistenceTests
{
    [Fact]
    public void AnomalyModel_ShouldDefineUniquenessAndAppendOnlyIndexes()
    {
        using var context = CreateContext();

        FindIndex(context, typeof(ElectionAnomalyThreadRecord), "ElectionId", "SubmitterPersonScopeId")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyThreadEventRecord), "AnomalyThreadId", "Sequence")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyThreadEventRecord), "EventHash")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyActionRecord), "SourceTransactionId")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyAttachmentManifestRecord), "EventId")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyAttachmentManifestRecord), "EncryptedPayloadReference")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyEvidenceRedactionRecord), "EventId")
            .IsUnique
            .Should()
            .BeTrue();
        FindIndex(context, typeof(ElectionAnomalyRestrictedPayloadRecord), "PayloadReference")
            .IsUnique
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task SaveAnomalyThreadWithInitialEvent_ShouldRoundTripEvidence()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var now = DateTime.UtcNow;
        var threadId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var threadEvent = CreateEvent(
            eventId,
            threadId,
            electionId,
            sequence: 1,
            previousEventHash: null,
            payloadJson: """
                {
                  "bodyHash": "body-hash",
                  "categoryId": "trustee_continuity_anomaly"
                }
                """,
            now);
        threadEvent = threadEvent with
        {
            EventHash = ElectionAnomalyEventHasher.ComputeEventHash(threadEvent),
        };
        var thread = new ElectionAnomalyThreadRecord(
            threadId,
            electionId,
            "person-scope-1",
            ElectionAnomalyPersonScopeDerivationVersions.Current,
            "submitter-address",
            ElectionAnomalyActorRoleContextIds.Trustee,
            ElectionAnomalyRoleEvidenceTypeIds.TrusteeInvitation,
            "trustee-invitation:trustee-1",
            ElectionLifecycleState.Open,
            now.AddDays(3),
            ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            ElectionAnomalyCaseStateIds.Submitted,
            SeverityCandidateId: null,
            GovernedDecisionRef: null,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            now,
            now,
            threadEvent.SourceTransactionId,
            SourceBlockHeight: 7,
            SourceBlockId: Guid.NewGuid(),
            ElectionAnomalyEventHasher.ComputeThreadHash([threadEvent]));
        var message = new ElectionAnomalyMessageEnvelopeRecord(
            messageId,
            threadId,
            eventId,
            electionId,
            ElectionAnomalyMessageKindIds.InitialSubmission,
            "encrypted-body",
            "sha256:encrypted-body",
            "sha256:plain-body",
            42,
            "test-encryption",
            now);
        var wrap = new ElectionAnomalyRecipientWrapRecord(
            Guid.NewGuid(),
            messageId,
            threadId,
            electionId,
            ElectionAnomalyRecipientRoleIds.ElectionOwner,
            "owner-address",
            "owner-key-fingerprint",
            "encrypted-content-key",
            "test-wrap",
            ElectionAnomalyRecipientWrapStatusIds.Available,
            now);

        await repository.SaveAnomalyThreadWithInitialEventAsync(thread, threadEvent, message, [wrap]);
        await context.SaveChangesAsync();

        var savedThread = await repository.GetAnomalyThreadByPersonScopeAsync(electionId, "person-scope-1");
        var savedEvents = await repository.GetAnomalyThreadEventsAsync(threadId);
        var latestEvent = await repository.GetLatestAnomalyThreadEventAsync(threadId);

        savedThread.Should().NotBeNull();
        savedThread!.CurrentThreadHash.Should().Be(thread.CurrentThreadHash);
        savedEvents.Should().ContainSingle();
        savedEvents[0].EventHash.Should().Be(threadEvent.EventHash);
        latestEvent!.Id.Should().Be(eventId);
        context.ElectionAnomalyMessageEnvelopes.Single().EncryptedBodyHash.Should().Be("sha256:encrypted-body");
        context.ElectionAnomalyRecipientWraps.Single().RecipientRoleId.Should().Be(ElectionAnomalyRecipientRoleIds.ElectionOwner);
    }

    [Fact]
    public async Task AnomalyEvidenceRecords_ShouldRoundTripManifestRedactionAndRestrictedPayload()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var threadId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payloadId = Guid.NewGuid();
        var payloadReference = ElectionAnomalyRestrictedPayloadReferences.Create(payloadId);
        var now = DateTime.UtcNow;
        var manifest = new ElectionAnomalyAttachmentManifestRecord(
            Guid.NewGuid(),
            threadId,
            eventId,
            Sha256Ref('e'),
            electionId,
            ElectionAnomalyAttachmentKindIds.AuthorityEvidence,
            payloadReference,
            Sha256Ref('a'),
            Sha256Ref('b'),
            1024,
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            ElectionAnomalyAttachmentValidationStatusIds.Accepted,
            ElectionAnomalyEvidenceScannerStatusIds.Clear,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            ClarificationRequestId: null,
            "owner-address",
            ElectionAnomalyRecipientRoleIds.ElectionOwner,
            Guid.NewGuid(),
            SourceBlockHeight: 7,
            SourceBlockId: Guid.NewGuid(),
            now);
        var redaction = new ElectionAnomalyEvidenceRedactionRecord(
            Guid.NewGuid(),
            threadId,
            Guid.NewGuid(),
            Sha256Ref('f'),
            electionId,
            ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
            manifest.Id.ToString(),
            ElectionAnomalyRedactionReasonIds.PersonalData,
            manifest.ContentHash,
            ReplacementManifestHash: null,
            TombstoneStatusId: "redacted",
            HoldReference: null,
            "owner-address",
            Guid.NewGuid(),
            SourceBlockHeight: 8,
            SourceBlockId: Guid.NewGuid(),
            now.AddMinutes(1));
        var payload = new ElectionAnomalyRestrictedPayloadRecord(
            payloadId,
            electionId,
            threadId,
            payloadReference,
            [1, 2, 3],
            manifest.EncryptedPayloadHash,
            manifest.ContentHash,
            manifest.SizeBytes,
            manifest.MimeType,
            manifest.ScannerStatusId,
            manifest.PayloadAvailabilityStatusId,
            now);

        await repository.SaveAnomalyAttachmentManifestAsync(manifest);
        await repository.SaveAnomalyEvidenceRedactionAsync(redaction);
        await repository.SaveAnomalyRestrictedPayloadAsync(payload);
        await context.SaveChangesAsync();

        var savedManifests = await repository.GetAnomalyAttachmentManifestsAsync(threadId);
        var savedElectionManifests = await repository.GetAnomalyAttachmentManifestsForElectionAsync(electionId);
        var savedRedactions = await repository.GetAnomalyEvidenceRedactionsAsync(threadId);
        var savedElectionRedactions = await repository.GetAnomalyEvidenceRedactionsForElectionAsync(electionId);
        var savedPayload = await repository.GetAnomalyRestrictedPayloadAsync(payloadReference);

        savedManifests.Should().ContainSingle();
        savedElectionManifests.Should().ContainSingle();
        savedManifests[0].EncryptedPayloadReference.Should().Be(payloadReference);
        savedRedactions.Should().ContainSingle();
        savedElectionRedactions.Should().ContainSingle();
        savedRedactions[0].OriginalHash.Should().Be(manifest.ContentHash);
        savedPayload.Should().NotBeNull();
        savedPayload!.PayloadReference.Should().Be(payloadReference);
        savedPayload.EncryptedPayload.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void EventHasher_ShouldBeCanonicalAndTamperSensitive()
    {
        var eventId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var electionId = ElectionId.NewElectionId;
        var now = new DateTime(2026, 5, 13, 14, 0, 0, DateTimeKind.Utc);
        var first = CreateEvent(
            eventId,
            threadId,
            electionId,
            sequence: 1,
            previousEventHash: null,
            payloadJson: """{ "categoryId": "trustee_continuity_anomaly", "bodyHash": "body" }""",
            now);
        var equivalent = first with
        {
            EventPayloadJson = """{ "bodyHash": "body", "categoryId": "trustee_continuity_anomaly" }""",
        };
        var changed = first with
        {
            EventPayloadJson = """{ "categoryId": "counting_or_tally_anomaly", "bodyHash": "body" }""",
        };

        var firstHash = ElectionAnomalyEventHasher.ComputeEventHash(first);

        ElectionAnomalyEventHasher.ComputeEventHash(equivalent).Should().Be(firstHash);
        ElectionAnomalyEventHasher.ComputeEventHash(changed).Should().NotBe(firstHash);

        var second = CreateEvent(
            Guid.NewGuid(),
            threadId,
            electionId,
            sequence: 2,
            previousEventHash: firstHash,
            payloadJson: """{ "caseStateId": "under_review" }""",
            now.AddMinutes(1));
        second = second with
        {
            EventHash = ElectionAnomalyEventHasher.ComputeEventHash(second),
        };
        var secondWithChangedPrevious = second with
        {
            PreviousEventHash = ElectionAnomalyEventHasher.ComputeEventHash(changed),
        };

        ElectionAnomalyEventHasher.ComputeEventHash(secondWithChangedPrevious).Should().NotBe(second.EventHash);
        ElectionAnomalyEventHasher.ComputeThreadHash([second, first with { EventHash = firstHash }])
            .Should()
            .Be(ElectionAnomalyEventHasher.ComputeThreadHash([first with { EventHash = firstHash }, second]));
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IIndex FindIndex(
        ElectionsDbContext context,
        Type entityType,
        params string[] propertyNames)
    {
        var entity = context.Model.FindEntityType(entityType);

        entity.Should().NotBeNull();

        return entity!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static ElectionAnomalyThreadEventRecord CreateEvent(
        Guid eventId,
        Guid threadId,
        ElectionId electionId,
        int sequence,
        string? previousEventHash,
        string payloadJson,
        DateTime occurredAt) =>
        new(
            eventId,
            threadId,
            electionId,
            sequence,
            ElectionAnomalyEventTypeIds.ThreadSubmitted,
            payloadJson,
            EventHash: string.Empty,
            previousEventHash,
            ActionNonce: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            SourceTransactionId: Guid.Parse($"bbbbbbbb-bbbb-bbbb-bbbb-{sequence:000000000000}"),
            SourceBlockHeight: 7,
            SourceBlockId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ActorPublicAddress: "submitter-address",
            occurredAt);

    private static string Sha256Ref(char fill) =>
        $"sha256:{new string(fill, 64)}";

    private static ElectionsRepository CreateRepository(ElectionsDbContext context)
    {
        var repository = new ElectionsRepository();
        repository.SetContext(context);
        return repository;
    }

    private static ElectionsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ElectionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ElectionsDbContext(new ElectionsDbContextConfigurator(), options);
    }
}

using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Elections.gRPC;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using System.Text.Json;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-123")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionAnomalyIntegrationTests : IAsyncLifetime
{
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await DisposeNodeAsync();

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task SignedAnomalyTransactions_CreateThreadAndRestrictedProjections()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-123 Anomaly Integration");
        (await SubmitBlockchainTransactionAsync(TestTransactionFactory.CreateIdentityRegistration(TestIdentities.Charlie)))
            .Successfull
            .Should()
            .BeTrue();
        var (inviteTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
            TestIdentities.Alice,
            electionId,
            TestIdentities.Charlie);
        var inviteResponse = await SubmitBlockchainTransactionAsync(inviteTransaction);
        inviteResponse.Successfull.Should().BeTrue(inviteResponse.Message);
        var acceptResponse = await SubmitBlockchainTransactionAsync(TestTransactionFactory.AcceptElectionTrusteeInvitation(
            TestIdentities.Charlie,
            electionId,
            invitationId));
        acceptResponse.Successfull.Should().BeTrue(acceptResponse.Message);

        var (submissionTransaction, anomalyThreadId) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        var submitResponse = await SubmitBlockchainTransactionAsync(submissionTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var duplicateTransaction = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId).Transaction;
        var duplicateResponse = await SubmitBlockchainTransactionAsync(duplicateTransaction);
        duplicateResponse.Successfull.Should().BeFalse("one person can submit only one anomaly thread per election");

        var (requestTransaction, clarificationRequestId) = TestTransactionFactory.RequestElectionAnomalyInformation(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId,
            anomalyThreadId);
        (await SubmitBlockchainTransactionAsync(requestTransaction)).Successfull.Should().BeTrue();

        var clarificationTransaction = TestTransactionFactory.SubmitElectionAnomalyInformation(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId,
            anomalyThreadId,
            clarificationRequestId);
        (await SubmitBlockchainTransactionAsync(clarificationTransaction)).Successfull.Should().BeTrue();

        var authorityResponseTransaction = TestTransactionFactory.RecordElectionAnomalyAuthorityResponse(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId,
            anomalyThreadId);
        (await SubmitBlockchainTransactionAsync(authorityResponseTransaction)).Successfull.Should().BeTrue();

        var grantTransaction = TestTransactionFactory.CreateElectionReportAccessGrant(
            TestIdentities.Alice,
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        (await SubmitBlockchainTransactionAsync(grantTransaction)).Successfull.Should().BeTrue();

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var threads = await dbContext.Set<ElectionAnomalyThreadRecord>()
            .Where(x => x.ElectionId == electionId)
            .ToListAsync();
        threads.Should().ContainSingle();
        threads[0].Id.Should().Be(anomalyThreadId);

        var messages = await dbContext.Set<ElectionAnomalyMessageEnvelopeRecord>()
            .Where(x => x.AnomalyThreadId == anomalyThreadId)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();
        messages.Should().HaveCount(4);

        var wraps = await dbContext.Set<ElectionAnomalyRecipientWrapRecord>()
            .Where(x => x.AnomalyThreadId == anomalyThreadId)
            .ToListAsync();
        foreach (var message in messages)
        {
            wraps.Where(x => x.MessageEnvelopeId == message.Id)
                .Select(x => x.RecipientRoleId)
                .Should()
                .Contain([
                    ElectionAnomalyRecipientRoleIds.Submitter,
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                ]);
        }

        wraps.Where(x => x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.DesignatedAuditor)
            .Should()
            .HaveCount(messages.Count)
            .And.OnlyContain(x => x.WrapStatusId == ElectionAnomalyRecipientWrapStatusIds.PendingBackfill);

        var queryService = scope.ServiceProvider.GetRequiredService<IElectionQueryApplicationService>();
        var ownProjection = await queryService.GetElectionAnomalyOwnThreadAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress);
        ownProjection.Should().NotBeNull();
        ownProjection!.Messages.Should().HaveCount(4);
        ownProjection.Messages.SelectMany(x => x.RecipientWraps)
            .Where(x => x.RecipientPublicAddress == TestIdentities.Alice.PublicSigningAddress)
            .Should()
            .OnlyContain(x => !string.IsNullOrWhiteSpace(x.EncryptedContentKey) &&
                              !string.IsNullOrWhiteSpace(x.WrapAlgorithm));
        ownProjection.Messages.SelectMany(x => x.RecipientWraps)
            .Where(x => x.RecipientPublicAddress != TestIdentities.Alice.PublicSigningAddress)
            .Should()
            .OnlyContain(x => string.IsNullOrWhiteSpace(x.EncryptedContentKey) &&
                              string.IsNullOrWhiteSpace(x.WrapAlgorithm));

        var electionsClient = _grpcFactory!.CreateClient<HushElections.HushElectionsClient>();
        var grpcOwnThread = await electionsClient.GetElectionAnomalyOwnThreadAsync(
            new GetElectionAnomalyOwnThreadRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            },
            headers: CreateSignedElectionQueryHeaders(
                "GetElectionAnomalyOwnThread",
                TestIdentities.Alice,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestIdentities.Alice.PublicSigningAddress,
                }));

        grpcOwnThread.Success.Should().BeTrue();
        grpcOwnThread.HasThread.Should().BeTrue();
        grpcOwnThread.Thread.Messages.Should().HaveCount(4);
        grpcOwnThread.Thread.Messages.SelectMany(x => x.RecipientWraps)
            .Where(x => x.RecipientPublicAddress != TestIdentities.Alice.PublicSigningAddress)
            .Should()
            .OnlyContain(x => string.IsNullOrWhiteSpace(x.EncryptedContentKey));

        var peerProjection = await queryService.GetElectionAnomalyOwnThreadAsync(
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        peerProjection.Should().BeNull();

        var ownerTriage = await queryService.GetElectionAnomalyOwnerTriageAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress);
        ownerTriage.Should().NotBeNull();
        ownerTriage!.Threads.Should().ContainSingle();
        ownerTriage.Threads[0].SubmitterActorPublicAddress.Should().Be(TestIdentities.Alice.PublicSigningAddress);
        ownerTriage.Threads[0].Messages.Should().OnlyContain(x =>
            x.CallerOwnerWrap != null &&
            !string.IsNullOrWhiteSpace(x.CallerOwnerWrap.EncryptedContentKey));

        var grpcOwnerTriage = await electionsClient.GetElectionAnomalyOwnerTriageAsync(
            new GetElectionAnomalyOwnerTriageRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            },
            headers: CreateSignedElectionQueryHeaders(
                "GetElectionAnomalyOwnerTriage",
                TestIdentities.Alice,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestIdentities.Alice.PublicSigningAddress,
                }));

        grpcOwnerTriage.Success.Should().BeTrue();
        grpcOwnerTriage.HasTriage.Should().BeTrue();
        grpcOwnerTriage.Triage.TotalThreadCount.Should().Be(1);
        grpcOwnerTriage.Triage.Threads[0].SubmitterActorPublicAddress.Should().Be(TestIdentities.Alice.PublicSigningAddress);
        grpcOwnerTriage.Triage.Threads[0].Messages.Should().OnlyContain(x =>
            x.HasCallerOwnerWrap &&
            !string.IsNullOrWhiteSpace(x.CallerOwnerWrap.EncryptedContentKey));

        var trusteeCounts = await queryService.GetElectionAnomalyTrusteeCountsAsync(
            electionId,
            TestIdentities.Charlie.PublicSigningAddress);
        trusteeCounts.Should().NotBeNull();
        trusteeCounts!.TotalThreadCount.Should().Be(1);
        trusteeCounts.ContinuitySummary.TrusteeContinuityThreadCount.Should().Be(1);
        trusteeCounts.ContinuitySummary.OpenContinuityThreadCount.Should().Be(1);
        trusteeCounts.ContinuitySummary.HasContinuityIssue.Should().BeTrue();
        trusteeCounts.GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));

        var grpcTrusteeCounts = await electionsClient.GetElectionAnomalyTrusteeCountsAsync(
            new GetElectionAnomalyTrusteeCountsRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestIdentities.Charlie.PublicSigningAddress,
            },
            headers: CreateSignedElectionQueryHeaders(
                "GetElectionAnomalyTrusteeCounts",
                TestIdentities.Charlie,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestIdentities.Charlie.PublicSigningAddress,
                }));

        grpcTrusteeCounts.Success.Should().BeTrue();
        grpcTrusteeCounts.HasCounts.Should().BeTrue();
        grpcTrusteeCounts.Counts.TotalThreadCount.Should().Be(1);
        grpcTrusteeCounts.Counts.CategoryCounts.Should().ContainSingle(x =>
            x.CategoryId == ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly &&
            x.Count == 1);
        grpcTrusteeCounts.Counts.ContinuitySummary.HasContinuityIssue.Should().BeTrue();
        grpcTrusteeCounts.Counts.ContinuitySummary.OpenContinuityThreadCount.Should().Be(1);

        var grpcVoterTrusteeCounts = await electionsClient.GetElectionAnomalyTrusteeCountsAsync(
            new GetElectionAnomalyTrusteeCountsRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
            },
            headers: CreateSignedElectionQueryHeaders(
                "GetElectionAnomalyTrusteeCounts",
                TestIdentities.Alice,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestIdentities.Alice.PublicSigningAddress,
                }));

        grpcVoterTrusteeCounts.Success.Should().BeFalse();
        grpcVoterTrusteeCounts.HasCounts.Should().BeFalse();

        var trusteeRestrictedReview = await queryService.GetElectionAnomalyAuditorRestrictedReviewAsync(
            electionId,
            TestIdentities.Charlie.PublicSigningAddress);
        trusteeRestrictedReview.Should().BeNull();

        var auditorReview = await queryService.GetElectionAnomalyAuditorRestrictedReviewAsync(
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        auditorReview.Should().NotBeNull();
        auditorReview!.Threads.Should().ContainSingle();
        auditorReview.Threads[0].Messages.Should().HaveCount(4);
        auditorReview.Threads[0].Messages
            .Should()
            .AllSatisfy(x =>
            {
                x.CallerAuditorWrap.Should().NotBeNull();
                x.CallerAuditorWrap!.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.PendingBackfill);
                x.CallerAuditorWrap.EncryptedContentKey.Should().BeNull();
            });
        auditorReview.Threads[0].Messages[0].RecipientWraps
            .Should()
            .OnlyContain(x => !string.IsNullOrWhiteSpace(x.RecipientRoleId));

        var grpcAuditorReview = await electionsClient.GetElectionAnomalyAuditorRestrictedReviewAsync(
            new GetElectionAnomalyAuditorRestrictedReviewRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestIdentities.Bob.PublicSigningAddress,
            },
            headers: CreateSignedElectionQueryHeaders(
                "GetElectionAnomalyAuditorRestrictedReview",
                TestIdentities.Bob,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestIdentities.Bob.PublicSigningAddress,
                }));

        grpcAuditorReview.Success.Should().BeTrue();
        grpcAuditorReview.HasReview.Should().BeTrue();
        grpcAuditorReview.Review.TotalThreadCount.Should().Be(1);
        grpcAuditorReview.Review.PendingRewrapMessageCount.Should().Be(messages.Count);
        grpcAuditorReview.Review.DecryptableMessageCount.Should().Be(0);
        grpcAuditorReview.Review.Threads[0].Messages
            .Should()
            .AllSatisfy(x =>
            {
                x.HasCallerAuditorWrap.Should().BeTrue();
                x.CallerAuditorWrap.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.PendingBackfill);
                x.CallerAuditorWrap.EncryptedContentKey.Should().BeEmpty();
            });
    }

    [Fact]
    [Trait("Category", "FEAT-128")]
    [Trait("Category", "TwinTest")]
    public async Task SignedAnomalyEvidenceManifestAndRedaction_ProjectDeterministicRestrictedManifest()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-128 Evidence Manifest TwinTest");
        var (submissionTransaction, anomalyThreadId) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        (await SubmitBlockchainTransactionAsync(submissionTransaction)).Successfull.Should().BeTrue();

        var (
            attachmentTransaction,
            attachmentManifestId,
            encryptedPayloadReference,
            contentHash) = TestTransactionFactory.RecordElectionAnomalyAuthorityAttachmentManifest(
            TestIdentities.Alice,
            electionId,
            anomalyThreadId);
        var attachmentResponse = await SubmitBlockchainTransactionAsync(attachmentTransaction);
        attachmentResponse.Successfull.Should().BeTrue(attachmentResponse.Message);

        var (redactionTransaction, redactionEventId) = TestTransactionFactory.RecordElectionAnomalyEvidenceRedaction(
            TestIdentities.Alice,
            electionId,
            anomalyThreadId,
            attachmentManifestId,
            contentHash);
        var redactionResponse = await SubmitBlockchainTransactionAsync(redactionTransaction);
        redactionResponse.Successfull.Should().BeTrue(redactionResponse.Message);

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var storedAttachment = await dbContext.Set<ElectionAnomalyAttachmentManifestRecord>()
            .SingleAsync(x => x.Id == attachmentManifestId);
        storedAttachment.AnomalyThreadId.Should().Be(anomalyThreadId);
        storedAttachment.AttachmentKindId.Should().Be(ElectionAnomalyAttachmentKindIds.AuthorityEvidence);
        storedAttachment.EncryptedPayloadReference.Should().Be(encryptedPayloadReference);
        storedAttachment.ContentHash.Should().Be(contentHash);
        storedAttachment.ValidationStatusId.Should().Be(ElectionAnomalyAttachmentValidationStatusIds.Accepted);
        storedAttachment.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Clear);
        storedAttachment.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Available);

        var storedRedaction = await dbContext.Set<ElectionAnomalyEvidenceRedactionRecord>()
            .SingleAsync(x => x.Id == redactionEventId);
        storedRedaction.AnomalyThreadId.Should().Be(anomalyThreadId);
        storedRedaction.TargetKindId.Should().Be(ElectionAnomalyRedactionTargetKindIds.AttachmentManifest);
        storedRedaction.TargetId.Should().Be(attachmentManifestId.ToString("D"));
        storedRedaction.ReasonCodeId.Should().Be(ElectionAnomalyRedactionReasonIds.PersonalData);
        storedRedaction.OriginalHash.Should().Be(contentHash);
        storedRedaction.TombstoneStatusId.Should().Be("redacted");

        var queryService = scope.ServiceProvider.GetRequiredService<IElectionQueryApplicationService>();
        var ownerManifest = await queryService.GetElectionAnomalyEvidenceManifestAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress,
            ElectionAnomalyEvidenceManifestScopeIds.Owner);
        ownerManifest.Should().NotBeNull();
        ownerManifest!.ScopeId.Should().Be(ElectionAnomalyEvidenceManifestScopeIds.Owner);
        ownerManifest.CanonicalizationId.Should().Be(ElectionAnomalyManifestCanonicalizationIds.Current);
        ownerManifest.PackageReadinessStatusId.Should().Be(ElectionAnomalyPackageReadinessStatusIds.Ready);
        ownerManifest.PackageReadinessBlockerIds.Should().BeEmpty();
        ownerManifest.ManifestHash.Should().Be(ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(ownerManifest)));

        var manifestThread = ownerManifest.Threads.Should().ContainSingle().Subject;
        manifestThread.AnomalyThreadId.Should().Be(anomalyThreadId);
        var projectedAttachment = manifestThread.AttachmentManifests.Should().ContainSingle().Subject;
        projectedAttachment.AttachmentManifestId.Should().Be(attachmentManifestId);
        projectedAttachment.EncryptedPayloadReference.Should().Be(encryptedPayloadReference);
        projectedAttachment.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Clear);
        projectedAttachment.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Available);
        var projectedRedaction = manifestThread.Redactions.Should().ContainSingle().Subject;
        projectedRedaction.RedactionEventId.Should().Be(redactionEventId);
        projectedRedaction.TargetId.Should().Be(attachmentManifestId.ToString("D"));
        projectedRedaction.OriginalHash.Should().Be(contentHash);
        projectedRedaction.TombstoneStatusId.Should().Be("redacted");

        var packageManifest = await queryService.GetElectionAnomalyEvidenceManifestAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress,
            ElectionAnomalyEvidenceManifestScopeIds.Package);
        packageManifest.Should().NotBeNull();
        packageManifest!.ManifestHash.Should().Be(ElectionAnomalyIntakeManifestHasher.ComputeHash(
            ElectionAnomalyIntakeManifestHasher.FromProjection(packageManifest)));
        packageManifest.PackageReadinessStatusId.Should().Be(ElectionAnomalyPackageReadinessStatusIds.Ready);
        packageManifest.Threads.Should().ContainSingle().Subject.AttachmentManifests
            .Should()
            .ContainSingle(x => x.AttachmentManifestId == attachmentManifestId);

        var electionsClient = _grpcFactory!.CreateClient<HushElections.HushElectionsClient>();
        var grpcManifest = await electionsClient.GetElectionAnomalyEvidenceManifestAsync(
            new GetElectionAnomalyEvidenceManifestRequest
            {
                ElectionId = electionId.ToString(),
                ActorPublicAddress = TestIdentities.Alice.PublicSigningAddress,
                ScopeId = ElectionAnomalyEvidenceManifestScopeIds.Owner,
            },
            headers: CreateSignedElectionQueryHeaders(
                "GetElectionAnomalyEvidenceManifest",
                TestIdentities.Alice,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId.ToString(),
                    ["ActorPublicAddress"] = TestIdentities.Alice.PublicSigningAddress,
                    ["ScopeId"] = ElectionAnomalyEvidenceManifestScopeIds.Owner,
                }));

        grpcManifest.Success.Should().BeTrue(grpcManifest.ErrorMessage);
        grpcManifest.HasManifest.Should().BeTrue();
        grpcManifest.Manifest.ManifestHash.Should().Be(ownerManifest.ManifestHash);
        grpcManifest.Manifest.PackageReadinessStatusId.Should().Be(ElectionAnomalyPackageReadinessStatusIds.Ready);
        grpcManifest.Manifest.PackageReadinessBlockerIds.Should().BeEmpty();
        grpcManifest.Manifest.AttachmentManifestCount.Should().Be(1);
        grpcManifest.Manifest.RedactionCount.Should().Be(1);
        grpcManifest.Manifest.Threads.Should().ContainSingle();
        grpcManifest.Manifest.Threads[0].AttachmentManifests[0].EncryptedPayloadReference
            .Should()
            .Be(encryptedPayloadReference);
        grpcManifest.Manifest.Threads[0].Redactions[0].OriginalHash.Should().Be(contentHash);
    }

    [Fact]
    [Trait("Category", "FEAT-127")]
    public async Task SignedAnomalyAuditorRecipientRewrap_UpdatesAuditorRestrictedProjection()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-127 Anomaly Auditor Rewrap");
        var (submissionTransaction, anomalyThreadId) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        (await SubmitBlockchainTransactionAsync(submissionTransaction)).Successfull.Should().BeTrue();

        var grantTransaction = TestTransactionFactory.CreateElectionReportAccessGrant(
            TestIdentities.Alice,
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        (await SubmitBlockchainTransactionAsync(grantTransaction)).Successfull.Should().BeTrue();

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var message = await dbContext.Set<ElectionAnomalyMessageEnvelopeRecord>()
            .SingleAsync(x => x.AnomalyThreadId == anomalyThreadId);
        var pendingWrap = await dbContext.Set<ElectionAnomalyRecipientWrapRecord>()
            .SingleAsync(x =>
                x.AnomalyThreadId == anomalyThreadId &&
                x.MessageEnvelopeId == message.Id &&
                x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.DesignatedAuditor &&
                x.RecipientPublicAddress == TestIdentities.Bob.PublicSigningAddress);
        pendingWrap.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.PendingBackfill);

        var rewrapTransaction = TestTransactionFactory.RecordElectionAnomalyAuditorRecipientRewrap(
            TestIdentities.Alice,
            TestIdentities.Bob,
            electionId,
            anomalyThreadId,
            message.Id);
        var rewrapResponse = await SubmitBlockchainTransactionAsync(rewrapTransaction);
        rewrapResponse.Successfull.Should().BeTrue(rewrapResponse.Message);

        dbContext.ChangeTracker.Clear();
        var availableWrap = await dbContext.Set<ElectionAnomalyRecipientWrapRecord>()
            .SingleAsync(x =>
                x.AnomalyThreadId == anomalyThreadId &&
                x.MessageEnvelopeId == message.Id &&
                x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.DesignatedAuditor &&
                x.RecipientPublicAddress == TestIdentities.Bob.PublicSigningAddress);
        availableWrap.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.Available);
        availableWrap.EncryptedContentKey.Should().NotBeNullOrWhiteSpace();

        var queryService = scope.ServiceProvider.GetRequiredService<IElectionQueryApplicationService>();
        var ownerTriage = await queryService.GetElectionAnomalyOwnerTriageAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress);
        ownerTriage.Should().NotBeNull();
        ownerTriage!.PendingRewrapMessageCount.Should().Be(0);

        var auditorReview = await queryService.GetElectionAnomalyAuditorRestrictedReviewAsync(
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        auditorReview.Should().NotBeNull();
        var auditorMessage = auditorReview!.Threads.Should().ContainSingle().Subject.Messages.Should().ContainSingle().Subject;
        auditorMessage.CallerAuditorWrap.Should().NotBeNull();
        auditorMessage.CallerAuditorWrap!.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.Available);
        auditorMessage.CallerAuditorWrap.EncryptedContentKey.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "FEAT-127")]
    public async Task SignedExternalClaimantRegistration_WithDuplicateReference_IsRejectedAndKeepsSingleThread()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-127 External Claimant Duplicate");
        const string externalClaimantReferenceHash = "sha256:integration-external-claimant-reference";
        var (firstTransaction, firstThreadId) = TestTransactionFactory.RegisterExternalElectionAnomalyClaimant(
            TestIdentities.Alice,
            electionId,
            externalClaimantReferenceHash);
        var firstResponse = await SubmitBlockchainTransactionAsync(firstTransaction);
        firstResponse.Successfull.Should().BeTrue(firstResponse.Message);

        var duplicateTransaction = TestTransactionFactory.RegisterExternalElectionAnomalyClaimant(
            TestIdentities.Alice,
            electionId,
            externalClaimantReferenceHash).Transaction;
        var duplicateResponse = await SubmitBlockchainTransactionAsync(duplicateTransaction);
        duplicateResponse.Successfull.Should().BeFalse("one external claimant reference maps to one anomaly thread");
        duplicateResponse.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.DuplicateThread);

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var threads = await dbContext.Set<ElectionAnomalyThreadRecord>()
            .Where(x => x.ElectionId == electionId)
            .ToListAsync();
        threads.Should().ContainSingle();
        var thread = threads.Single();
        thread.Id.Should().Be(firstThreadId);
        thread.CurrentCategoryId.Should().Be(ElectionAnomalyCategoryIds.ExternalObjectionOrComplaint);
        thread.SubmitterRoleContextId.Should().Be(ElectionAnomalyActorRoleContextIds.ExternalClaimantRegistrar);
        thread.SubmitterRoleEvidenceTypeId.Should().Be(ElectionAnomalyRoleEvidenceTypeIds.ExternalClaimantBridge);
        thread.SubmitterRoleEvidenceReference.Should().Be($"external-claimant:{externalClaimantReferenceHash}");

        var ownerTriage = await scope.ServiceProvider
            .GetRequiredService<IElectionQueryApplicationService>()
            .GetElectionAnomalyOwnerTriageAsync(electionId, TestIdentities.Alice.PublicSigningAddress);
        ownerTriage.Should().NotBeNull();
        ownerTriage!.ExternalClaimantThreadCount.Should().Be(1);
        ownerTriage.Threads.Should().ContainSingle(x =>
            x.CategoryId == ElectionAnomalyCategoryIds.ExternalObjectionOrComplaint &&
            x.SubmitterRoleContextId == ElectionAnomalyActorRoleContextIds.ExternalClaimantRegistrar);
    }

    [Fact]
    public async Task SignedAnomalySubmission_WithClosedSubmissionWindow_IsRejected()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-123 Closed Anomaly Window");

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var election = await dbContext.Set<ElectionRecord>()
                .AsNoTracking()
                .SingleAsync(x => x.ElectionId == electionId);
            dbContext.Update(election with { AnomalySubmissionWindowClosesAt = DateTime.UtcNow.AddMinutes(-1) });
            await dbContext.SaveChangesAsync();
        }

        var (submissionTransaction, _) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        var submitResponse = await SubmitBlockchainTransactionAsync(submissionTransaction);
        submitResponse.Successfull.Should().BeFalse("the anomaly submission window is closed");

        await using var verificationScope = _node!.Services.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var threadCount = await verificationDbContext.Set<ElectionAnomalyThreadRecord>()
            .CountAsync(x => x.ElectionId == electionId);
        threadCount.Should().Be(0);
    }

    private async Task StartNodeAsync()
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();
        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync();
    }

    private async Task DisposeNodeAsync()
    {
        _grpcFactory?.Dispose();
        _grpcFactory = null;
        _blockControl = null;

        if (_node is not null)
        {
            await _node.DisposeAsync();
            _node = null;
        }
    }

    private async Task<ElectionId> CreateDraftElectionAsync(string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-123 anomaly integration draft",
            CreateDraftSpecification(title));
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
        return electionId;
    }

    private async Task<SubmitSignedTransactionReply> SubmitBlockchainTransactionAsync(string signedTransaction)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        using var waiter = _node!.StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (submitResponse.Successfull)
        {
            await waiter.WaitAsync();
            await _blockControl!.ProduceBlockAsync();
        }

        return submitResponse;
    }

    private static Metadata CreateSignedElectionQueryHeaders(
        string method,
        TestIdentity actor,
        IReadOnlyDictionary<string, object?> request)
    {
        var signedAt = DateTimeOffset.UtcNow.ToString("O");
        var payload = BuildSignedPayload(method, actor.PublicSigningAddress, signedAt, request);
        return new Metadata
        {
            { "x-hush-election-query-signatory", actor.PublicSigningAddress },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, actor.PrivateSigningKey) },
        };
    }

    private static string BuildSignedPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = request.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static ElectionDraftSpecification CreateDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Anomaly integration validation",
            ExternalReferenceCode: "FEAT-123",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            SelectedProfileId: "dkg-prod-3of5",
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3);
}

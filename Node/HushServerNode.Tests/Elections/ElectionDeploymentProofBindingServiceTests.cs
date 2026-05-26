using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionDeploymentProofBindingServiceTests
{
    [Fact]
    public async Task BindForOpenAsync_WithAcceptedProductionProof_PersistsLedgerCheckpointAndBindings()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 15, 0, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: observedAt,
            sourceTransactionId: Guid.NewGuid(),
            sourceBlockHeight: 42,
            sourceBlockId: Guid.NewGuid());
        var deploymentEvent = CreateDeploymentEvent(observedAt.AddMinutes(-1));
        var proofFamily = CreateProofFamilyStatus(observedAt);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Accepted, observedAt),
            [deploymentEvent],
            [proofFamily]);

        var result = await service.BindForOpenAsync(repository, election, openArtifact);
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeTrue();
        result.WasCaptured.Should().BeTrue();
        result.CheckpointId.Should().NotBeNull();

        var ledger = await repository.GetDeploymentProofLedgerAsync(election.ElectionId);
        ledger.Should().NotBeNull();
        ledger!.Status.Should().Be(ElectionDeploymentProofEvidenceStatus.Accepted);
        ledger.OpenedAtUtc.Should().Be(observedAt);
        ledger.ActiveProofSetIdAtOpen.Should().Be("server-proof-v1");
        ledger.LatestCheckpointId.Should().Be(result.CheckpointId);

        var checkpoint = await repository.GetDeploymentProofCheckpointAsync(result.CheckpointId!.Value);
        checkpoint.Should().NotBeNull();
        checkpoint!.CheckpointType.Should().Be(ElectionDeploymentProofCheckpointType.DraftToOpen);
        checkpoint.TransitionArtifactId.Should().Be(openArtifact.Id);
        checkpoint.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.Accepted);
        checkpoint.BlocksDeploymentProofClaims.Should().BeFalse();

        var observations = await repository.GetDeploymentProofComponentObservationsAsync(checkpoint.Id);
        observations.Select(x => x.ComponentId).Should()
            .Equal(ElectionDeploymentProofComponentId.HushServerNode, ElectionDeploymentProofComponentId.HushWebClient);
        observations.Single(x => x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient)
            .MismatchCode.Should()
            .Be(ElectionDeploymentProofConstants.Feat144WebClientProofMissingCode);

        var events = await repository.GetDeploymentProofEventsAsync(checkpoint.Id);
        events.Should().ContainSingle().Which.EventPublicId.Should().Be(deploymentEvent.EventPublicId);

        var proofFamilies = await repository.GetProofFamilyBindingStatusesAsync(checkpoint.Id);
        proofFamilies.Should().ContainSingle().Which.ProofFamilyId.Should()
            .Be(ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId);
    }

    [Fact]
    public async Task BindForOpenAsync_WithExpectedWebClientProof_RecordsExpectedSlotWithoutObservedClaim()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 15, 10, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: observedAt);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Accepted, observedAt) with
            {
                ExpectedWebClientProof = CreateWebClientProof(
                    "webclient-proof-v1",
                    Hash('d'),
                    ElectionDeploymentProofObservationSource.Catalog),
            },
            proofFamilies: [CreateProofFamilyStatus(observedAt)]);

        var result = await service.BindForOpenAsync(repository, election, openArtifact);
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeTrue();

        var observations = await repository.GetDeploymentProofComponentObservationsAsync(result.CheckpointId!.Value);
        var webClientObservation = observations.Single(x =>
            x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient);
        webClientObservation.DeploymentProofId.Should().Be("webclient-proof-v1");
        webClientObservation.ExpectedDeploymentProofId.Should().Be("webclient-proof-v1");
        webClientObservation.ObservedDeploymentProofId.Should().BeNull();
        webClientObservation.ExpectedArtifactHash.Should().Be("sha256:" + Hash('d'));
        webClientObservation.ObservedArtifactHash.Should().BeNull();
        webClientObservation.EvidenceStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.Missing);
        webClientObservation.ObservationSource.Should().Be(ElectionDeploymentProofObservationSource.NotAvailable);
        webClientObservation.MismatchCode.Should()
            .Be(ElectionDeploymentProofConstants.Feat144WebClientProofMissingCode);
    }

    [Fact]
    public async Task BindForOpenAsync_WithLatestFeat144Observation_RecordsObservedWebClientProof()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 15, 15, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: observedAt);
        await repository.SaveWebClientDeploymentProofObservationAsync(new ElectionWebClientDeploymentProofObservationRecord(
            Guid.NewGuid(),
            election.ElectionId.ToString(),
            "submit_transaction",
            ElectionDeploymentProofConstants.WebClientDeploymentProofHandshakeSchemaVersion,
            ElectionDeploymentProofConstants.WebClientComponentId,
            "webclient-proof-v1",
            "hush-prod-test",
            "git:refs/tags/deployment-proof-v1",
            "sha256:" + Hash('d'),
            "sha256:" + Hash('d'),
            Hash('f'),
            "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            MismatchCode: null,
            observedAt.AddSeconds(-1),
            observedAt.AddMinutes(-1)));
        await context.SaveChangesAsync();
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Accepted, observedAt) with
            {
                ExpectedWebClientProof = CreateWebClientProof(
                    "webclient-proof-v1",
                    Hash('d'),
                    ElectionDeploymentProofObservationSource.Catalog),
            },
            proofFamilies: [CreateProofFamilyStatus(observedAt)]);

        var result = await service.BindForOpenAsync(repository, election, openArtifact);
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeTrue();

        var observations = await repository.GetDeploymentProofComponentObservationsAsync(result.CheckpointId!.Value);
        var webClientObservation = observations.Single(x =>
            x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient);
        webClientObservation.ExpectedDeploymentProofId.Should().Be("webclient-proof-v1");
        webClientObservation.ObservedDeploymentProofId.Should().Be("webclient-proof-v1");
        webClientObservation.EvidenceStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.Accepted);
        webClientObservation.ObservationSource.Should()
            .Be(ElectionDeploymentProofObservationSource.Feat144Handshake);
        webClientObservation.MismatchCode.Should().BeNull();
    }

    [Fact]
    public async Task BindForOpenAsync_WithObservedWebClientMismatch_RecordsMismatchPolicyForFeat144()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 15, 20, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: observedAt);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Accepted, observedAt) with
            {
                ExpectedWebClientProof = CreateWebClientProof(
                    "webclient-proof-v1",
                    Hash('d'),
                    ElectionDeploymentProofObservationSource.Catalog),
                ObservedWebClientProof = CreateWebClientProof(
                    "webclient-proof-v2",
                    Hash('e'),
                    ElectionDeploymentProofObservationSource.Feat144Handshake),
            },
            proofFamilies: [CreateProofFamilyStatus(observedAt)]);

        var result = await service.BindForOpenAsync(repository, election, openArtifact);
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeTrue();

        var observations = await repository.GetDeploymentProofComponentObservationsAsync(result.CheckpointId!.Value);
        var webClientObservation = observations.Single(x =>
            x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient);
        webClientObservation.ExpectedDeploymentProofId.Should().Be("webclient-proof-v1");
        webClientObservation.ObservedDeploymentProofId.Should().Be("webclient-proof-v2");
        webClientObservation.ExpectedArtifactHash.Should().Be("sha256:" + Hash('d'));
        webClientObservation.ObservedArtifactHash.Should().Be("sha256:" + Hash('e'));
        webClientObservation.EvidenceStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.Mismatch);
        webClientObservation.ObservationSource.Should()
            .Be(ElectionDeploymentProofObservationSource.Feat144Handshake);
        webClientObservation.MismatchCode.Should()
            .Be(ElectionDeploymentProofConstants.Feat144WebClientProofMismatchCode);
    }

    [Fact]
    public async Task BindForOpenAsync_WithMissingProductionProof_BlocksWithoutPersistingCheckpoint()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 15, 30, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: observedAt);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Missing, observedAt));

        var result = await service.BindForOpenAsync(repository, election, openArtifact);
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeFalse();
        result.WasCaptured.Should().BeFalse();
        result.EvidenceStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.Missing);
        result.FailureCodes.Should().Contain("deployment_proof_missing");
        (await repository.GetDeploymentProofLedgerAsync(election.ElectionId)).Should().BeNull();
        (await repository.GetDeploymentProofCheckpointsAsync(election.ElectionId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileCheckpointAsync_WithEmergencyDeploymentEvent_PersistsBlockedClaimWithoutBlockingLifecycle()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 16, 0, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenedAt = observedAt.AddHours(-1),
            ClosedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var deploymentEvent = CreateDeploymentEvent(
            observedAt.AddMinutes(-5),
            ElectionDeploymentProofImpactClassification.EmergencyChange);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Accepted, observedAt),
            [deploymentEvent]);

        var result = await service.ReconcileCheckpointAsync(
            repository,
            new ElectionDeploymentProofReconciliationRequest(
                election,
                ElectionDeploymentProofCheckpointType.OpenToClose,
                ElectionLifecycleState.Open,
                ElectionLifecycleState.Closed,
                observedAt,
                TransitionArtifactId: Guid.NewGuid(),
                ReportPackageId: null,
                SourceTransactionId: Guid.NewGuid(),
                SourceBlockHeight: 51,
                SourceBlockId: Guid.NewGuid()));
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeTrue();
        result.EvidenceStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.Blocked);
        result.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.Blocked);

        var checkpoint = await repository.GetDeploymentProofCheckpointAsync(result.CheckpointId!.Value);
        checkpoint.Should().NotBeNull();
        checkpoint!.CheckpointType.Should().Be(ElectionDeploymentProofCheckpointType.OpenToClose);
        checkpoint.BlocksDeploymentProofClaims.Should().BeTrue();

        var events = await repository.GetDeploymentProofEventsAsync(checkpoint.Id);
        events.Should().ContainSingle().Which.Classification.Should()
            .Be(ElectionDeploymentProofImpactClassification.EmergencyChange);
    }

    [Fact]
    public async Task BuildPublicLedgerArtifactAsync_AfterFinalPackageExport_EmitsPublicSafeLedgerAndReference()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var openedAt = new DateTime(2026, 5, 26, 17, 0, 0, DateTimeKind.Utc);
        var exportedAt = openedAt.AddMinutes(15);
        var reportPackageId = Guid.NewGuid();
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: openedAt);
        var deploymentEvent = CreateDeploymentEvent(
            openedAt.AddMinutes(10),
            ElectionDeploymentProofImpactClassification.WebsiteOnlyNoProtocolChange);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations, openedAt),
            [deploymentEvent],
            [CreateProofFamilyStatus(exportedAt)]);

        await service.BindForOpenAsync(repository, election, openArtifact);
        var finalizedElection = election with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = exportedAt,
            LastUpdatedAt = exportedAt,
        };

        var finalExportResult = await service.ReconcileCheckpointAsync(
            repository,
            new ElectionDeploymentProofReconciliationRequest(
                finalizedElection,
                ElectionDeploymentProofCheckpointType.FinalPackageExport,
                ElectionLifecycleState.Finalized,
                ElectionLifecycleState.Finalized,
                exportedAt,
                TransitionArtifactId: Guid.NewGuid(),
                reportPackageId,
                SourceTransactionId: Guid.NewGuid(),
                SourceBlockHeight: 99,
                SourceBlockId: Guid.NewGuid()));
        await context.SaveChangesAsync();

        finalExportResult.WasCaptured.Should().BeTrue();
        finalExportResult.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.AcceptedWithLimitations);

        var artifact = await service.BuildPublicLedgerArtifactAsync(repository, election.ElectionId);

        artifact.Should().NotBeNull();
        artifact!.SchemaId.Should().Be(ElectionDeploymentProofConstants.PublicLedgerArtifactSchemaId);
        artifact.FinalStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations.ToString());
        artifact.ClaimEffect.Should().Be(ElectionDeploymentProofClaimEffect.AcceptedWithLimitations.ToString());
        artifact.Checkpoints.Should().HaveCount(2);
        artifact.Checkpoints.Should().Contain(x =>
            x.CheckpointType == ElectionDeploymentProofCheckpointType.FinalPackageExport.ToString() &&
            x.ReportPackageId == reportPackageId);
        artifact.DeploymentEvents.Should().ContainSingle(x =>
            x.EventPublicId == deploymentEvent.EventPublicId &&
            x.Classification == ElectionDeploymentProofImpactClassification.WebsiteOnlyNoProtocolChange.ToString());
        artifact.PublicPrivacyBoundary.Should().Contain("restricted_evidence_refs_are_ids_or_hashes_only");

        var content = JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        content.Should().NotContain("private key");
        content.Should().NotContain("raw log");
        content.Should().NotContain("voter identity");

        await service.UpdatePublicLedgerArtifactReferenceAsync(
            repository,
            election.ElectionId,
            $"report-package:{reportPackageId:N}/{ElectionDeploymentProofConstants.PublicLedgerArtifactFileName}",
            Hash('d'),
            exportedAt);
        await context.SaveChangesAsync();

        var ledger = await repository.GetDeploymentProofLedgerAsync(election.ElectionId);
        ledger!.PublicLedgerArtifactRef.Should()
            .Be($"report-package:{reportPackageId:N}/{ElectionDeploymentProofConstants.PublicLedgerArtifactFileName}");
        ledger.PublicLedgerArtifactHash.Should().Be(Hash('d'));
    }

    [Fact]
    public async Task BuildPublicLedgerArtifactAsync_WithStalePrivacyProof_EmitsProofFamilyClaimEffect()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var observedAt = new DateTime(2026, 5, 26, 17, 30, 0, DateTimeKind.Utc);
        var election = CreateElection(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = observedAt,
            LastUpdatedAt = observedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            election.OwnerPublicAddress,
            recordedAt: observedAt);
        var service = CreateService(
            CreateActiveContext(ElectionDeploymentProofEvidenceStatus.Accepted, observedAt),
            proofFamilies:
            [
                CreateProofFamilyStatus(
                    observedAt,
                    ElectionDeploymentProofEvidenceStatus.Stale,
                    mismatchCode: null,
                    "Retention/log privacy proof-family is stale for the active server proof."),
            ]);

        var result = await service.BindForOpenAsync(repository, election, openArtifact);
        await context.SaveChangesAsync();

        result.IsAllowed.Should().BeTrue();

        var artifact = await service.BuildPublicLedgerArtifactAsync(repository, election.ElectionId);

        artifact.Should().NotBeNull();
        artifact!.ProofFamilies.Should().ContainSingle().Which.Should().Match<ElectionDeploymentProofPublicProofFamilyArtifactRecord>(
            x => x.EvidenceStatus == ElectionDeploymentProofEvidenceStatus.Stale.ToString() &&
                 x.ClaimEffect == ElectionDeploymentProofClaimEffect.Downgraded.ToString() &&
                 x.MismatchCode == ElectionDeploymentProofConstants.PrivacyProofStaleCode);
        artifact.ClaimLimitations.Should().Contain(x =>
            x.Contains(ElectionDeploymentProofConstants.Feat137SourceFeature, StringComparison.Ordinal) &&
            x.Contains(ElectionDeploymentProofClaimEffect.Downgraded.ToString(), StringComparison.Ordinal));
    }

    private static ElectionDeploymentProofBindingService CreateService(
        ActiveDeploymentProofContext activeContext,
        IReadOnlyList<ActiveDeploymentProofEvent>? events = null,
        IReadOnlyList<ActiveProofFamilyStatus>? proofFamilies = null) =>
        new(
            new ElectionDeploymentProofProfilePolicy(ElectionDeploymentProofOptions.Default),
            new FixtureActiveDeploymentProofProvider(activeContext, events, proofFamilies));

    private static ActiveDeploymentProofContext CreateActiveContext(
        ElectionDeploymentProofEvidenceStatus providerStatus,
        DateTime observedAt) =>
        new(
            providerStatus,
            observedAt,
            DeploymentTarget: "hush-prod-test",
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            PublicCatalogRef: "refs/tags/deployment-proof-v1",
            PlatformCeremonyId: "ceremony-public-1",
            ServerProof: providerStatus.BlocksDeploymentProofClaims()
                ? null
                : new ActiveDeploymentProofComponent(
                    ElectionDeploymentProofComponentId.HushServerNode,
                    "server-proof-v1",
                    ElectionDeploymentProofEvidenceStatus.Accepted,
                    "git:refs/tags/deployment-proof-v1",
                    "sha256:" + Hash('a'),
                    Hash('b'),
                    "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
                    PreviousProofId: null,
                    SupersedesProofIds: Array.Empty<string>(),
                    ElectionDeploymentProofObservationSource.Fixture),
            ExpectedWebClientProof: null,
            ProviderErrors: Array.Empty<ActiveDeploymentProofProviderError>());

    private static ActiveDeploymentProofComponent CreateWebClientProof(
        string proofId,
        string artifactHash,
        ElectionDeploymentProofObservationSource observationSource) =>
        new(
            ElectionDeploymentProofComponentId.HushWebClient,
            proofId,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            "git:refs/tags/deployment-proof-v1",
            "sha256:" + artifactHash,
            Hash('f'),
            "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
            PreviousProofId: null,
            SupersedesProofIds: Array.Empty<string>(),
            observationSource);

    private static ActiveDeploymentProofEvent CreateDeploymentEvent(
        DateTime occurredAt,
        ElectionDeploymentProofImpactClassification classification =
            ElectionDeploymentProofImpactClassification.VotingProtocolNoChange) =>
        new(
            "deployment-event-1",
            "release",
            "deployment-run-1",
            ElectionDeploymentProofComponentId.HushServerNode,
            "server-proof-v0",
            "server-proof-v1",
            classification,
            "Routine deployment event.",
            ["smoke-tests"],
            "passed",
            "release-manager-approved",
            occurredAt,
            ElectionDeploymentProofEvidenceStatus.Accepted);

    private static ActiveProofFamilyStatus CreateProofFamilyStatus(
        DateTime observedAt,
        ElectionDeploymentProofEvidenceStatus status = ElectionDeploymentProofEvidenceStatus.Accepted,
        string? mismatchCode = null,
        string publicSummary = "Retention/log privacy proof-family remains accepted.") =>
        new(
            ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
            "v1",
            "feat137-retention-log-privacy",
            Hash('c'),
            "readiness-register/feat137",
            ElectionDeploymentProofConstants.Feat137SourceFeature,
            status,
            mismatchCode,
            publicSummary,
            observedAt);

    private static ElectionRecord CreateElection(string selectedProfileId) =>
        ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            title: "Deployment proof binding election",
            shortDescription: null,
            ownerPublicAddress: "owner-address",
            externalReferenceCode: null,
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId,
            selectedProfileDevOnly: false,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass-fail-simple-majority",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "reject-on-tie",
                CalculationBasis: "counted-votes"),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushvoting", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, false),
                new ElectionOptionDefinition("no", "No", null, 2, false),
            ],
            requiredApprovalCount: null);

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

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);
}

using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionDeploymentProofBindingRepositoryTests
{
    [Fact]
    public async Task SaveDeploymentProofLedgerCheckpointAndBindings_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        var ledger = CreateLedger(electionId, now);
        var checkpoint = CreateCheckpoint(
            ledger.Id,
            electionId,
            ElectionDeploymentProofCheckpointType.DraftToOpen,
            ElectionLifecycleState.Draft,
            ElectionLifecycleState.Open,
            now.AddSeconds(5),
            transitionArtifactId: Guid.NewGuid());
        var serverObservation = CreateComponentObservation(
            checkpoint.Id,
            electionId,
            ElectionDeploymentProofComponentId.HushServerNode,
            "server-proof-v1",
            ElectionDeploymentProofEvidenceStatus.Accepted,
            ElectionDeploymentProofObservationSource.Provider,
            now.AddSeconds(6));
        var webObservation = CreateComponentObservation(
            checkpoint.Id,
            electionId,
            ElectionDeploymentProofComponentId.HushWebClient,
            "web-proof-v1",
            ElectionDeploymentProofEvidenceStatus.NotYetSupported,
            ElectionDeploymentProofObservationSource.NotAvailable,
            now.AddSeconds(7),
            mismatchCode: ElectionDeploymentProofConstants.Feat144WebClientProofNotSupportedCode);
        var deploymentEvent = CreateDeploymentEvent(checkpoint.Id, electionId, now.AddSeconds(4));
        var proofFamily = CreateProofFamilyStatus(checkpoint.Id, electionId, now.AddSeconds(8));
        var browserObservation = CreateWebClientObservation(electionId, now.AddSeconds(9));

        await repository.SaveDeploymentProofLedgerAsync(ledger);
        await repository.SaveDeploymentProofCheckpointAsync(checkpoint);
        await repository.SaveDeploymentProofComponentObservationAsync(serverObservation);
        await repository.SaveDeploymentProofComponentObservationAsync(webObservation);
        await repository.SaveDeploymentProofEventAsync(deploymentEvent);
        await repository.SaveProofFamilyBindingStatusAsync(proofFamily);
        await repository.SaveWebClientDeploymentProofObservationAsync(browserObservation);
        await context.SaveChangesAsync();

        await repository.UpdateDeploymentProofLedgerAsync(ledger with
        {
            Status = ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations,
            ActiveProofSetIdAtOpen = checkpoint.ProofSetId,
            OpenedAtUtc = now.AddSeconds(10),
            LatestCheckpointId = checkpoint.Id,
            LastReconciledAtUtc = now.AddSeconds(10),
        });
        await context.SaveChangesAsync();

        var storedLedger = await repository.GetDeploymentProofLedgerAsync(electionId);
        var storedLedgerById = await repository.GetDeploymentProofLedgerAsync(ledger.Id);
        var checkpoints = await repository.GetDeploymentProofCheckpointsAsync(electionId);
        var checkpointById = await repository.GetDeploymentProofCheckpointAsync(checkpoint.Id);
        var latestCheckpoint = await repository.GetLatestDeploymentProofCheckpointAsync(electionId);
        var latestOpenCheckpoint = await repository.GetLatestDeploymentProofCheckpointAsync(
            electionId,
            ElectionDeploymentProofCheckpointType.DraftToOpen);
        var observations = await repository.GetDeploymentProofComponentObservationsAsync(checkpoint.Id);
        var events = await repository.GetDeploymentProofEventsAsync(checkpoint.Id);
        var proofFamilies = await repository.GetProofFamilyBindingStatusesAsync(checkpoint.Id);
        var electionProofFamilies = await repository.GetProofFamilyBindingStatusesForElectionAsync(electionId);
        var latestBrowserObservation = await repository.GetLatestWebClientDeploymentProofObservationAsync(
            electionId,
            now.AddSeconds(10));

        storedLedger.Should().NotBeNull();
        storedLedger!.LedgerPublicId.Should().Be("deployment-ledger-test");
        storedLedger.Status.Should().Be(ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations);
        storedLedger.ActiveProofSetIdAtOpen.Should().Be("proof-set-open-v1");
        storedLedger.LatestCheckpointId.Should().Be(checkpoint.Id);
        storedLedgerById.Should().BeEquivalentTo(storedLedger);

        checkpoints.Should().ContainSingle();
        checkpointById.Should().BeEquivalentTo(checkpoint);
        latestCheckpoint.Should().BeEquivalentTo(checkpoint);
        latestOpenCheckpoint.Should().BeEquivalentTo(checkpoint);
        checkpoint.BlocksDeploymentProofClaims.Should().BeFalse();

        observations.Should().HaveCount(2);
        observations.Select(x => x.ComponentId).Should()
            .Equal(ElectionDeploymentProofComponentId.HushServerNode, ElectionDeploymentProofComponentId.HushWebClient);
        observations.Single(x => x.ComponentId == ElectionDeploymentProofComponentId.HushServerNode)
            .PackageHash.Should()
            .Be(Hash('b'));
        observations.Single(x => x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient)
            .EvidenceStatus.Should()
            .Be(ElectionDeploymentProofEvidenceStatus.NotYetSupported);

        events.Should().ContainSingle();
        events[0].Classification.Should().Be(ElectionDeploymentProofImpactClassification.VotingProtocolNoChange);
        events[0].RequiresClassificationRemediation.Should().BeFalse();

        proofFamilies.Should().ContainSingle();
        proofFamilies[0].ProofFamilyId.Should().Be(ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId);
        electionProofFamilies.Should().BeEquivalentTo(proofFamilies);

        latestBrowserObservation.Should().NotBeNull();
        latestBrowserObservation!.DeploymentProofId.Should().Be("webclient-proof-v1");
        latestBrowserObservation.EvidenceStatus.Should().Be(ElectionDeploymentProofEvidenceStatus.Accepted);
        latestBrowserObservation.MismatchCode.Should().BeNull();
    }

    [Fact]
    public async Task SaveDeploymentProofBindingDuplicates_ShouldFailClosed()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var now = new DateTime(2026, 5, 26, 12, 30, 0, DateTimeKind.Utc);
        var ledger = CreateLedger(electionId, now);
        var checkpoint = CreateCheckpoint(
            ledger.Id,
            electionId,
            ElectionDeploymentProofCheckpointType.DraftToOpen,
            ElectionLifecycleState.Draft,
            ElectionLifecycleState.Open,
            now.AddSeconds(1),
            transitionArtifactId: Guid.NewGuid());
        var observation = CreateComponentObservation(
            checkpoint.Id,
            electionId,
            ElectionDeploymentProofComponentId.HushServerNode,
            "server-proof-v1",
            ElectionDeploymentProofEvidenceStatus.Accepted,
            ElectionDeploymentProofObservationSource.Provider,
            now.AddSeconds(2));
        var deploymentEvent = CreateDeploymentEvent(checkpoint.Id, electionId, now.AddSeconds(3));
        var proofFamily = CreateProofFamilyStatus(checkpoint.Id, electionId, now.AddSeconds(4));

        await repository.SaveDeploymentProofLedgerAsync(ledger);
        await repository.SaveDeploymentProofCheckpointAsync(checkpoint);
        await repository.SaveDeploymentProofComponentObservationAsync(observation);
        await repository.SaveDeploymentProofEventAsync(deploymentEvent);
        await repository.SaveProofFamilyBindingStatusAsync(proofFamily);
        await context.SaveChangesAsync();

        var duplicateLedger = ledger with { Id = Guid.NewGuid(), LedgerPublicId = "deployment-ledger-test-2" };
        var duplicateCheckpoint = checkpoint with { Id = Guid.NewGuid(), ObservedAtUtc = now.AddMinutes(1) };
        var duplicateObservation = observation with { Id = Guid.NewGuid(), DeploymentProofId = "server-proof-v2" };
        var duplicateEvent = deploymentEvent with { Id = Guid.NewGuid(), AfterProofId = "server-proof-v2" };
        var duplicateProofFamily = proofFamily with { Id = Guid.NewGuid(), PackageHash = Hash('f') };

        await FluentActions.Invoking(() => repository.SaveDeploymentProofLedgerAsync(duplicateLedger))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A deployment proof ledger already exists*");
        await FluentActions.Invoking(() => repository.SaveDeploymentProofCheckpointAsync(duplicateCheckpoint))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A deployment proof checkpoint already exists*");
        await FluentActions.Invoking(() => repository.SaveDeploymentProofComponentObservationAsync(duplicateObservation))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A deployment proof component observation already exists*");
        await FluentActions.Invoking(() => repository.SaveDeploymentProofEventAsync(duplicateEvent))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A deployment proof event already exists*");
        await FluentActions.Invoking(() => repository.SaveProofFamilyBindingStatusAsync(duplicateProofFamily))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A proof-family binding status already exists*");
    }

    [Fact]
    public void DeploymentProofRecords_WithRestrictedPublicValue_ShouldThrow()
    {
        var act = () => CreateCheckpoint(
            Guid.NewGuid(),
            ElectionId.NewElectionId,
            ElectionDeploymentProofCheckpointType.DraftToOpen,
            ElectionLifecycleState.Draft,
            ElectionLifecycleState.Open,
            DateTime.UtcNow,
            transitionArtifactId: Guid.NewGuid(),
            publicSummary: "Provider result included kms:alias/hush-voting.");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Public deployment proof values cannot contain restricted material.*")
            .And.ParamName.Should()
            .Be("PublicSummary");
    }

    private static ElectionDeploymentProofLedgerRecord CreateLedger(
        ElectionId electionId,
        DateTime now) =>
        new(
            Guid.NewGuid(),
            electionId,
            " deployment-ledger-test ",
            ElectionDeploymentProofConstants.SchemaVersion,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            ElectionDeploymentProofLedgerVisibility.Public,
            " controlled-pilot ",
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            "https://github.com/HushNetworkOrg/hush-deployment-proofs",
            "refs/tags/deployment-proof-v1",
            Hash('a'),
            "ceremony-public-1",
            ActiveProofSetIdAtOpen: null,
            OpenedAtUtc: null,
            ClosedAtUtc: null,
            FinalizedAtUtc: null,
            VoidedAtUtc: null,
            LatestCheckpointId: null,
            FinalStatus: null,
            PublicLedgerArtifactRef: null,
            PublicLedgerArtifactHash: null,
            RestrictedEvidenceIndexRef: null,
            now,
            now);

    private static ElectionDeploymentProofCheckpointRecord CreateCheckpoint(
        Guid ledgerId,
        ElectionId electionId,
        ElectionDeploymentProofCheckpointType checkpointType,
        ElectionLifecycleState sourceState,
        ElectionLifecycleState targetState,
        DateTime observedAt,
        Guid? transitionArtifactId = null,
        Guid? reportPackageId = null,
        string publicSummary = "Deployment proof accepted for this lifecycle checkpoint.") =>
        new(
            Guid.NewGuid(),
            ledgerId,
            electionId,
            checkpointType,
            sourceState,
            targetState,
            transitionArtifactId,
            reportPackageId,
            "proof-set-open-v1",
            ElectionDeploymentProofEvidenceStatus.Accepted,
            ElectionDeploymentProofClaimEffect.Accepted,
            observedAt,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            ["provider:accepted"],
            SupersedesCheckpointId: null,
            publicSummary,
            SourceTransactionId: Guid.NewGuid(),
            SourceBlockHeight: 42,
            SourceBlockId: Guid.NewGuid());

    private static ElectionDeploymentProofComponentObservationRecord CreateComponentObservation(
        Guid checkpointId,
        ElectionId electionId,
        ElectionDeploymentProofComponentId componentId,
        string proofId,
        ElectionDeploymentProofEvidenceStatus status,
        ElectionDeploymentProofObservationSource source,
        DateTime observedAt,
        string? mismatchCode = null) =>
        new(
            Guid.NewGuid(),
            checkpointId,
            electionId,
            componentId,
            proofId,
            proofId,
            source == ElectionDeploymentProofObservationSource.NotAvailable ? null : proofId,
            Hash('c'),
            source == ElectionDeploymentProofObservationSource.NotAvailable ? null : Hash('c'),
            status,
            source,
            "git:refs/tags/deployment-proof-v1",
            "sha256:" + Hash('d'),
            "SHA256:" + Hash('b'),
            "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
            mismatchCode,
            ["server-proof-v0"],
            observedAt);

    private static ElectionDeploymentProofEventRecord CreateDeploymentEvent(
        Guid checkpointId,
        ElectionId electionId,
        DateTime occurredAt) =>
        new(
            Guid.NewGuid(),
            checkpointId,
            electionId,
            "deployment-event-1",
            "release",
            "deployment-run-1",
            ElectionDeploymentProofComponentId.HushServerNode,
            "server-proof-v0",
            "server-proof-v1",
            ElectionDeploymentProofImpactClassification.VotingProtocolNoChange,
            "Routine server release with unchanged voting protocol.",
            ["smoke-tests", "protocol-proof-hash-check"],
            "passed",
            "release-manager-approved",
            occurredAt,
            ElectionDeploymentProofEvidenceStatus.Accepted);

    private static ElectionWebClientDeploymentProofObservationRecord CreateWebClientObservation(
        ElectionId electionId,
        DateTime observedAt) =>
        new(
            Guid.NewGuid(),
            electionId.ToString(),
            "submit_transaction",
            ElectionDeploymentProofConstants.WebClientDeploymentProofHandshakeSchemaVersion,
            ElectionDeploymentProofConstants.WebClientComponentId,
            "webclient-proof-v1",
            "hush-prod-test",
            "git:refs/tags/deployment-proof-v1",
            "sha256:" + Hash('c'),
            "sha256:" + Hash('c'),
            Hash('b'),
            "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            MismatchCode: null,
            observedAt,
            observedAt.AddMinutes(-1));

    private static ElectionProofFamilyBindingStatusRecord CreateProofFamilyStatus(
        Guid checkpointId,
        ElectionId electionId,
        DateTime observedAt) =>
        new(
            Guid.NewGuid(),
            checkpointId,
            electionId,
            ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
            "v1",
            "feat137-retention-log-privacy",
            Hash('e'),
            "readiness-register/feat137-retention-log-privacy",
            ElectionDeploymentProofConstants.Feat137SourceFeature,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            MismatchCode: null,
            "Retention/log privacy proof-family remains accepted for this server proof set.",
            observedAt);

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

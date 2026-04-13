using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using HushShared.Elections.Model;
using Olimpo;
using System.Text.Json;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-097")]
public sealed class ElectionCeremonyProfileRegistryIntegrationTests : IAsyncLifetime
{
    private static readonly TestIdentity Delta = TestIdentities.GenerateFromSeed("TEST_DELTA_V1", "Delta");
    private static readonly TestIdentity Echo = TestIdentities.GenerateFromSeed("TEST_ECHO_V1", "Echo");
    private static readonly TestIdentity Foxtrot = TestIdentities.GenerateFromSeed("TEST_FOXTROT_V1", "Foxtrot");
    private static readonly IReadOnlyList<TestIdentity> RolloutTrustees =
    [
        TestIdentities.Bob,
        TestIdentities.Charlie,
        Delta,
        Echo,
        Foxtrot,
    ];

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
    public async Task GetElection_WithDevProfilesEnabled_ExposesTheShippedProfilePair()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Dev Visibility");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = createResponse.Election.ElectionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyProfiles.Select(x => x.ProfileId).Should().Contain(["dkg-dev-3of5", "dkg-prod-3of5"]);
    }

    [Fact]
    public async Task StartElectionCeremony_WithDevProfilesDisabled_HidesAndRejectsDevOnlyProfiles()
    {
        var client = await StartClientAsync(enableDevProfiles: false);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Production Gating");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = createResponse.Election.ElectionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyProfiles.Select(x => x.ProfileId).Should().Equal("dkg-prod-3of5");

        var startSubmitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.StartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(createResponse.Election.ElectionId)),
                "dkg-dev-3of5"));

        startSubmitResponse.Successfull.Should().BeFalse();
        startSubmitResponse.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StartGovernedProposal_WithReadyCeremony_AllowsGovernedOpenPath()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Ready Ceremony");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(client, electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });

        readiness.IsReadyToOpen.Should().BeTrue(string.Join(" | ", readiness.ValidationErrors));
        readiness.CeremonySnapshot.Should().NotBeNull();
        readiness.CeremonySnapshot!.ProfileId.Should().Be("dkg-prod-3of5");
        readiness.CeremonySnapshot.CompletedTrustees.Should().HaveCount(3);

        var proposalResponse = await StartGovernedProposalViaBlockchainAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);

        proposalResponse.Success.Should().BeTrue();
        proposalResponse.GovernedProposals.Should().ContainSingle(x =>
            x.ActionType == ElectionGovernedActionTypeProto.GovernedActionOpen &&
            x.ExecutionStatus == ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
    }

    [Fact]
    public async Task StartGovernedProposal_WithIncompleteCeremony_BlocksGovernedOpenPath()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Incomplete Ceremony");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, RolloutTrustees[0], 0);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });

        readiness.IsReadyToOpen.Should().BeFalse();
        readiness.ValidationErrors.Should().Contain(x => x.Contains("ready key-ceremony version", StringComparison.OrdinalIgnoreCase));

        var (submitResponse, proposalId) = await SubmitStartGovernedProposalAsync(
            electionId,
            ElectionGovernedActionType.Open);

        submitResponse.Successfull.Should().BeFalse();
        submitResponse.Message.Should().NotBeNullOrWhiteSpace();

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.GovernedProposals.Should().NotContain(x => x.Id == proposalId.ToString());
    }

    [Fact]
    public async Task RestartElectionCeremony_AfterProgress_SupersedesPreviousVersionAndKeepsOpenBlocked()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Superseded Ceremony");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var firstVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await PublishAndJoinAsync(client, electionId, firstVersionId, RolloutTrustees[0], 0);

        var restartResponse = await RestartCeremonyAsync(
            electionId,
            "dkg-prod-3of5",
            "Replace the partially completed version.");

        restartResponse.Success.Should().BeTrue(restartResponse.ErrorMessage);
        restartResponse.CeremonyVersion.Should().NotBeNull();
        restartResponse.CeremonyVersion!.VersionNumber.Should().Be(2);

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyVersions.Should().ContainSingle(x =>
            string.Equals(x.Id, firstVersionId, StringComparison.Ordinal) &&
            x.Status == ElectionCeremonyVersionStatusProto.CeremonyVersionSuperseded);
        electionResponse.CeremonyVersions.Should().ContainSingle(x =>
            string.Equals(x.Id, restartResponse.CeremonyVersion.Id, StringComparison.Ordinal) &&
            x.Status == ElectionCeremonyVersionStatusProto.CeremonyVersionInProgress);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });

        readiness.IsReadyToOpen.Should().BeFalse();
        readiness.ValidationErrors.Should().Contain(x => x.Contains("ready key-ceremony version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubmitElectionCeremonyMaterial_WithoutSelfTest_ReturnsValidationFailedAndKeepsTrusteeIncomplete()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Self-Test Required");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishAndJoinAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-without-self-test",
                payloadFingerprint: "payload-without-self-test"));

        submitResponse.Successfull.Should().BeFalse();
        submitResponse.Message.Should().NotBeNullOrWhiteSpace();

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateJoined);
    }

    [Fact]
    public async Task PublishElectionCeremonyTransportKey_WhenSubmittedTwice_ReturnsRejectedValidationCodeAndKeepsOriginalFingerprint()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Duplicate Transport Publish");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];

        var firstPublish = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                "transport-fingerprint-original"));
        firstPublish.Successfull.Should().BeTrue(firstPublish.Message);

        var duplicatePublish = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                "transport-fingerprint-replayed"));

        duplicatePublish.Successfull.Should().BeFalse();
        duplicatePublish.Status.Should().Be(TransactionStatus.Rejected);
        duplicatePublish.ValidationCode.Should().Be("election_ceremony_publish_invalid_state");
        duplicatePublish.Message.Should().Contain("already published a transport key");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.TransportPublicKeyFingerprint == "transport-fingerprint-original" &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateAcceptedTrustee);
    }

    [Fact]
    public async Task JoinElectionCeremony_WithoutTransportKey_ReturnsRejectedValidationCodeAndLeavesTrusteeNotStarted()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Join Requires Transport Key");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];

        var joinResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.JoinElectionCeremony(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));

        joinResponse.Successfull.Should().BeFalse();
        joinResponse.Status.Should().Be(TransactionStatus.Rejected);
        joinResponse.ValidationCode.Should().Be("election_ceremony_join_invalid_state");
        joinResponse.Message.Should().Contain("publish a transport key before joining");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateAcceptedTrustee);
    }

    [Fact]
    public async Task RecordElectionCeremonySelfTestSuccess_WhenSubmittedTwice_ReturnsRejectedValidationCodeAndKeepsJoinedState()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Duplicate Self-Test");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var duplicateSelfTest = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));

        duplicateSelfTest.Successfull.Should().BeFalse();
        duplicateSelfTest.Status.Should().Be(TransactionStatus.Rejected);
        duplicateSelfTest.ValidationCode.Should().Be("election_ceremony_self_test_invalid_state");
        duplicateSelfTest.Message.Should().Contain("self-test has already been recorded");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateJoined &&
            x.SelfTestSucceededAt != null);
    }

    [Fact]
    public async Task SubmitElectionCeremonyMaterial_AfterOnlyPublishingTransportKey_ReturnsRejectedValidationCodeAndKeepsTrusteeIncomplete()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Submit Requires Join And Self-Test");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];

        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                "transport-before-submit"));
        publishResponse.Successfull.Should().BeTrue(publishResponse.Message);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-with-missing-middle-steps",
                payloadFingerprint: "payload-with-missing-middle-steps"));

        submitResponse.Successfull.Should().BeFalse();
        submitResponse.Status.Should().Be(TransactionStatus.Rejected);
        submitResponse.ValidationCode.Should().Be("election_ceremony_submit_invalid_state");
        submitResponse.Message.Should().Contain("join the ceremony before submitting material");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateAcceptedTrustee &&
            x.TransportPublicKeyFingerprint == "transport-before-submit");
    }

    [Fact]
    public async Task SubmitElectionCeremonyMaterial_WithUnboundRecipient_ReturnsRejectedValidationCodeAndKeepsTrusteeJoined()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Submit Recipient Must Be Bound");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: "not-a-bound-trustee",
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-invalid-recipient",
                payloadFingerprint: "payload-invalid-recipient"));

        submitResponse.Successfull.Should().BeFalse();
        submitResponse.Status.Should().Be(TransactionStatus.Rejected);
        submitResponse.ValidationCode.Should().Be("election_ceremony_submit_invalid_recipient");
        submitResponse.Message.Should().Contain("Recipient trustee is not bound");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateJoined &&
            x.SelfTestSucceededAt != null &&
            x.MaterialSubmittedAt == null);
    }

    [Fact]
    public async Task CompleteElectionCeremonyTrustee_BeforeMaterialSubmitted_ReturnsRejectedValidationCodeAndKeepsTrusteeJoined()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Complete Requires Submitted Material");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var completeResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.CompleteElectionCeremonyTrustee(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "share-before-submit"));

        completeResponse.Successfull.Should().BeFalse();
        completeResponse.Status.Should().Be(TransactionStatus.Rejected);
        completeResponse.ValidationCode.Should().Be("election_ceremony_complete_invalid_state");
        completeResponse.Message.Should().Contain("material must be submitted before ceremony completion");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateJoined &&
            x.CompletedAt == null &&
            x.ShareVersion == "");
    }

    [Fact]
    public async Task RecordElectionCeremonyShareExport_BeforeCompletion_ReturnsRejectedValidationCodeAndKeepsTrusteeIncomplete()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Export Requires Completion");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-before-export",
                payloadFingerprint: "payload-before-export"));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var exportResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonyShareExport(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                "share-before-owner-complete"));

        exportResponse.Successfull.Should().BeFalse();
        exportResponse.Status.Should().Be(TransactionStatus.Rejected);
        exportResponse.ValidationCode.Should().Be("election_ceremony_share_not_ready");
        exportResponse.Message.Should().Contain("ceremony-complete trustee state");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateMaterialSubmitted &&
            x.CompletedAt == null);
    }

    [Fact]
    public async Task PublishElectionCeremonyTransportKey_OnSupersededVersion_ReturnsRejectedValidationCodeAndLeavesNewVersionActive()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Superseded Version Rejects Trustee Actions");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var firstVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var restartResponse = await RestartCeremonyAsync(
            electionId,
            "dkg-prod-3of5",
            "Replace stale version before trustee progress.");
        var trustee = RolloutTrustees[0];

        var stalePublishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(firstVersionId),
                "transport-stale-version"));

        stalePublishResponse.Successfull.Should().BeFalse();
        stalePublishResponse.Status.Should().Be(TransactionStatus.Rejected);
        stalePublishResponse.ValidationCode.Should().Be("election_ceremony_version_not_found");
        stalePublishResponse.Message.Should().Contain("was not found for election");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.CeremonyVersions.Should().ContainSingle(x =>
            x.Id == firstVersionId &&
            x.Status == ElectionCeremonyVersionStatusProto.CeremonyVersionSuperseded);
        electionResponse.CeremonyVersions.Should().ContainSingle(x =>
            x.Id == restartResponse.CeremonyVersion!.Id &&
            x.Status == ElectionCeremonyVersionStatusProto.CeremonyVersionInProgress);
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.CeremonyVersionId == restartResponse.CeremonyVersion!.Id &&
            x.TransportPublicKeyFingerprint == "" &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateAcceptedTrustee);
    }

    [Fact]
    public async Task CompleteElectionCeremonyTrustee_ByTrusteeActor_ReturnsRejectedValidationCodeAndKeepsOwnerBoundary()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Owner Boundary For Completion");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-before-illegal-complete",
                payloadFingerprint: "payload-before-illegal-complete"));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var illegalCompleteResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.CompleteElectionCeremonyTrustee(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "share-illegal-trustee-complete"));

        illegalCompleteResponse.Successfull.Should().BeFalse();
        illegalCompleteResponse.Status.Should().Be(TransactionStatus.Rejected);
        illegalCompleteResponse.ValidationCode.Should().Be("election_ceremony_owner_required");
        illegalCompleteResponse.Message.Should().Contain("Only the election owner");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateMaterialSubmitted &&
            x.CompletedAt == null);
    }

    [Fact]
    public async Task JoinElectionCeremony_WhenSubmittedTwice_ReturnsRejectedValidationCodeAndKeepsJoinedState()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Duplicate Join");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishAndJoinAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var duplicateJoin = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.JoinElectionCeremony(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));

        duplicateJoin.Successfull.Should().BeFalse();
        duplicateJoin.Status.Should().Be(TransactionStatus.Rejected);
        duplicateJoin.ValidationCode.Should().Be("election_ceremony_join_invalid_state");
        duplicateJoin.Message.Should().Contain("already joined this ceremony version");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateJoined &&
            x.JoinedAt != null);
    }

    [Fact]
    public async Task CompleteElectionCeremonyTrustee_WhenSubmittedTwiceByOwner_ReturnsRejectedValidationCodeAndKeepsCompletedState()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Duplicate Owner Completion");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-before-duplicate-complete",
                payloadFingerprint: "payload-before-duplicate-complete"));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var firstComplete = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.CompleteElectionCeremonyTrustee(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "share-v1-duplicate-complete"));
        firstComplete.Successfull.Should().BeTrue(firstComplete.Message);

        var duplicateComplete = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.CompleteElectionCeremonyTrustee(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "share-v2-duplicate-complete"));

        duplicateComplete.Successfull.Should().BeFalse();
        duplicateComplete.Status.Should().Be(TransactionStatus.Rejected);
        duplicateComplete.ValidationCode.Should().Be("election_ceremony_complete_invalid_state");
        duplicateComplete.Message.Should().Contain("already been recorded");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateCompleted &&
            x.ShareVersion == "share-v1-duplicate-complete");
    }

    [Fact]
    public async Task RecordElectionCeremonyShareImport_WithBindingMismatch_ReturnsRejectedValidationCodeAndLeavesCustodyExported()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Import Binding Mismatch");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-before-import-mismatch",
                payloadFingerprint: "payload-before-import-mismatch"));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var completeResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.CompleteElectionCeremonyTrustee(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "share-v1-import-mismatch"));
        completeResponse.Successfull.Should().BeTrue(completeResponse.Message);

        var exportResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonyShareExport(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                "share-v1-import-mismatch"));
        exportResponse.Successfull.Should().BeTrue(exportResponse.Message);

        var importResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonyShareImport(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                ElectionId.NewElectionId,
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "share-v1-import-mismatch"));

        importResponse.Successfull.Should().BeFalse();
        importResponse.Status.Should().Be(TransactionStatus.Rejected);
        importResponse.ValidationCode.Should().Be("election_ceremony_import_binding_mismatch");
        importResponse.Message.Should().Contain("exact ceremony binding");

        var actionView = await GetElectionCeremonyActionViewAsync(client, electionId, trustee);

        actionView.Success.Should().BeTrue();
        actionView.SelfShareCustody.Should().NotBeNull();
        actionView.SelfShareCustody.Status.Should().Be(ElectionCeremonyShareCustodyStatusProto.ShareCustodyExported);
        actionView.SelfShareCustody.LastImportFailureReason.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishElectionCeremonyTransportKey_AfterElectionLeavesDraft_ReturnsRejectedValidationCodeAndLeavesElectionOpen()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Ceremony Actions Stop After Draft");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(client, electionId, ceremonyVersionId, requiredCompletionCount: 3);
        await OpenElectionThroughGovernedApprovalsAsync(client, electionId, requiredApprovalCount: 3);
        var trustee = RolloutTrustees[3];

        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                "transport-after-open"));

        publishResponse.Successfull.Should().BeFalse();
        publishResponse.Status.Should().Be(TransactionStatus.Rejected);
        publishResponse.ValidationCode.Should().Be("election_ceremony_not_draft");
        publishResponse.Message.Should().Contain("only allowed while the election remains in draft");

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.CeremonyVersionId == ceremonyVersionId &&
            x.TransportPublicKeyFingerprint == "" &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateAcceptedTrustee);
    }

    [Fact]
    public async Task TrusteeResubmission_AfterOwnerValidationFailure_RequiresSelfTestThenAllowsResubmit()
    {
        var client = await StartClientAsync(enableDevProfiles: true);
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-097 Validation Failure Resubmission");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(client, electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        var trustee = RolloutTrustees[0];
        await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, 0);

        var firstSubmit = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-before-validation-failure",
                payloadFingerprint: "payload-before-validation-failure"));
        firstSubmit.Successfull.Should().BeTrue(firstSubmit.Message);

        var validationFailure = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonyValidationFailure(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                trustee.PublicSigningAddress,
                "stale package binding"));
        validationFailure.Successfull.Should().BeTrue(validationFailure.Message);

        var directResubmitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-without-rerun-self-test",
                payloadFingerprint: "payload-without-rerun-self-test"));

        directResubmitResponse.Successfull.Should().BeFalse();
        directResubmitResponse.Status.Should().Be(TransactionStatus.Rejected);
        directResubmitResponse.ValidationCode.Should().Be("election_ceremony_submit_invalid_state");
        directResubmitResponse.Message.Should().Contain("successful self-test");

        var rerunSelfTestResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        rerunSelfTestResponse.Successfull.Should().BeTrue(rerunSelfTestResponse.Message);

        var resubmitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionCeremonyMaterial(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                recipientTrusteeUserAddress: null,
                messageType: "dkg-share-package",
                payloadVersion: "omega-v1.0.0",
                encryptedPayload: "payload-after-validation-failure",
                payloadFingerprint: "payload-after-validation-failure"));

        resubmitResponse.Successfull.Should().BeTrue(resubmitResponse.Message);
        resubmitResponse.Status.Should().Be(TransactionStatus.Accepted);

        var electionResponse = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        electionResponse.Success.Should().BeTrue();
        electionResponse.ActiveCeremonyTrusteeStates.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, trustee.PublicSigningAddress, StringComparison.Ordinal) &&
            x.State == ElectionTrusteeCeremonyStateProto.CeremonyStateMaterialSubmitted &&
            x.MaterialSubmittedAt != null &&
            string.IsNullOrEmpty(x.ValidationFailureReason));
    }

    private async Task<HushElections.HushElectionsClient> StartClientAsync(bool enableDevProfiles)
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();

        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Elections:Ceremony:EnableDevCeremonyProfiles"] = enableDevProfiles ? "true" : "false",
        };

        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync(configurationOverrides: configurationOverrides);
        return _grpcFactory.CreateClient<HushElections.HushElectionsClient>();
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

    private async Task<ElectionCommandResponse> CreateTrusteeThresholdDraftAsync(
        HushElections.HushElectionsClient client,
        string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-097 integration draft",
            BuildTrusteeThresholdDraftSpecification(title));
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId.ToString(),
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.LatestDraftSnapshot.Should().NotBeNull();

        var importRosterResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ImportElectionRoster(
                TestIdentities.Alice,
                electionId,
                BuildOpenReadyRosterEntries()));
        importRosterResponse.Successfull.Should().BeTrue(importRosterResponse.Message);

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            DraftSnapshot = response.LatestDraftSnapshot,
        };
    }

    private static IReadOnlyList<ElectionRosterImportItem> BuildOpenReadyRosterEntries() =>
    [
        new ElectionRosterImportItem("feat097-owner-ready-001", ElectionRosterContactType.Email, "ready-owner-001@hush.test"),
        new ElectionRosterImportItem("feat097-owner-ready-002", ElectionRosterContactType.Phone, "+15550002002", IsInitiallyActive: false),
        new ElectionRosterImportItem("feat097-owner-ready-003", ElectionRosterContactType.Email, "ready-owner-003@hush.test"),
    ];

    private async Task InviteAndAcceptRolloutTrusteesAsync(HushElections.HushElectionsClient client, string electionId)
    {
        foreach (var trustee in RolloutTrustees)
        {
            var (signedTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                trustee);
            var inviteSubmitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
            inviteSubmitResponse.Successfull.Should().BeTrue(inviteSubmitResponse.Message);

            var acceptSubmitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.AcceptElectionTrusteeInvitation(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    invitationId));
            acceptSubmitResponse.Successfull.Should().BeTrue(acceptSubmitResponse.Message);
        }
    }

    private async Task<string> StartCeremonyAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string profileId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.StartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                profileId));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First()
            .Id;
    }

    private async Task<ElectionCommandResponse> RestartCeremonyAsync(
        string electionId,
        string profileId,
        string restartReason)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RestartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                profileId,
                restartReason));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await _grpcFactory!.CreateClient<HushElections.HushElectionsClient>().GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        var version = response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            CeremonyVersion = version,
        };
    }

    private async Task<GetElectionResponse> StartGovernedProposalViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        ElectionGovernedActionType actionType)
    {
        var (submitResponse, proposalId) = await SubmitStartGovernedProposalAsync(electionId, actionType);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
        await _blockControl!.ProduceBlockAsync();

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.GovernedProposals.Should().ContainSingle(x => x.Id == proposalId.ToString());
        return response;
    }

    private async Task<GetElectionResponse> OpenElectionThroughGovernedApprovalsAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        int requiredApprovalCount)
    {
        var startedProposal = await StartGovernedProposalViaBlockchainAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        var proposalId = Guid.Parse(startedProposal.GovernedProposals.Single().Id);

        for (var index = 0; index < requiredApprovalCount; index++)
        {
            var approvalResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.ApproveElectionGovernedProposal(
                    RolloutTrustees[index],
                    new ElectionId(Guid.Parse(electionId)),
                    proposalId,
                    approvalNote: null));
            approvalResponse.Successfull.Should().BeTrue(approvalResponse.Message);
        }

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);
        return response;
    }

    private async Task<GetElectionCeremonyActionViewResponse> GetElectionCeremonyActionViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var request = new GetElectionCeremonyActionViewRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
        };

        var response = await client.GetElectionCeremonyActionViewAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionCeremonyActionView),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<(SubmitSignedTransactionReply SubmitResponse, Guid ProposalId)> SubmitStartGovernedProposalAsync(
        string electionId,
        ElectionGovernedActionType actionType)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        var (signedTransaction, proposalId) = TestTransactionFactory.StartElectionGovernedProposal(
            TestIdentities.Alice,
            new ElectionId(Guid.Parse(electionId)),
            actionType);
        using var waiter = _node!.StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (submitResponse.Successfull)
        {
            await waiter.WaitAsync();
        }

        return (submitResponse, proposalId);
    }

    private async Task CompleteReadyThresholdAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        int requiredCompletionCount)
    {
        const string tallyFingerprint = "feat097-ready-tally-fingerprint";

        for (var index = 0; index < requiredCompletionCount; index++)
        {
            var trustee = RolloutTrustees[index];
            await PublishJoinAndSelfTestAsync(client, electionId, ceremonyVersionId, trustee, index);

            var submitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.SubmitElectionCeremonyMaterial(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    recipientTrusteeUserAddress: null,
                    messageType: "dkg-share-package",
                    payloadVersion: "omega-v1.0.0",
                    encryptedPayload: $"payload-{index}",
                    payloadFingerprint: $"payload-fingerprint-{index}"));
            submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

            var completeResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.CompleteElectionCeremonyTrustee(
                    TestIdentities.Alice,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    trustee.PublicSigningAddress,
                    $"share-v1-{index}",
                    tallyFingerprint));
            completeResponse.Successfull.Should().BeTrue(completeResponse.Message);
        }
    }

    private async Task PublishJoinAndSelfTestAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        await PublishAndJoinAsync(client, electionId, ceremonyVersionId, trustee, index);

        var selfTestResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        selfTestResponse.Successfull.Should().BeTrue(selfTestResponse.Message);
    }

    private async Task PublishAndJoinAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                $"transport-fingerprint-{index}"));
        publishResponse.Successfull.Should().BeTrue(publishResponse.Message);

        var joinResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.JoinElectionCeremony(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        joinResponse.Successfull.Should().BeTrue(joinResponse.Message);
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
        var payload = BuildSignedElectionQueryPayload(
            method,
            actor.PublicSigningAddress,
            signedAt,
            request);

        return new Metadata
        {
            { "x-hush-election-query-signatory", actor.PublicSigningAddress },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, actor.PrivateSigningKey) },
        };
    }

    private static string BuildSignedElectionQueryPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = DeepSortElectionQueryValue(request),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object? DeepSortElectionQueryValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in readOnlyDictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in dictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(DeepSortElectionQueryValue).ToArray();
        }

        return value;
    }

    private static ElectionDraftSpecification BuildTrusteeThresholdDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Governed policy vote",
            ExternalReferenceCode: "REF-2026-097",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass_fail_yes_no",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "simple_majority_of_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3);
}

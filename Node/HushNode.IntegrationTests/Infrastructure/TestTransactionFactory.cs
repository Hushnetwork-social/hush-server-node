using System.Text.Json;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using HushShared.Reactions.Model;
using HushNode.Reactions.Crypto;
using HushServerNode.Testing;
using Olimpo;
using System.Collections.Concurrent;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Factory for creating signed transactions for integration tests.
/// </summary>
internal static class TestTransactionFactory
{
    private static readonly ConcurrentDictionary<string, EncryptKeys> ElectionKeysByElectionId = new();

    /// <summary>
    /// Creates a signed identity registration transaction.
    /// </summary>
    /// <param name="identity">The test identity to register.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateIdentityRegistration(TestIdentity identity)
    {
        var payload = new FullIdentityPayload(
            identity.DisplayName,
            identity.PublicSigningAddress,
            identity.PublicEncryptAddress,
            IsPublic: false);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            FullIdentityPayloadHandler.FullIdentityPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            identity.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<FullIdentityPayload>(
            unsignedTransaction,
            new SignatureInfo(identity.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed election draft creation transaction.
    /// </summary>
    public static (string Transaction, ElectionId ElectionId) CreateElectionDraft(
        TestIdentity owner,
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var electionId = ElectionId.NewElectionId;
        var electionEncryptKeys = new EncryptKeys();
        ElectionKeysByElectionId[electionId.ToString()] = electionEncryptKeys;
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.CreateDraft,
            JsonSerializer.SerializeToElement(new CreateElectionDraftActionPayload(
                owner.PublicSigningAddress,
                snapshotReason,
                draft)));
        var envelopeSurface = BuildCurrentEnvelopeSurface(actionEnvelope);
        var unsignedTransaction = EncryptedElectionEnvelopePayloadHandler.CreateNewV21(
            electionId,
            EncryptKeys.Encrypt(electionEncryptKeys.PrivateKey, owner.PublicEncryptAddress),
            electionEncryptKeys.PublicKey,
            EncryptKeys.Encrypt(JsonSerializer.Serialize(actionEnvelope), electionEncryptKeys.PublicKey),
            actionEnvelope.ActionType,
            envelopeSurface.PublicActionPayload,
            envelopeSurface.ActionArtifacts);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            owner.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedTransaction,
            new SignatureInfo(owner.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), electionId);
    }

    /// <summary>
    /// Creates a signed election trustee invitation transaction.
    /// </summary>
    public static (string Transaction, Guid InvitationId) CreateElectionTrusteeInvitation(
        TestIdentity owner,
        ElectionId electionId,
        TestIdentity trustee)
    {
        var invitationId = Guid.NewGuid();
        var electionEncryptKeys = ElectionKeysByElectionId[electionId.ToString()];
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.InviteTrustee,
            JsonSerializer.SerializeToElement(new InviteElectionTrusteeActionPayload(
                invitationId,
                owner.PublicSigningAddress,
                trustee.PublicSigningAddress,
                EncryptKeys.Encrypt(electionEncryptKeys.PrivateKey, trustee.PublicEncryptAddress),
                trustee.DisplayName)));

        return (CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope), invitationId);
    }

    public static string AcceptElectionTrusteeInvitation(
        TestIdentity trustee,
        ElectionId electionId,
        Guid invitationId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.AcceptTrusteeInvitation,
            JsonSerializer.SerializeToElement(new ResolveElectionTrusteeInvitationActionPayload(
                invitationId,
                trustee.PublicSigningAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    public static string RejectElectionTrusteeInvitation(
        TestIdentity trustee,
        ElectionId electionId,
        Guid invitationId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RejectTrusteeInvitation,
            JsonSerializer.SerializeToElement(new ResolveElectionTrusteeInvitationActionPayload(
                invitationId,
                trustee.PublicSigningAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed election draft update transaction.
    /// </summary>
    public static string UpdateElectionDraft(
        TestIdentity owner,
        ElectionId electionId,
        string snapshotReason,
        ElectionDraftSpecification draft)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.UpdateDraft,
            JsonSerializer.SerializeToElement(new UpdateElectionDraftActionPayload(
                owner.PublicSigningAddress,
                snapshotReason,
                draft)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string ImportElectionRoster(
        TestIdentity owner,
        ElectionId electionId,
        IReadOnlyList<ElectionRosterImportItem> rosterEntries)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.ImportRoster,
            JsonSerializer.SerializeToElement(new ImportElectionRosterActionPayload(
                owner.PublicSigningAddress,
                rosterEntries)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string ClaimElectionRosterEntry(
        TestIdentity actor,
        ElectionId electionId,
        string organizationVoterId,
        string verificationCode)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.ClaimRosterEntry,
            JsonSerializer.SerializeToElement(new ClaimElectionRosterEntryActionPayload(
                actor.PublicSigningAddress,
                organizationVoterId,
                verificationCode)));

        return CreateEncryptedElectionEnvelopeTransaction(actor, electionId, actionEnvelope);
    }

    public static string ActivateElectionRosterEntry(
        TestIdentity owner,
        ElectionId electionId,
        string organizationVoterId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.ActivateRosterEntry,
            JsonSerializer.SerializeToElement(new ActivateElectionRosterEntryActionPayload(
                owner.PublicSigningAddress,
                organizationVoterId)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string RegisterElectionVotingCommitment(
        TestIdentity actor,
        ElectionId electionId,
        string commitmentHash)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RegisterVotingCommitment,
            JsonSerializer.SerializeToElement(new RegisterElectionVotingCommitmentActionPayload(
                actor.PublicSigningAddress,
                commitmentHash)));

        return CreateEncryptedElectionEnvelopeTransaction(actor, electionId, actionEnvelope);
    }

    public static string AcceptElectionBallotCast(
        TestIdentity actor,
        ElectionId electionId,
        string idempotencyKey,
        string encryptedBallotPackage,
        string proofBundle,
        string ballotNullifier,
        Guid openArtifactId,
        byte[] eligibleSetHash,
        Guid ceremonyVersionId,
        string dkgProfileId,
        string tallyPublicKeyFingerprint)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast,
            JsonSerializer.SerializeToElement(new AcceptElectionBallotCastActionPayload(
                actor.PublicSigningAddress,
                idempotencyKey,
                encryptedBallotPackage,
                proofBundle,
                ballotNullifier,
                openArtifactId,
                eligibleSetHash,
                ceremonyVersionId,
                dkgProfileId,
                tallyPublicKeyFingerprint)));

        return CreateEncryptedElectionEnvelopeTransaction(actor, electionId, actionEnvelope);
    }

    public static string CreateElectionReportAccessGrant(
        TestIdentity owner,
        ElectionId electionId,
        string designatedAuditorPublicAddress)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.CreateReportAccessGrant,
            JsonSerializer.SerializeToElement(new CreateElectionReportAccessGrantActionPayload(
                owner.PublicSigningAddress,
                designatedAuditorPublicAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string StartElectionCeremony(
        TestIdentity owner,
        ElectionId electionId,
        string profileId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.StartCeremony,
            JsonSerializer.SerializeToElement(new StartElectionCeremonyActionPayload(
                owner.PublicSigningAddress,
                profileId)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string RestartElectionCeremony(
        TestIdentity owner,
        ElectionId electionId,
        string profileId,
        string restartReason)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RestartCeremony,
            JsonSerializer.SerializeToElement(new RestartElectionCeremonyActionPayload(
                owner.PublicSigningAddress,
                profileId,
                restartReason)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string PublishElectionCeremonyTransportKey(
        TestIdentity trustee,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string transportPublicKeyFingerprint)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.PublishCeremonyTransportKey,
            JsonSerializer.SerializeToElement(new PublishElectionCeremonyTransportKeyActionPayload(
                ceremonyVersionId,
                trustee.PublicSigningAddress,
                transportPublicKeyFingerprint)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    public static string JoinElectionCeremony(
        TestIdentity trustee,
        ElectionId electionId,
        Guid ceremonyVersionId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.JoinCeremony,
            JsonSerializer.SerializeToElement(new JoinElectionCeremonyActionPayload(
                ceremonyVersionId,
                trustee.PublicSigningAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    public static string RecordElectionCeremonySelfTestSuccess(
        TestIdentity trustee,
        ElectionId electionId,
        Guid ceremonyVersionId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RecordCeremonySelfTestSuccess,
            JsonSerializer.SerializeToElement(new RecordElectionCeremonySelfTestActionPayload(
                ceremonyVersionId,
                trustee.PublicSigningAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    public static string SubmitElectionCeremonyMaterial(
        TestIdentity trustee,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string? recipientTrusteeUserAddress,
        string messageType,
        string payloadVersion,
        string encryptedPayload,
        string payloadFingerprint,
        string? shareVersion = null)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.SubmitCeremonyMaterial,
            JsonSerializer.SerializeToElement(new SubmitElectionCeremonyMaterialActionPayload(
                ceremonyVersionId,
                trustee.PublicSigningAddress,
                recipientTrusteeUserAddress,
                messageType,
                payloadVersion,
                encryptedPayload,
                payloadFingerprint,
                shareVersion ?? $"share-{payloadFingerprint}")));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    public static string RecordElectionCeremonyValidationFailure(
        TestIdentity owner,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string trusteeUserAddress,
        string validationFailureReason,
        string? evidenceReference = null)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyValidationFailure,
            JsonSerializer.SerializeToElement(new RecordElectionCeremonyValidationFailureActionPayload(
                ceremonyVersionId,
                owner.PublicSigningAddress,
                trusteeUserAddress,
                validationFailureReason,
                evidenceReference)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string CompleteElectionCeremonyTrustee(
        TestIdentity owner,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string trusteeUserAddress,
        string shareVersion,
        string? tallyPublicKeyFingerprint = null)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.CompleteCeremonyTrustee,
            JsonSerializer.SerializeToElement(new CompleteElectionCeremonyTrusteeActionPayload(
                ceremonyVersionId,
                owner.PublicSigningAddress,
                trusteeUserAddress,
                shareVersion,
                tallyPublicKeyFingerprint)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string RecordElectionCeremonyShareExport(
        TestIdentity trustee,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string shareVersion)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyShareExport,
            JsonSerializer.SerializeToElement(new RecordElectionCeremonyShareExportActionPayload(
                ceremonyVersionId,
                trustee.PublicSigningAddress,
                shareVersion)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    public static string RecordElectionCeremonyShareImport(
        TestIdentity trustee,
        ElectionId electionId,
        Guid ceremonyVersionId,
        ElectionId importedElectionId,
        Guid importedCeremonyVersionId,
        string importedTrusteeUserAddress,
        string importedShareVersion)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyShareImport,
            JsonSerializer.SerializeToElement(new RecordElectionCeremonyShareImportActionPayload(
                ceremonyVersionId,
                trustee.PublicSigningAddress,
                importedElectionId,
                importedCeremonyVersionId,
                importedTrusteeUserAddress,
                importedShareVersion)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed election trustee invitation revoke transaction.
    /// </summary>
    public static string RevokeElectionTrusteeInvitation(
        TestIdentity owner,
        ElectionId electionId,
        Guid invitationId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RevokeTrusteeInvitation,
            JsonSerializer.SerializeToElement(new RevokeElectionTrusteeInvitationActionPayload(
                invitationId,
                owner.PublicSigningAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    private static string CreateEncryptedElectionEnvelopeTransaction(
        TestIdentity actor,
        ElectionId electionId,
        EncryptedElectionActionEnvelope actionEnvelope)
    {
        if (!ElectionKeysByElectionId.TryGetValue(electionId.ToString(), out var electionEncryptKeys))
        {
            throw new InvalidOperationException($"Election private key for {electionId} is not available in the integration transaction factory.");
        }

        var envelopeSurface = BuildCurrentEnvelopeSurface(actionEnvelope);
        var unsignedTransaction = EncryptedElectionEnvelopePayloadHandler.CreateNewV21(
            electionId,
            EncryptKeys.Encrypt(electionEncryptKeys.PrivateKey, actor.PublicEncryptAddress),
            electionEncryptKeys.PublicKey,
            EncryptKeys.Encrypt(JsonSerializer.Serialize(actionEnvelope), electionEncryptKeys.PublicKey),
            actionEnvelope.ActionType,
            envelopeSurface.PublicActionPayload,
            envelopeSurface.ActionArtifacts);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            actor.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedTransaction,
            new SignatureInfo(actor.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    private static (JsonElement PublicActionPayload, JsonElement? ActionArtifacts) BuildCurrentEnvelopeSurface(
        EncryptedElectionActionEnvelope actionEnvelope)
    {
        switch (actionEnvelope.ActionType)
        {
            case EncryptedElectionEnvelopeActionTypes.ClaimRosterEntry:
            {
                var claimAction = JsonSerializer.Deserialize<ClaimElectionRosterEntryActionPayload>(
                    actionEnvelope.ActionPayload.GetRawText())!;
                return (
                    JsonSerializer.SerializeToElement(new
                    {
                        claimAction.ActorPublicAddress,
                        claimAction.OrganizationVoterId,
                    }),
                    null);
            }

            case EncryptedElectionEnvelopeActionTypes.InviteTrustee:
            {
                var inviteAction = JsonSerializer.Deserialize<InviteElectionTrusteeActionPayload>(
                    actionEnvelope.ActionPayload.GetRawText())!;
                return (
                    JsonSerializer.SerializeToElement(new
                    {
                        inviteAction.InvitationId,
                        inviteAction.ActorPublicAddress,
                        inviteAction.TrusteeUserAddress,
                        inviteAction.TrusteeDisplayName,
                    }),
                    JsonSerializer.SerializeToElement(new InviteElectionTrusteeActionArtifacts(
                        inviteAction.TrusteeEncryptedElectionPrivateKey)));
            }

            default:
                return (actionEnvelope.ActionPayload, null);
        }
    }

    /// <summary>
    /// Creates a signed open election transaction.
    /// </summary>
    public static string OpenElection(
        TestIdentity owner,
        ElectionId electionId,
        ElectionWarningCode[] requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.OpenElection,
            JsonSerializer.SerializeToElement(new OpenElectionActionPayload(
                owner.PublicSigningAddress,
                requiredWarningCodes,
                frozenEligibleVoterSetHash,
                trusteePolicyExecutionReference,
                reportingPolicyExecutionReference,
                reviewWindowExecutionReference)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed close election transaction.
    /// </summary>
    public static string CloseElection(
        TestIdentity owner,
        ElectionId electionId,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.CloseElection,
            JsonSerializer.SerializeToElement(new CloseElectionActionPayload(
                owner.PublicSigningAddress,
                acceptedBallotSetHash,
                finalEncryptedTallyHash)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed finalize election transaction.
    /// </summary>
    public static string FinalizeElection(
        TestIdentity owner,
        ElectionId electionId,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.FinalizeElection,
            JsonSerializer.SerializeToElement(new FinalizeElectionActionPayload(
                owner.PublicSigningAddress,
                acceptedBallotSetHash,
                finalEncryptedTallyHash)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed finalization share submission transaction.
    /// </summary>
    public static string SubmitElectionFinalizationShare(
        TestIdentity trustee,
        ElectionId electionId,
        Guid finalizationSessionId,
        int shareIndex,
        string shareVersion,
        ElectionFinalizationTargetType targetType,
        Guid claimedCloseArtifactId,
        byte[]? claimedAcceptedBallotSetHash,
        byte[]? claimedFinalEncryptedTallyHash,
        string claimedTargetTallyId,
        Guid? claimedCeremonyVersionId,
        string? claimedTallyPublicKeyFingerprint,
        string shareMaterial,
        Guid? closeCountingJobId = null,
        string? executorSessionPublicKey = null,
        string? executorKeyAlgorithm = null)
    {
        if (!closeCountingJobId.HasValue ||
            string.IsNullOrWhiteSpace(executorSessionPublicKey) ||
            string.IsNullOrWhiteSpace(executorKeyAlgorithm))
        {
            throw new InvalidOperationException(
                "Executor-encrypted trustee share submission requires close-counting job binding and executor session key material.");
        }

        var encryptedExecutorSubmission = EncryptKeys.Encrypt(
            JsonSerializer.Serialize(new CloseCountingExecutorSubmissionPayload(
                closeCountingJobId.Value,
                electionId.ToString(),
                finalizationSessionId,
                trustee.PublicSigningAddress,
                shareIndex,
                shareVersion,
                targetType,
                claimedCloseArtifactId,
                claimedAcceptedBallotSetHash,
                claimedFinalEncryptedTallyHash,
                claimedTargetTallyId,
                claimedCeremonyVersionId,
                claimedTallyPublicKeyFingerprint,
                shareMaterial)),
            executorSessionPublicKey);
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.SubmitFinalizationShare,
            JsonSerializer.SerializeToElement(new SubmitElectionFinalizationShareActionPayload(
                finalizationSessionId,
                trustee.PublicSigningAddress,
                shareIndex,
                shareVersion,
                targetType,
                claimedCloseArtifactId,
                claimedAcceptedBallotSetHash,
                claimedFinalEncryptedTallyHash,
                claimedTargetTallyId,
                claimedCeremonyVersionId,
                claimedTallyPublicKeyFingerprint,
                ShareMaterial: null,
                closeCountingJobId,
                executorKeyAlgorithm,
                encryptedExecutorSubmission)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed governed proposal start transaction.
    /// </summary>
    public static (string Transaction, Guid ProposalId) StartElectionGovernedProposal(
        TestIdentity owner,
        ElectionId electionId,
        ElectionGovernedActionType actionType)
    {
        var proposalId = Guid.NewGuid();
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.StartGovernedProposal,
            JsonSerializer.SerializeToElement(new StartElectionGovernedProposalActionPayload(
                proposalId,
                actionType,
                owner.PublicSigningAddress)));

        return (CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope), proposalId);
    }

    /// <summary>
    /// Creates a signed governed proposal approval transaction.
    /// </summary>
    public static string ApproveElectionGovernedProposal(
        TestIdentity trustee,
        ElectionId electionId,
        Guid proposalId,
        string? approvalNote)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal,
            JsonSerializer.SerializeToElement(new ApproveElectionGovernedProposalActionPayload(
                proposalId,
                trustee.PublicSigningAddress,
                approvalNote)));

        return CreateEncryptedElectionEnvelopeTransaction(trustee, electionId, actionEnvelope);
    }

    /// <summary>
    /// Creates a signed governed proposal retry transaction.
    /// </summary>
    public static string RetryElectionGovernedProposalExecution(
        TestIdentity owner,
        ElectionId electionId,
        Guid proposalId)
    {
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.RetryGovernedProposalExecution,
            JsonSerializer.SerializeToElement(new RetryElectionGovernedProposalExecutionActionPayload(
                proposalId,
                owner.PublicSigningAddress)));

        return CreateEncryptedElectionEnvelopeTransaction(owner, electionId, actionEnvelope);
    }

    public static string LegacyPlaintextOpenElection(
        TestIdentity owner,
        ElectionId electionId,
        ElectionWarningCode[] requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference)
    {
        var unsignedTransaction = OpenElectionPayloadHandler.CreateNew(
            electionId,
            owner.PublicSigningAddress,
            requiredWarningCodes,
            frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference,
            reportingPolicyExecutionReference,
            reviewWindowExecutionReference);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            owner.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<OpenElectionPayload>(
            unsignedTransaction,
            new SignatureInfo(owner.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed personal feed creation transaction.
    /// This must be submitted AFTER identity registration, in the same or subsequent block.
    /// </summary>
    /// <param name="identity">The identity to create a personal feed for.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreatePersonalFeed(TestIdentity identity)
    {
        var (transaction, _) = CreatePersonalFeedWithKey(identity);
        return transaction;
    }

    /// <summary>
    /// Creates a signed personal feed creation transaction and returns the AES key.
    /// This must be submitted AFTER identity registration, in the same or subsequent block.
    /// </summary>
    /// <param name="identity">The identity to create a personal feed for.</param>
    /// <returns>Tuple of (JSON transaction, AES key) for later message encryption.</returns>
    public static (string Transaction, string AesKey) CreatePersonalFeedWithKey(TestIdentity identity)
    {
        // Generate AES key for the feed and encrypt it with the owner's public encryption key
        var feedAesKey = EncryptKeys.GenerateAesKey();
        var encryptedFeedKey = EncryptKeys.Encrypt(feedAesKey, identity.PublicEncryptAddress);

        var unsignedTransaction = NewPersonalFeedPayloadHandler.CreateNewPersonalFeedTransaction(encryptedFeedKey);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            identity.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewPersonalFeedPayload>(
            unsignedTransaction,
            new SignatureInfo(identity.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), feedAesKey);
    }

    /// <summary>
    /// Creates a signed chat feed creation transaction.
    /// </summary>
    /// <param name="initiator">The identity initiating the chat.</param>
    /// <param name="recipient">The identity being invited to chat.</param>
    /// <returns>Tuple of (JSON transaction, FeedId, AES key) for later message encryption.</returns>
    public static (string Transaction, FeedId FeedId, string AesKey) CreateChatFeed(
        TestIdentity initiator,
        TestIdentity recipient)
    {
        var feedId = FeedId.NewFeedId;
        var aesKey = EncryptKeys.GenerateAesKey();

        // Encrypt the AES key for each participant using their public encrypt keys
        var initiatorEncryptedKey = EncryptKeys.Encrypt(aesKey, initiator.PublicEncryptAddress);
        var recipientEncryptedKey = EncryptKeys.Encrypt(aesKey, recipient.PublicEncryptAddress);

        var initiatorParticipant = new ChatFeedParticipant(
            feedId,
            initiator.PublicSigningAddress,
            initiatorEncryptedKey);

        var recipientParticipant = new ChatFeedParticipant(
            feedId,
            recipient.PublicSigningAddress,
            recipientEncryptedKey);

        var payload = new NewChatFeedPayload(
            feedId,
            FeedType.Chat,
            [initiatorParticipant, recipientParticipant]);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewChatFeedPayloadHandler.NewChatFeedPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            initiator.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewChatFeedPayload>(
            unsignedTransaction,
            new SignatureInfo(initiator.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), feedId, aesKey);
    }

    /// <summary>
    /// Creates a signed group feed creation transaction.
    /// </summary>
    /// <param name="creator">The identity creating the group.</param>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="isPublic">Whether the group is public (anyone can join).</param>
    /// <returns>Tuple of (JSON transaction, FeedId, AES key) for later message encryption.</returns>
    public static (string Transaction, FeedId FeedId, string AesKey) CreateGroupFeed(
        TestIdentity creator,
        string groupName,
        bool isPublic = false)
    {
        var feedId = FeedId.NewFeedId;
        var aesKey = EncryptKeys.GenerateAesKey();

        // Encrypt the AES key for the creator using their public encrypt key
        var creatorEncryptedKey = EncryptKeys.Encrypt(aesKey, creator.PublicEncryptAddress);

        var creatorParticipant = new GroupFeedParticipant(
            feedId,
            creator.PublicSigningAddress,
            ParticipantType.Owner,
            creatorEncryptedKey,
            KeyGeneration: 1);

        var payload = new NewGroupFeedPayload(
            feedId,
            groupName,
            Description: $"Test group: {groupName}",
            IsPublic: isPublic,
            [creatorParticipant]);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            creator.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewGroupFeedPayload>(
            unsignedTransaction,
            new SignatureInfo(creator.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), feedId, aesKey);
    }

    /// <summary>
    /// Creates a signed feed message transaction for Personal or Chat feeds.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The feed to send the message to.</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the feed.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateFeedMessage(
        TestIdentity sender,
        FeedId feedId,
        string message,
        string feedAesKey)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            sender.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(sender.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed group feed message transaction with explicit KeyGeneration.
    /// Use this for Group feeds to properly track which key was used for encryption.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The group feed to send the message to.</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the current key generation.</param>
    /// <param name="keyGeneration">The key generation number (0-based) used for encryption.</param>
    /// <returns>Tuple of (JSON transaction, FeedMessageId) for verification.</returns>
    public static (string Transaction, FeedMessageId MessageId) CreateGroupFeedMessage(
        TestIdentity sender,
        FeedId feedId,
        string message,
        string feedAesKey,
        int keyGeneration)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            KeyGeneration: keyGeneration);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            sender.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(sender.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), messageId);
    }

    /// <summary>
    /// FEAT-057: Creates a signed feed message transaction with a SPECIFIC message ID.
    /// Use this for idempotency testing - submit the same transaction multiple times to test duplicate handling.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The feed to send the message to.</param>
    /// <param name="messageId">The specific message ID to use (allows re-submission testing).</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the feed.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateFeedMessageWithId(
        TestIdentity sender,
        FeedId feedId,
        FeedMessageId messageId,
        string message,
        string feedAesKey)
    {
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            sender.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(sender.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// FEAT-066: Creates a signed feed message transaction WITH attachment metadata.
    /// Returns the transaction JSON, message ID, and attachment references for verification.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The feed to send the message to.</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the feed.</param>
    /// <param name="attachments">List of attachment references to include in the payload.</param>
    /// <returns>Tuple of (JSON transaction, FeedMessageId) for verification.</returns>
    public static (string Transaction, FeedMessageId MessageId) CreateFeedMessageWithAttachments(
        TestIdentity sender,
        FeedId feedId,
        string message,
        string feedAesKey,
        List<AttachmentReference> attachments)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            Attachments: attachments);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            sender.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(sender.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), messageId);
    }

    /// <summary>
    /// Creates a signed identity update transaction (display name change).
    /// </summary>
    public static string CreateIdentityUpdate(TestIdentity identity, string newAlias)
    {
        var unsignedTransaction = UpdateIdentityPayloadHandler.CreateNew(newAlias);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            identity.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<UpdateIdentityPayload>(
            unsignedTransaction,
            new SignatureInfo(identity.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed social post creation transaction (FEAT-086).
    /// </summary>
    public static (string Transaction, Guid PostId) CreateSocialPost(
        TestIdentity author,
        string content,
        SocialPostVisibility visibility,
        IReadOnlyCollection<FeedId>? circleFeedIds = null,
        IReadOnlyCollection<SocialPostAttachment>? attachments = null,
        byte[]? authorCommitment = null)
    {
        var postId = Guid.NewGuid();
        var audience = new SocialPostAudience(
            visibility,
            (circleFeedIds ?? Array.Empty<FeedId>()).Select(x => x.ToString()).ToArray());

        var payload = new CreateSocialPostPayload(
            postId,
            postId,
            author.PublicSigningAddress,
            authorCommitment,
            content,
            audience,
            (attachments ?? Array.Empty<SocialPostAttachment>()).ToArray(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            CreateSocialPostPayloadHandler.CreateSocialPostPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            author.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<CreateSocialPostPayload>(
            unsignedTransaction,
            new SignatureInfo(author.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), postId);
    }

    /// <summary>
    /// Creates a signed social thread entry transaction (comment or reply) for FEAT-088.
    /// Social thread content is stored plaintext against the social post feed id.
    /// </summary>
    public static (string Transaction, FeedMessageId MessageId) CreateSocialThreadEntry(
        TestIdentity author,
        Guid postId,
        string content,
        FeedMessageId? replyToMessageId = null)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var payload = new NewFeedMessagePayload(
            messageId,
            new FeedId(postId),
            content,
            ReplyToMessageId: replyToMessageId);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            author.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(author.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), messageId);
    }

    /// <summary>
    /// Creates a signed dev-mode reaction transaction for integration tests.
    /// The payload uses a simple one-hot point encoding so tally deltas remain easy to assert.
    /// </summary>
    public static string CreateDevModeReaction(
        TestIdentity reactor,
        FeedId reactionScopeId,
        FeedMessageId messageId,
        byte[] nullifier,
        int emojiIndex)
    {
        if (emojiIndex < 0 || emojiIndex > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(emojiIndex), "emojiIndex must be between 0 and 5.");
        }

        if (nullifier.Length != 32)
        {
            throw new ArgumentException("Nullifier must be exactly 32 bytes.", nameof(nullifier));
        }

        var curve = new BabyJubJubCurve();
        var generatorX = PadTo32Bytes(curve.Generator.X.ToByteArray(isUnsigned: true, isBigEndian: true));
        var generatorY = PadTo32Bytes(curve.Generator.Y.ToByteArray(isUnsigned: true, isBigEndian: true));
        var identityX = PadTo32Bytes(curve.Identity.X.ToByteArray(isUnsigned: true, isBigEndian: true));
        var identityY = PadTo32Bytes(curve.Identity.Y.ToByteArray(isUnsigned: true, isBigEndian: true));

        var ciphertextC1X = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorX : identityX)
            .ToArray();
        var ciphertextC1Y = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorY : identityY)
            .ToArray();
        var ciphertextC2X = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorX : identityX)
            .ToArray();
        var ciphertextC2Y = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorY : identityY)
            .ToArray();

        var payload = new NewReactionPayload(
            reactionScopeId,
            messageId,
            nullifier,
            ciphertextC1X,
            ciphertextC1Y,
            ciphertextC2X,
            ciphertextC2Y,
            new byte[256],
            "dev-mode-v1",
            BuildReactionBackup(emojiIndex));

        var unsignedTransaction = new UnsignedTransaction<NewReactionPayload>(
            new TransactionId(Guid.NewGuid()),
            NewReactionPayloadHandler.NewReactionPayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            reactor.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewReactionPayload>(
            unsignedTransaction,
            new SignatureInfo(reactor.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed reaction transaction with an externally generated proof payload.
    /// </summary>
    public static string CreateReaction(
        TestIdentity reactor,
        FeedId reactionScopeId,
        FeedMessageId messageId,
        byte[] nullifier,
        byte[][] ciphertextC1X,
        byte[][] ciphertextC1Y,
        byte[][] ciphertextC2X,
        byte[][] ciphertextC2Y,
        byte[] zkProof,
        string circuitVersion,
        byte[]? encryptedEmojiBackup)
    {
        var payload = new NewReactionPayload(
            reactionScopeId,
            messageId,
            nullifier,
            ciphertextC1X,
            ciphertextC1Y,
            ciphertextC2X,
            ciphertextC2Y,
            zkProof,
            circuitVersion,
            encryptedEmojiBackup);

        var unsignedTransaction = new UnsignedTransaction<NewReactionPayload>(
            new TransactionId(Guid.NewGuid()),
            NewReactionPayloadHandler.NewReactionPayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            reactor.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewReactionPayload>(
            unsignedTransaction,
            new SignatureInfo(reactor.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    private static byte[] BuildReactionBackup(int emojiIndex)
    {
        var backup = new byte[32];
        backup[31] = checked((byte)emojiIndex);
        return backup;
    }

    private static byte[] PadTo32Bytes(byte[] input)
    {
        if (input.Length >= 32)
        {
            return input[^32..];
        }

        var padded = new byte[32];
        Array.Copy(input, 0, padded, 32 - input.Length, input.Length);
        return padded;
    }

    /// <summary>
    /// Creates a signed UpdateGroupFeedTitle transaction.
    /// </summary>
    /// <param name="admin">The admin identity issuing the title change.</param>
    /// <param name="feedId">The group feed to rename.</param>
    /// <param name="newTitle">The new title for the group.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateUpdateGroupFeedTitle(TestIdentity admin, FeedId feedId, string newTitle)
    {
        var payload = new UpdateGroupFeedTitlePayload(
            feedId,
            admin.PublicSigningAddress,
            newTitle);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            UpdateGroupFeedTitlePayloadHandler.UpdateGroupFeedTitlePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            admin.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<UpdateGroupFeedTitlePayload>(
            unsignedTransaction,
            new SignatureInfo(admin.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }
}

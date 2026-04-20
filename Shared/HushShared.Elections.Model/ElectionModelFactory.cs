namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionRecord CreateDraftRecord(
        ElectionId electionId,
        string title,
        string? shortDescription,
        string ownerPublicAddress,
        string? externalReferenceCode,
        ElectionClass electionClass,
        ElectionBindingStatus bindingStatus,
        ElectionGovernanceMode governanceMode,
        ElectionDisclosureMode disclosureMode,
        ParticipationPrivacyMode participationPrivacyMode,
        VoteUpdatePolicy voteUpdatePolicy,
        EligibilitySourceType eligibilitySourceType,
        EligibilityMutationPolicy eligibilityMutationPolicy,
        OutcomeRuleDefinition outcomeRule,
        IReadOnlyList<ApprovedClientApplicationRecord> approvedClientApplications,
        string protocolOmegaVersion,
        ReportingPolicy reportingPolicy,
        ReviewWindowPolicy reviewWindowPolicy,
        IReadOnlyList<ElectionOptionDefinition> ownerOptions,
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        int currentDraftRevision = 1,
        int? requiredApprovalCount = null,
        DateTime? createdAt = null,
        OfficialResultVisibilityPolicy officialResultVisibilityPolicy = OfficialResultVisibilityPolicy.ParticipantEncryptedOnly) =>
        CreateDraftRecord(
            electionId,
            title,
            shortDescription,
            ownerPublicAddress,
            externalReferenceCode,
            electionClass,
            bindingStatus,
            selectedProfileId: ElectionSelectableProfileCatalog.GetDefaultProfileId(
                governanceMode,
                bindingStatus),
            selectedProfileDevOnly: bindingStatus == ElectionBindingStatus.NonBinding,
            governanceMode,
            disclosureMode,
            participationPrivacyMode,
            voteUpdatePolicy,
            eligibilitySourceType,
            eligibilityMutationPolicy,
            outcomeRule,
            approvedClientApplications,
            protocolOmegaVersion,
            reportingPolicy,
            reviewWindowPolicy,
            ownerOptions,
            acknowledgedWarningCodes,
            currentDraftRevision,
            requiredApprovalCount,
            createdAt,
            officialResultVisibilityPolicy);

    public static ElectionRecord CreateDraftRecord(
        ElectionId electionId,
        string title,
        string? shortDescription,
        string ownerPublicAddress,
        string? externalReferenceCode,
        ElectionClass electionClass,
        ElectionBindingStatus bindingStatus,
        string selectedProfileId,
        bool selectedProfileDevOnly,
        ElectionGovernanceMode governanceMode,
        ElectionDisclosureMode disclosureMode,
        ParticipationPrivacyMode participationPrivacyMode,
        VoteUpdatePolicy voteUpdatePolicy,
        EligibilitySourceType eligibilitySourceType,
        EligibilityMutationPolicy eligibilityMutationPolicy,
        OutcomeRuleDefinition outcomeRule,
        IReadOnlyList<ApprovedClientApplicationRecord> approvedClientApplications,
        string protocolOmegaVersion,
        ReportingPolicy reportingPolicy,
        ReviewWindowPolicy reviewWindowPolicy,
        IReadOnlyList<ElectionOptionDefinition> ownerOptions,
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        int currentDraftRevision = 1,
        int? requiredApprovalCount = null,
        DateTime? createdAt = null,
        OfficialResultVisibilityPolicy officialResultVisibilityPolicy = OfficialResultVisibilityPolicy.ParticipantEncryptedOnly)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Election title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(ownerPublicAddress))
        {
            throw new ArgumentException("Owner public address is required.", nameof(ownerPublicAddress));
        }

        if (string.IsNullOrWhiteSpace(protocolOmegaVersion))
        {
            throw new ArgumentException("Protocol Omega version is required.", nameof(protocolOmegaVersion));
        }

        if (currentDraftRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentDraftRevision), "Draft revision must be at least 1.");
        }

        var normalizedRequiredApprovalCount = NormalizeRequiredApprovalCount(governanceMode, requiredApprovalCount);
        var canonicalOptions = BuildCanonicalOptions(ownerOptions);
        var normalizedWarnings = NormalizeWarningCodes(acknowledgedWarningCodes);
        var normalizedApplications = CloneApprovedApplications(approvedClientApplications);
        var timestamp = createdAt ?? DateTime.UtcNow;

        return new ElectionRecord(
            electionId,
            title.Trim(),
            NormalizeOptionalText(shortDescription),
            ownerPublicAddress.Trim(),
            NormalizeOptionalText(externalReferenceCode),
            ElectionLifecycleState.Draft,
            electionClass,
            bindingStatus,
            NormalizeRequiredText(selectedProfileId, nameof(selectedProfileId)),
            selectedProfileDevOnly,
            governanceMode,
            disclosureMode,
            participationPrivacyMode,
            voteUpdatePolicy,
            eligibilitySourceType,
            eligibilityMutationPolicy,
            outcomeRule,
            normalizedApplications,
            protocolOmegaVersion.Trim(),
            reportingPolicy,
            reviewWindowPolicy,
            officialResultVisibilityPolicy,
            currentDraftRevision,
            canonicalOptions,
            normalizedWarnings,
            normalizedRequiredApprovalCount,
            timestamp,
            timestamp,
            OpenedAt: null,
            VoteAcceptanceLockedAt: null,
            ClosedAt: null,
            FinalizedAt: null,
            TallyReadyAt: null,
            ClosedProgressStatus: ElectionClosedProgressStatus.None,
            OpenArtifactId: null,
            CloseArtifactId: null,
            TallyReadyArtifactId: null,
            FinalizeArtifactId: null,
            UnofficialResultArtifactId: null,
            OfficialResultArtifactId: null);
    }

    public static ElectionDraftSnapshotRecord CreateDraftSnapshot(
        ElectionRecord election,
        string snapshotReason,
        string recordedByPublicAddress,
        DateTime? recordedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (string.IsNullOrWhiteSpace(snapshotReason))
        {
            throw new ArgumentException("Snapshot reason is required.", nameof(snapshotReason));
        }

        if (string.IsNullOrWhiteSpace(recordedByPublicAddress))
        {
            throw new ArgumentException("Recorded-by public address is required.", nameof(recordedByPublicAddress));
        }

        return new ElectionDraftSnapshotRecord(
            Guid.NewGuid(),
            election.ElectionId,
            election.CurrentDraftRevision,
            new ElectionMetadataSnapshot(
                election.Title,
                election.ShortDescription,
                election.OwnerPublicAddress,
                election.ExternalReferenceCode),
            new ElectionFrozenPolicySnapshot(
                election.ElectionClass,
                election.BindingStatus,
                election.SelectedProfileId,
                election.SelectedProfileDevOnly,
                election.GovernanceMode,
                election.DisclosureMode,
                election.ParticipationPrivacyMode,
                election.VoteUpdatePolicy,
                election.EligibilitySourceType,
                election.EligibilityMutationPolicy,
                election.OutcomeRule,
                CloneApprovedApplications(election.ApprovedClientApplications),
                election.ProtocolOmegaVersion,
                election.ReportingPolicy,
                election.ReviewWindowPolicy,
                election.OfficialResultVisibilityPolicy,
                election.RequiredApprovalCount),
            CloneOptions(election.Options),
            NormalizeWarningCodes(election.AcknowledgedWarningCodes),
            snapshotReason.Trim(),
            recordedAt ?? DateTime.UtcNow,
            recordedByPublicAddress.Trim(),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionBoundaryArtifactRecord CreateBoundaryArtifact(
        ElectionBoundaryArtifactType artifactType,
        ElectionRecord election,
        string recordedByPublicAddress,
        ElectionTrusteeBoundarySnapshot? trusteeSnapshot = null,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null,
        DateTime? recordedAt = null,
        byte[]? frozenEligibleVoterSetHash = null,
        string? trusteePolicyExecutionReference = null,
        string? reportingPolicyExecutionReference = null,
        string? reviewWindowExecutionReference = null,
        int? acceptedBallotCount = null,
        byte[]? acceptedBallotSetHash = null,
        int? publishedBallotCount = null,
        byte[]? publishedBallotStreamHash = null,
        byte[]? finalEncryptedTallyHash = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (string.IsNullOrWhiteSpace(recordedByPublicAddress))
        {
            throw new ArgumentException("Recorded-by public address is required.", nameof(recordedByPublicAddress));
        }

        return new ElectionBoundaryArtifactRecord(
            Guid.NewGuid(),
            election.ElectionId,
            artifactType,
            ResolveLifecycleState(artifactType),
            election.CurrentDraftRevision,
            new ElectionMetadataSnapshot(
                election.Title,
                election.ShortDescription,
                election.OwnerPublicAddress,
                election.ExternalReferenceCode),
            new ElectionFrozenPolicySnapshot(
                election.ElectionClass,
                election.BindingStatus,
                election.SelectedProfileId,
                election.SelectedProfileDevOnly,
                election.GovernanceMode,
                election.DisclosureMode,
                election.ParticipationPrivacyMode,
                election.VoteUpdatePolicy,
                election.EligibilitySourceType,
                election.EligibilityMutationPolicy,
                election.OutcomeRule,
                CloneApprovedApplications(election.ApprovedClientApplications),
                election.ProtocolOmegaVersion,
                election.ReportingPolicy,
                election.ReviewWindowPolicy,
                election.OfficialResultVisibilityPolicy,
                election.RequiredApprovalCount),
            CloneOptions(election.Options),
            NormalizeWarningCodes(election.AcknowledgedWarningCodes),
            CloneTrusteeSnapshot(trusteeSnapshot),
            CloneCeremonySnapshot(ceremonySnapshot),
            CloneBytes(frozenEligibleVoterSetHash),
            NormalizeOptionalText(trusteePolicyExecutionReference),
            NormalizeOptionalText(reportingPolicyExecutionReference),
            NormalizeOptionalText(reviewWindowExecutionReference),
            acceptedBallotCount,
            CloneBytes(acceptedBallotSetHash),
            publishedBallotCount,
            CloneBytes(publishedBallotStreamHash),
            CloneBytes(finalEncryptedTallyHash),
            recordedAt ?? DateTime.UtcNow,
            recordedByPublicAddress.Trim(),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionWarningAcknowledgementRecord CreateWarningAcknowledgement(
        ElectionId electionId,
        ElectionWarningCode warningCode,
        int draftRevision,
        string acknowledgedByPublicAddress,
        DateTime? acknowledgedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (draftRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(draftRevision), "Draft revision must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(acknowledgedByPublicAddress))
        {
            throw new ArgumentException("Acknowledged-by public address is required.", nameof(acknowledgedByPublicAddress));
        }

        return new ElectionWarningAcknowledgementRecord(
            Guid.NewGuid(),
            electionId,
            warningCode,
            draftRevision,
            acknowledgedByPublicAddress.Trim(),
            acknowledgedAt ?? DateTime.UtcNow,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionTrusteeInvitationRecord CreateTrusteeInvitation(
        ElectionId electionId,
        string trusteeUserAddress,
        string? trusteeDisplayName,
        string invitedByPublicAddress,
        int sentAtDraftRevision,
        Guid? invitationId = null,
        DateTime? sentAt = null,
        Guid? linkedMessageId = null,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        if (sentAtDraftRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sentAtDraftRevision), "Sent-at draft revision must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(trusteeUserAddress))
        {
            throw new ArgumentException("Trustee user address is required.", nameof(trusteeUserAddress));
        }

        if (string.IsNullOrWhiteSpace(invitedByPublicAddress))
        {
            throw new ArgumentException("Invited-by public address is required.", nameof(invitedByPublicAddress));
        }

        return new ElectionTrusteeInvitationRecord(
            invitationId ?? Guid.NewGuid(),
            electionId,
            trusteeUserAddress.Trim(),
            NormalizeOptionalText(trusteeDisplayName),
            invitedByPublicAddress.Trim(),
            linkedMessageId,
            ElectionTrusteeInvitationStatus.Pending,
            sentAtDraftRevision,
            sentAt ?? DateTime.UtcNow,
            ResolvedAtDraftRevision: null,
            RespondedAt: null,
            RevokedAt: null,
            latestTransactionId,
            latestBlockHeight,
            latestBlockId);
    }

    public static ElectionTrusteeBoundarySnapshot CreateTrusteeBoundarySnapshot(
        int requiredApprovalCount,
        IReadOnlyList<ElectionTrusteeReference> acceptedTrustees)
    {
        if (requiredApprovalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApprovalCount), "Required approval count must be at least 1.");
        }

        if (acceptedTrustees is null || acceptedTrustees.Count == 0)
        {
            throw new ArgumentException("At least one accepted trustee is required.", nameof(acceptedTrustees));
        }

        var normalizedTrustees = acceptedTrustees
            .Select(x => new ElectionTrusteeReference(
                NormalizeRequiredText(x.TrusteeUserAddress, nameof(acceptedTrustees)),
                NormalizeOptionalText(x.TrusteeDisplayName)))
            .ToArray();

        var duplicateTrustee = normalizedTrustees
            .GroupBy(x => x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateTrustee is not null)
        {
            throw new ArgumentException($"Duplicate accepted trustee detected: {duplicateTrustee.Key}", nameof(acceptedTrustees));
        }

        if (requiredApprovalCount > normalizedTrustees.Length)
        {
            throw new ArgumentException("Required approval count cannot exceed the accepted trustee count.", nameof(requiredApprovalCount));
        }

        return new ElectionTrusteeBoundarySnapshot(
            requiredApprovalCount,
            normalizedTrustees,
            EveryAcceptedTrusteeMustApprove: normalizedTrustees.Length == requiredApprovalCount);
    }

    public static ElectionCeremonyBindingSnapshot CreateCeremonyBindingSnapshot(
        Guid ceremonyVersionId,
        int ceremonyVersionNumber,
        string profileId,
        int boundTrusteeCount,
        int requiredApprovalCount,
        IReadOnlyList<ElectionTrusteeReference> activeTrustees,
        string tallyPublicKeyFingerprint,
        byte[]? tallyPublicKey = null)
    {
        if (ceremonyVersionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ceremonyVersionNumber), "Ceremony version number must be at least 1.");
        }

        if (boundTrusteeCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(boundTrusteeCount), "Bound trustee count must be at least 1.");
        }

        if (activeTrustees is null || activeTrustees.Count == 0)
        {
            throw new ArgumentException("At least one active trustee is required.", nameof(activeTrustees));
        }

        var normalizedTrustees = activeTrustees
            .Select(x => new ElectionTrusteeReference(
                NormalizeRequiredText(x.TrusteeUserAddress, nameof(activeTrustees)),
                NormalizeOptionalText(x.TrusteeDisplayName)))
            .ToArray();

        var duplicateTrustee = normalizedTrustees
            .GroupBy(x => x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateTrustee is not null)
        {
            throw new ArgumentException($"Duplicate active trustee detected: {duplicateTrustee.Key}", nameof(activeTrustees));
        }

        if (requiredApprovalCount < 1 || requiredApprovalCount > normalizedTrustees.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApprovalCount), "Required approval count must be between 1 and the active trustee count.");
        }

        return new ElectionCeremonyBindingSnapshot(
            ceremonyVersionId,
            ceremonyVersionNumber,
            NormalizeRequiredText(profileId, nameof(profileId)),
            boundTrusteeCount,
            requiredApprovalCount,
            normalizedTrustees,
            EveryActiveTrusteeMustApprove: normalizedTrustees.Length == requiredApprovalCount,
            NormalizeRequiredText(tallyPublicKeyFingerprint, nameof(tallyPublicKeyFingerprint)),
            CloneBytes(tallyPublicKey));
    }

    public static ElectionGovernedProposalRecord CreateGovernedProposal(
        ElectionRecord election,
        ElectionGovernedActionType actionType,
        string proposedByPublicAddress,
        Guid? preassignedProposalId = null,
        DateTime? createdAt = null,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        if (string.IsNullOrWhiteSpace(proposedByPublicAddress))
        {
            throw new ArgumentException("Proposed-by public address is required.", nameof(proposedByPublicAddress));
        }

        return new ElectionGovernedProposalRecord(
            preassignedProposalId ?? Guid.NewGuid(),
            election.ElectionId,
            actionType,
            election.LifecycleState,
            proposedByPublicAddress.Trim(),
            createdAt ?? DateTime.UtcNow,
            ElectionGovernedProposalExecutionStatus.WaitingForApprovals,
            LastExecutionAttemptedAt: null,
            ExecutedAt: null,
            ExecutionFailureReason: null,
            LastExecutionTriggeredByPublicAddress: null,
            latestTransactionId,
            latestBlockHeight,
            latestBlockId);
    }

    public static ElectionGovernedProposalApprovalRecord CreateGovernedProposalApproval(
        ElectionGovernedProposalRecord proposal,
        string trusteeUserAddress,
        string? trusteeDisplayName,
        string? approvalNote,
        DateTime? approvedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (string.IsNullOrWhiteSpace(trusteeUserAddress))
        {
            throw new ArgumentException("Trustee user address is required.", nameof(trusteeUserAddress));
        }

        return new ElectionGovernedProposalApprovalRecord(
            Guid.NewGuid(),
            proposal.Id,
            proposal.ElectionId,
            proposal.ActionType,
            proposal.LifecycleStateAtCreation,
            trusteeUserAddress.Trim(),
            NormalizeOptionalText(trusteeDisplayName),
            NormalizeOptionalText(approvalNote),
            approvedAt ?? DateTime.UtcNow,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionCeremonyProfileRecord CreateCeremonyProfile(
        string profileId,
        string displayName,
        string description,
        string providerKey,
        string profileVersion,
        int trusteeCount,
        int requiredApprovalCount,
        bool devOnly,
        DateTime? registeredAt = null)
    {
        if (trusteeCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(trusteeCount), "Trustee count must be at least 1.");
        }

        if (requiredApprovalCount < 1 || requiredApprovalCount > trusteeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApprovalCount), "Required approval count must be between 1 and the trustee count.");
        }

        var timestamp = registeredAt ?? DateTime.UtcNow;

        return new ElectionCeremonyProfileRecord(
            NormalizeRequiredText(profileId, nameof(profileId)),
            NormalizeRequiredText(displayName, nameof(displayName)),
            NormalizeRequiredText(description, nameof(description)),
            NormalizeRequiredText(providerKey, nameof(providerKey)),
            NormalizeRequiredText(profileVersion, nameof(profileVersion)),
            trusteeCount,
            requiredApprovalCount,
            devOnly,
            timestamp,
            timestamp);
    }

    public static ElectionCeremonyVersionRecord CreateCeremonyVersion(
        ElectionId electionId,
        int versionNumber,
        string profileId,
        int requiredApprovalCount,
        IReadOnlyList<ElectionTrusteeReference> boundTrustees,
        string startedByPublicAddress,
        DateTime? startedAt = null)
    {
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Ceremony version number must be at least 1.");
        }

        if (boundTrustees is null || boundTrustees.Count == 0)
        {
            throw new ArgumentException("At least one bound trustee is required.", nameof(boundTrustees));
        }

        var normalizedTrustees = boundTrustees
            .Select(x => new ElectionTrusteeReference(
                NormalizeRequiredText(x.TrusteeUserAddress, nameof(boundTrustees)),
                NormalizeOptionalText(x.TrusteeDisplayName)))
            .ToArray();

        var duplicateTrustee = normalizedTrustees
            .GroupBy(x => x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateTrustee is not null)
        {
            throw new ArgumentException($"Duplicate bound trustee detected: {duplicateTrustee.Key}", nameof(boundTrustees));
        }

        if (requiredApprovalCount < 1 || requiredApprovalCount > normalizedTrustees.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApprovalCount), "Required approval count must be between 1 and the bound trustee count.");
        }

        return new ElectionCeremonyVersionRecord(
            Guid.NewGuid(),
            electionId,
            versionNumber,
            NormalizeRequiredText(profileId, nameof(profileId)),
            ElectionCeremonyVersionStatus.InProgress,
            normalizedTrustees.Length,
            requiredApprovalCount,
            normalizedTrustees,
            NormalizeRequiredText(startedByPublicAddress, nameof(startedByPublicAddress)),
            startedAt ?? DateTime.UtcNow,
            CompletedAt: null,
            SupersededAt: null,
            SupersededReason: null,
            TallyPublicKeyFingerprint: null,
            TallyPublicKey: null);
    }

    public static ElectionCeremonyTranscriptEventRecord CreateCeremonyTranscriptEvent(
        ElectionId electionId,
        Guid ceremonyVersionId,
        int versionNumber,
        ElectionCeremonyTranscriptEventType eventType,
        string eventSummary,
        DateTime? occurredAt = null,
        string? actorPublicAddress = null,
        string? trusteeUserAddress = null,
        string? trusteeDisplayName = null,
        ElectionTrusteeCeremonyState? trusteeState = null,
        string? evidenceReference = null,
        string? restartReason = null,
        string? tallyPublicKeyFingerprint = null)
    {
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Ceremony version number must be at least 1.");
        }

        return new ElectionCeremonyTranscriptEventRecord(
            Guid.NewGuid(),
            electionId,
            ceremonyVersionId,
            versionNumber,
            eventType,
            NormalizeOptionalText(actorPublicAddress),
            NormalizeOptionalText(trusteeUserAddress),
            NormalizeOptionalText(trusteeDisplayName),
            trusteeState,
            NormalizeRequiredText(eventSummary, nameof(eventSummary)),
            NormalizeOptionalText(evidenceReference),
            NormalizeOptionalText(restartReason),
            NormalizeOptionalText(tallyPublicKeyFingerprint),
            occurredAt ?? DateTime.UtcNow);
    }

    public static ElectionCeremonyMessageEnvelopeRecord CreateCeremonyMessageEnvelope(
        ElectionId electionId,
        Guid ceremonyVersionId,
        int versionNumber,
        string profileId,
        string senderTrusteeUserAddress,
        string? recipientTrusteeUserAddress,
        string messageType,
        string payloadVersion,
        byte[] encryptedPayload,
        string payloadFingerprint,
        DateTime? submittedAt = null)
    {
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Ceremony version number must be at least 1.");
        }

        if (encryptedPayload is null || encryptedPayload.Length == 0)
        {
            throw new ArgumentException("Encrypted payload is required.", nameof(encryptedPayload));
        }

        return new ElectionCeremonyMessageEnvelopeRecord(
            Guid.NewGuid(),
            electionId,
            ceremonyVersionId,
            versionNumber,
            NormalizeRequiredText(profileId, nameof(profileId)),
            NormalizeRequiredText(senderTrusteeUserAddress, nameof(senderTrusteeUserAddress)),
            NormalizeOptionalText(recipientTrusteeUserAddress),
            NormalizeRequiredText(messageType, nameof(messageType)),
            NormalizeRequiredText(payloadVersion, nameof(payloadVersion)),
            CloneBytes(encryptedPayload)!,
            NormalizeRequiredText(payloadFingerprint, nameof(payloadFingerprint)),
            submittedAt ?? DateTime.UtcNow);
    }

    public static ElectionCeremonyTrusteeStateRecord CreateCeremonyTrusteeState(
        ElectionId electionId,
        Guid ceremonyVersionId,
        string trusteeUserAddress,
        string? trusteeDisplayName,
        ElectionTrusteeCeremonyState state = ElectionTrusteeCeremonyState.CeremonyNotStarted,
        DateTime? recordedAt = null)
    {
        var timestamp = recordedAt ?? DateTime.UtcNow;

        return new ElectionCeremonyTrusteeStateRecord(
            Guid.NewGuid(),
            electionId,
            ceremonyVersionId,
            NormalizeRequiredText(trusteeUserAddress, nameof(trusteeUserAddress)),
            NormalizeOptionalText(trusteeDisplayName),
            state,
            TransportPublicKeyFingerprint: null,
            TransportPublicKeyPublishedAt: null,
            JoinedAt: null,
            SelfTestSucceededAt: null,
            MaterialSubmittedAt: null,
            ValidationFailedAt: null,
            ValidationFailureReason: null,
            CompletedAt: null,
            RemovedAt: null,
            ShareVersion: null,
            LastUpdatedAt: timestamp,
            CloseCountingPublicCommitment: null);
    }

    public static ElectionCeremonyShareCustodyRecord CreateCeremonyShareCustodyRecord(
        ElectionId electionId,
        Guid ceremonyVersionId,
        string trusteeUserAddress,
        string shareVersion,
        bool passwordProtected = true,
        DateTime? recordedAt = null)
    {
        var timestamp = recordedAt ?? DateTime.UtcNow;

        return new ElectionCeremonyShareCustodyRecord(
            Guid.NewGuid(),
            electionId,
            ceremonyVersionId,
            NormalizeRequiredText(trusteeUserAddress, nameof(trusteeUserAddress)),
            NormalizeRequiredText(shareVersion, nameof(shareVersion)),
            passwordProtected,
            ElectionCeremonyShareCustodyStatus.NotExported,
            LastExportedAt: null,
            LastImportedAt: null,
            LastImportFailedAt: null,
            LastImportFailureReason: null,
            LastUpdatedAt: timestamp);
    }

    public static ElectionFinalizationSessionRecord CreateFinalizationSession(
        ElectionRecord election,
        Guid closeArtifactId,
        byte[] acceptedBallotSetHash,
        byte[] finalEncryptedTallyHash,
        ElectionFinalizationSessionPurpose sessionPurpose,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot,
        int requiredShareCount,
        IReadOnlyList<ElectionTrusteeReference> eligibleTrustees,
        string createdByPublicAddress,
        Guid? governedProposalId = null,
        DateTime? createdAt = null,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(election);

        if (acceptedBallotSetHash is null || acceptedBallotSetHash.Length == 0)
        {
            throw new ArgumentException("Accepted ballot set hash is required.", nameof(acceptedBallotSetHash));
        }

        if (finalEncryptedTallyHash is null || finalEncryptedTallyHash.Length == 0)
        {
            throw new ArgumentException("Final encrypted tally hash is required.", nameof(finalEncryptedTallyHash));
        }

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold)
        {
            if (ceremonySnapshot is null)
            {
                throw new ArgumentException("Ceremony snapshot is required for trustee-threshold finalization.", nameof(ceremonySnapshot));
            }

            if (requiredShareCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredShareCount), "Required share count must be at least 1.");
            }

            if (eligibleTrustees is null || eligibleTrustees.Count == 0)
            {
                throw new ArgumentException("Eligible trustees are required for trustee-threshold finalization.", nameof(eligibleTrustees));
            }
        }

        var timestamp = createdAt ?? DateTime.UtcNow;
        var normalizedEligibleTrustees = eligibleTrustees is null || eligibleTrustees.Count == 0
            ? Array.Empty<ElectionTrusteeReference>()
            : eligibleTrustees
                .Select(x => new ElectionTrusteeReference(
                    NormalizeRequiredText(x.TrusteeUserAddress, nameof(eligibleTrustees)),
                    NormalizeOptionalText(x.TrusteeDisplayName)))
                .ToArray();

        return new ElectionFinalizationSessionRecord(
            Guid.NewGuid(),
            election.ElectionId,
            governedProposalId,
            election.GovernanceMode,
            sessionPurpose,
            closeArtifactId,
            CloneBytes(acceptedBallotSetHash)!,
            CloneBytes(finalEncryptedTallyHash)!,
            Convert.ToHexString(finalEncryptedTallyHash),
            CloneCeremonySnapshot(ceremonySnapshot),
            requiredShareCount,
            normalizedEligibleTrustees,
            ElectionFinalizationSessionStatus.AwaitingShares,
            timestamp,
            NormalizeRequiredText(createdByPublicAddress, nameof(createdByPublicAddress)),
            CompletedAt: null,
            ReleaseEvidenceId: null,
            latestTransactionId,
            latestBlockHeight,
            latestBlockId);
    }

    public static ElectionFinalizationShareRecord CreateAcceptedFinalizationShare(
        Guid finalizationSessionId,
        ElectionId electionId,
        string trusteeUserAddress,
        string? trusteeDisplayName,
        string submittedByPublicAddress,
        int shareIndex,
        string shareVersion,
        ElectionFinalizationTargetType targetType,
        Guid claimedCloseArtifactId,
        byte[] claimedAcceptedBallotSetHash,
        byte[] claimedFinalEncryptedTallyHash,
        string claimedTargetTallyId,
        Guid? claimedCeremonyVersionId,
        string? claimedTallyPublicKeyFingerprint,
        string shareMaterial,
        Guid? closeCountingJobId = null,
        string? executorKeyAlgorithm = null,
        DateTime? submittedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null) =>
        CreateFinalizationShare(
            finalizationSessionId,
            electionId,
            trusteeUserAddress,
            trusteeDisplayName,
            submittedByPublicAddress,
            shareIndex,
            shareVersion,
            targetType,
            claimedCloseArtifactId,
            claimedAcceptedBallotSetHash,
            claimedFinalEncryptedTallyHash,
            claimedTargetTallyId,
            claimedCeremonyVersionId,
            claimedTallyPublicKeyFingerprint,
            shareMaterial,
            closeCountingJobId,
            executorKeyAlgorithm,
            ElectionFinalizationShareStatus.Accepted,
            failureCode: null,
            failureReason: null,
            submittedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

    public static ElectionFinalizationShareRecord CreateRejectedFinalizationShare(
        Guid finalizationSessionId,
        ElectionId electionId,
        string trusteeUserAddress,
        string? trusteeDisplayName,
        string submittedByPublicAddress,
        int shareIndex,
        string shareVersion,
        ElectionFinalizationTargetType targetType,
        Guid claimedCloseArtifactId,
        byte[] claimedAcceptedBallotSetHash,
        byte[] claimedFinalEncryptedTallyHash,
        string claimedTargetTallyId,
        Guid? claimedCeremonyVersionId,
        string? claimedTallyPublicKeyFingerprint,
        string shareMaterial,
        string failureCode,
        string failureReason,
        Guid? closeCountingJobId = null,
        string? executorKeyAlgorithm = null,
        DateTime? submittedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("Failure code is required.", nameof(failureCode));
        }

        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));
        }

        return CreateFinalizationShare(
            finalizationSessionId,
            electionId,
            trusteeUserAddress,
            trusteeDisplayName,
            submittedByPublicAddress,
            shareIndex,
            shareVersion,
            targetType,
            claimedCloseArtifactId,
            claimedAcceptedBallotSetHash,
            claimedFinalEncryptedTallyHash,
            claimedTargetTallyId,
            claimedCeremonyVersionId,
            claimedTallyPublicKeyFingerprint,
            shareMaterial,
            closeCountingJobId,
            executorKeyAlgorithm,
            ElectionFinalizationShareStatus.Rejected,
            failureCode.Trim(),
            failureReason.Trim(),
            submittedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionFinalizationReleaseEvidenceRecord CreateFinalizationReleaseEvidence(
        ElectionFinalizationSessionRecord session,
        IReadOnlyList<ElectionTrusteeReference> acceptedTrustees,
        string completedByPublicAddress,
        DateTime? completedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var timestamp = completedAt ?? DateTime.UtcNow;
        var normalizedTrustees = acceptedTrustees is null || acceptedTrustees.Count == 0
            ? Array.Empty<ElectionTrusteeReference>()
            : acceptedTrustees
                .Select(x => new ElectionTrusteeReference(
                    NormalizeRequiredText(x.TrusteeUserAddress, nameof(acceptedTrustees)),
                    NormalizeOptionalText(x.TrusteeDisplayName)))
                .ToArray();

        return new ElectionFinalizationReleaseEvidenceRecord(
            Guid.NewGuid(),
            session.Id,
            session.ElectionId,
            session.SessionPurpose,
            ElectionFinalizationReleaseMode.AggregateTallyOnly,
            session.CloseArtifactId,
            CloneBytes(session.AcceptedBallotSetHash)!,
            CloneBytes(session.FinalEncryptedTallyHash)!,
            session.TargetTallyId,
            normalizedTrustees.Length,
            normalizedTrustees,
            timestamp,
            NormalizeRequiredText(completedByPublicAddress, nameof(completedByPublicAddress)),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    private static ElectionFinalizationShareRecord CreateFinalizationShare(
        Guid finalizationSessionId,
        ElectionId electionId,
        string trusteeUserAddress,
        string? trusteeDisplayName,
        string submittedByPublicAddress,
        int shareIndex,
        string shareVersion,
        ElectionFinalizationTargetType targetType,
        Guid claimedCloseArtifactId,
        byte[] claimedAcceptedBallotSetHash,
        byte[] claimedFinalEncryptedTallyHash,
        string claimedTargetTallyId,
        Guid? claimedCeremonyVersionId,
        string? claimedTallyPublicKeyFingerprint,
        string shareMaterial,
        Guid? closeCountingJobId,
        string? executorKeyAlgorithm,
        ElectionFinalizationShareStatus status,
        string? failureCode,
        string? failureReason,
        DateTime? submittedAt,
        Guid? sourceTransactionId,
        long? sourceBlockHeight,
        Guid? sourceBlockId)
    {
        if (shareIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(shareIndex), "Share index must be at least 1.");
        }

        if (claimedAcceptedBallotSetHash is null)
        {
            throw new ArgumentNullException(nameof(claimedAcceptedBallotSetHash));
        }

        if (claimedFinalEncryptedTallyHash is null)
        {
            throw new ArgumentNullException(nameof(claimedFinalEncryptedTallyHash));
        }

        var normalizedShareMaterial = NormalizeOptionalText(shareMaterial) ?? string.Empty;

        return new ElectionFinalizationShareRecord(
            Guid.NewGuid(),
            finalizationSessionId,
            electionId,
            NormalizeRequiredText(trusteeUserAddress, nameof(trusteeUserAddress)),
            NormalizeOptionalText(trusteeDisplayName),
            NormalizeRequiredText(submittedByPublicAddress, nameof(submittedByPublicAddress)),
            shareIndex,
            NormalizeOptionalText(shareVersion) ?? string.Empty,
            targetType,
            claimedCloseArtifactId,
            CloneBytes(claimedAcceptedBallotSetHash) ?? Array.Empty<byte>(),
            CloneBytes(claimedFinalEncryptedTallyHash) ?? Array.Empty<byte>(),
            NormalizeOptionalText(claimedTargetTallyId) ?? string.Empty,
            claimedCeremonyVersionId,
            NormalizeOptionalText(claimedTallyPublicKeyFingerprint),
            closeCountingJobId,
            NormalizeOptionalText(executorKeyAlgorithm),
            normalizedShareMaterial,
            ComputeHashHex(normalizedShareMaterial),
            status,
            NormalizeOptionalText(failureCode),
            NormalizeOptionalText(failureReason),
            submittedAt ?? DateTime.UtcNow,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    private static string ComputeHashHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static int? NormalizeRequiredApprovalCount(
        ElectionGovernanceMode governanceMode,
        int? requiredApprovalCount)
    {
        if (governanceMode == ElectionGovernanceMode.AdminOnly)
        {
            return null;
        }

        if (!requiredApprovalCount.HasValue || requiredApprovalCount.Value < 1)
        {
            throw new ArgumentException("Trustee-threshold elections require a required approval count of at least 1.", nameof(requiredApprovalCount));
        }

        return requiredApprovalCount.Value;
    }

    private static IReadOnlyList<ElectionOptionDefinition> BuildCanonicalOptions(IReadOnlyList<ElectionOptionDefinition> ownerOptions)
    {
        if (ownerOptions is null)
        {
            throw new ArgumentNullException(nameof(ownerOptions));
        }

        var normalizedOwnerOptions = ownerOptions
            .Select(x => new ElectionOptionDefinition(
                NormalizeRequiredText(x.OptionId, nameof(ownerOptions)),
                NormalizeRequiredText(x.DisplayLabel, nameof(ownerOptions)),
                NormalizeOptionalText(x.ShortDescription),
                x.BallotOrder,
                x.IsBlankOption))
            .ToList();

        if (normalizedOwnerOptions.Any(x => x.IsBlankOption))
        {
            throw new ArgumentException("Owner options must not mark themselves as the reserved blank vote option.", nameof(ownerOptions));
        }

        if (normalizedOwnerOptions.Any(x => string.Equals(x.OptionId, ElectionOptionDefinition.ReservedBlankOptionId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Owner options must not reuse the reserved blank option id.", nameof(ownerOptions));
        }

        var duplicateOptionId = normalizedOwnerOptions
            .GroupBy(x => x.OptionId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateOptionId is not null)
        {
            throw new ArgumentException($"Duplicate election option id detected: {duplicateOptionId.Key}", nameof(ownerOptions));
        }

        var duplicateOrder = normalizedOwnerOptions
            .GroupBy(x => x.BallotOrder)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateOrder is not null)
        {
            throw new ArgumentException($"Duplicate election option ballot order detected: {duplicateOrder.Key}", nameof(ownerOptions));
        }

        var orderedOptions = normalizedOwnerOptions
            .OrderBy(x => x.BallotOrder)
            .ToList();

        var blankOrder = orderedOptions.Count == 0
            ? 0
            : orderedOptions[^1].BallotOrder + 1;
        orderedOptions.Add(ElectionOptionDefinition.CreateReservedBlankVote(blankOrder));

        return orderedOptions.ToArray();
    }

    private static IReadOnlyList<ElectionWarningCode> NormalizeWarningCodes(IReadOnlyList<ElectionWarningCode>? warningCodes)
    {
        if (warningCodes is null || warningCodes.Count == 0)
        {
            return Array.Empty<ElectionWarningCode>();
        }

        return warningCodes
            .Distinct()
            .OrderBy(x => (int)x)
            .ToArray();
    }

    private static IReadOnlyList<ApprovedClientApplicationRecord> CloneApprovedApplications(
        IReadOnlyList<ApprovedClientApplicationRecord>? applications)
    {
        if (applications is null || applications.Count == 0)
        {
            return Array.Empty<ApprovedClientApplicationRecord>();
        }

        return applications
            .Select(x => new ApprovedClientApplicationRecord(
                NormalizeRequiredText(x.ApplicationId, nameof(applications)),
                NormalizeRequiredText(x.Version, nameof(applications))))
            .ToArray();
    }

    private static IReadOnlyList<ElectionOptionDefinition> CloneOptions(IReadOnlyList<ElectionOptionDefinition> options) =>
        options
            .Select(x => new ElectionOptionDefinition(
                x.OptionId,
                x.DisplayLabel,
                x.ShortDescription,
                x.BallotOrder,
                x.IsBlankOption))
            .ToArray();

    private static ElectionTrusteeBoundarySnapshot? CloneTrusteeSnapshot(ElectionTrusteeBoundarySnapshot? snapshot) =>
        snapshot is null
            ? null
            : new ElectionTrusteeBoundarySnapshot(
                snapshot.RequiredApprovalCount,
                snapshot.AcceptedTrustees
                    .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
                    .ToArray(),
                snapshot.EveryAcceptedTrusteeMustApprove);

    private static ElectionCeremonyBindingSnapshot? CloneCeremonySnapshot(ElectionCeremonyBindingSnapshot? snapshot) =>
        snapshot is null
            ? null
            : new ElectionCeremonyBindingSnapshot(
                snapshot.CeremonyVersionId,
                snapshot.CeremonyVersionNumber,
                snapshot.ProfileId,
                snapshot.BoundTrusteeCount,
                snapshot.RequiredApprovalCount,
                snapshot.ActiveTrustees
                    .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
                    .ToArray(),
                snapshot.EveryActiveTrusteeMustApprove,
                snapshot.TallyPublicKeyFingerprint,
                CloneBytes(snapshot.TallyPublicKey));

    private static byte[]? CloneBytes(byte[]? value) => value is null ? null : value.ToArray();

    private static ElectionLifecycleState ResolveLifecycleState(ElectionBoundaryArtifactType artifactType) =>
        artifactType switch
        {
            ElectionBoundaryArtifactType.Open => ElectionLifecycleState.Open,
            ElectionBoundaryArtifactType.Close => ElectionLifecycleState.Closed,
            ElectionBoundaryArtifactType.TallyReady => ElectionLifecycleState.Closed,
            ElectionBoundaryArtifactType.Finalize => ElectionLifecycleState.Finalized,
            _ => throw new ArgumentOutOfRangeException(nameof(artifactType), artifactType, "Unsupported boundary artifact type."),
        };

    private static string NormalizeRequiredText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

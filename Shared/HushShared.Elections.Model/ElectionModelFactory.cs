namespace HushShared.Elections.Model;

public static class ElectionModelFactory
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
        DateTime? createdAt = null)
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
            currentDraftRevision,
            canonicalOptions,
            normalizedWarnings,
            normalizedRequiredApprovalCount,
            timestamp,
            timestamp,
            OpenedAt: null,
            ClosedAt: null,
            FinalizedAt: null,
            TallyReadyAt: null,
            OpenArtifactId: null,
            CloseArtifactId: null,
            FinalizeArtifactId: null);
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
        DateTime? recordedAt = null,
        byte[]? frozenEligibleVoterSetHash = null,
        string? trusteePolicyExecutionReference = null,
        string? reportingPolicyExecutionReference = null,
        string? reviewWindowExecutionReference = null,
        byte[]? acceptedBallotSetHash = null,
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
                election.RequiredApprovalCount),
            CloneOptions(election.Options),
            NormalizeWarningCodes(election.AcknowledgedWarningCodes),
            CloneTrusteeSnapshot(trusteeSnapshot),
            CloneBytes(frozenEligibleVoterSetHash),
            NormalizeOptionalText(trusteePolicyExecutionReference),
            NormalizeOptionalText(reportingPolicyExecutionReference),
            NormalizeOptionalText(reviewWindowExecutionReference),
            CloneBytes(acceptedBallotSetHash),
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
            Guid.NewGuid(),
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

    public static ElectionGovernedProposalRecord CreateGovernedProposal(
        ElectionRecord election,
        ElectionGovernedActionType actionType,
        string proposedByPublicAddress,
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
            Guid.NewGuid(),
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

    private static byte[]? CloneBytes(byte[]? value) => value is null ? null : value.ToArray();

    private static ElectionLifecycleState ResolveLifecycleState(ElectionBoundaryArtifactType artifactType) =>
        artifactType switch
        {
            ElectionBoundaryArtifactType.Open => ElectionLifecycleState.Open,
            ElectionBoundaryArtifactType.Close => ElectionLifecycleState.Closed,
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

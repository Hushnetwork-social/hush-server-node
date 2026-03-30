using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HushNetwork.proto;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

internal static class ElectionGrpcMappings
{
    public static ElectionDraftSpecification ToDomain(this ElectionDraftInput draft)
    {
        var outcomeRule = draft.OutcomeRule ?? new OutcomeRule();

        return new ElectionDraftSpecification(
            Title: draft.Title ?? string.Empty,
            ShortDescription: NormalizeOptionalString(draft.ShortDescription),
            ExternalReferenceCode: NormalizeOptionalString(draft.ExternalReferenceCode),
            ElectionClass: (ElectionClass)(int)draft.ElectionClass,
            BindingStatus: (ElectionBindingStatus)(int)draft.BindingStatus,
            GovernanceMode: (ElectionGovernanceMode)(int)draft.GovernanceMode,
            DisclosureMode: (ElectionDisclosureMode)(int)draft.DisclosureMode,
            ParticipationPrivacyMode: (ParticipationPrivacyMode)(int)draft.ParticipationPrivacyMode,
            VoteUpdatePolicy: (VoteUpdatePolicy)(int)draft.VoteUpdatePolicy,
            EligibilitySourceType: (EligibilitySourceType)(int)draft.EligibilitySourceType,
            EligibilityMutationPolicy: (EligibilityMutationPolicy)(int)draft.EligibilityMutationPolicy,
            OutcomeRule: new OutcomeRuleDefinition(
                (OutcomeRuleKind)(int)outcomeRule.Kind,
                outcomeRule.TemplateKey ?? string.Empty,
                outcomeRule.SeatCount,
                outcomeRule.BlankVoteCountsForTurnout,
                outcomeRule.BlankVoteExcludedFromWinnerSelection,
                outcomeRule.BlankVoteExcludedFromThresholdDenominator,
                outcomeRule.TieResolutionRule ?? string.Empty,
                outcomeRule.CalculationBasis ?? string.Empty),
            ApprovedClientApplications: draft.ApprovedClientApplications
                .Select(x => new ApprovedClientApplicationRecord(x.ApplicationId, x.Version))
                .ToArray(),
            ProtocolOmegaVersion: draft.ProtocolOmegaVersion ?? string.Empty,
            ReportingPolicy: (ReportingPolicy)(int)draft.ReportingPolicy,
            ReviewWindowPolicy: (ReviewWindowPolicy)(int)draft.ReviewWindowPolicy,
            OwnerOptions: draft.OwnerOptions
                .Select(x => new ElectionOptionDefinition(
                    x.OptionId,
                    x.DisplayLabel,
                    NormalizeOptionalString(x.ShortDescription),
                    x.BallotOrder,
                    x.IsBlankOption))
                .ToArray(),
            AcknowledgedWarningCodes: draft.AcknowledgedWarningCodes.Select(x => (ElectionWarningCode)(int)x).ToArray(),
            RequiredApprovalCount: draft.HasRequiredApprovalCount ? draft.RequiredApprovalCount : null);
    }

    public static ElectionCommandResponse ToProto(this ElectionCommandResult result)
    {
        var response = new ElectionCommandResponse
        {
            Success = result.IsSuccess,
            ErrorCode = (ElectionCommandErrorCodeProto)(int)result.ErrorCode,
            ErrorMessage = result.ErrorMessage ?? string.Empty,
        };

        response.ValidationErrors.AddRange(result.ValidationErrors);

        if (result.Election is not null)
        {
            response.Election = result.Election.ToProto();
        }

        if (result.DraftSnapshot is not null)
        {
            response.DraftSnapshot = result.DraftSnapshot.ToProto();
        }

        if (result.BoundaryArtifact is not null)
        {
            response.BoundaryArtifact = result.BoundaryArtifact.ToProto();
        }

        if (result.TrusteeInvitation is not null)
        {
            response.TrusteeInvitation = result.TrusteeInvitation.ToProto();
        }

        if (result.GovernedProposal is not null)
        {
            response.GovernedProposal = result.GovernedProposal.ToProto();
        }

        if (result.GovernedProposalApproval is not null)
        {
            response.GovernedProposalApproval = result.GovernedProposalApproval.ToProto();
        }

        if (result.CeremonyProfile is not null)
        {
            response.CeremonyProfile = result.CeremonyProfile.ToProto();
        }

        if (result.CeremonyVersion is not null)
        {
            response.CeremonyVersion = result.CeremonyVersion.ToProto();
        }

        response.CeremonyTranscriptEvents.AddRange(result.CeremonyTranscriptEvents.Select(x => x.ToProto()));

        if (result.CeremonyTrusteeState is not null)
        {
            response.CeremonyTrusteeState = result.CeremonyTrusteeState.ToProto();
        }

        if (result.CeremonyMessageEnvelope is not null)
        {
            response.CeremonyMessageEnvelope = result.CeremonyMessageEnvelope.ToProto();
        }

        if (result.CeremonyShareCustody is not null)
        {
            response.CeremonyShareCustody = result.CeremonyShareCustody.ToProto();
        }

        if (result.FinalizationSession is not null)
        {
            response.FinalizationSession = result.FinalizationSession.ToProto();
        }

        if (result.FinalizationShare is not null)
        {
            response.FinalizationShare = result.FinalizationShare.ToProto();
        }

        if (result.FinalizationReleaseEvidence is not null)
        {
            response.FinalizationReleaseEvidence = result.FinalizationReleaseEvidence.ToProto();
        }

        return response;
    }

    public static GetElectionOpenReadinessResponse ToProto(this ElectionOpenValidationResult result)
    {
        var response = new GetElectionOpenReadinessResponse
        {
            IsReadyToOpen = result.IsReadyToOpen,
        };

        response.ValidationErrors.AddRange(result.ValidationErrors);
        response.RequiredWarningCodes.AddRange(result.RequiredWarningCodes.Select(x => (ElectionWarningCodeProto)(int)x));
        response.MissingWarningAcknowledgements.AddRange(result.MissingWarningAcknowledgements.Select(x => (ElectionWarningCodeProto)(int)x));

        if (result.CeremonySnapshot is not null)
        {
            response.CeremonySnapshot = result.CeremonySnapshot.ToProto();
        }

        return response;
    }

    public static ElectionRecordView ToProto(this ElectionRecord election)
    {
        var view = new ElectionRecordView
        {
            ElectionId = election.ElectionId.ToString(),
            Title = election.Title,
            ShortDescription = election.ShortDescription ?? string.Empty,
            OwnerPublicAddress = election.OwnerPublicAddress,
            ExternalReferenceCode = election.ExternalReferenceCode ?? string.Empty,
            LifecycleState = (ElectionLifecycleStateProto)(int)election.LifecycleState,
            ElectionClass = (ElectionClassProto)(int)election.ElectionClass,
            BindingStatus = (ElectionBindingStatusProto)(int)election.BindingStatus,
            GovernanceMode = (ElectionGovernanceModeProto)(int)election.GovernanceMode,
            DisclosureMode = (ElectionDisclosureModeProto)(int)election.DisclosureMode,
            ParticipationPrivacyMode = (ParticipationPrivacyModeProto)(int)election.ParticipationPrivacyMode,
            VoteUpdatePolicy = (VoteUpdatePolicyProto)(int)election.VoteUpdatePolicy,
            EligibilitySourceType = (EligibilitySourceTypeProto)(int)election.EligibilitySourceType,
            EligibilityMutationPolicy = (EligibilityMutationPolicyProto)(int)election.EligibilityMutationPolicy,
            OutcomeRule = election.OutcomeRule.ToProto(),
            ProtocolOmegaVersion = election.ProtocolOmegaVersion,
            ReportingPolicy = (ReportingPolicyProto)(int)election.ReportingPolicy,
            ReviewWindowPolicy = (ReviewWindowPolicyProto)(int)election.ReviewWindowPolicy,
            OfficialResultVisibilityPolicy = (OfficialResultVisibilityPolicyProto)(int)election.OfficialResultVisibilityPolicy,
            CurrentDraftRevision = election.CurrentDraftRevision,
            CreatedAt = ToTimestamp(election.CreatedAt),
            LastUpdatedAt = ToTimestamp(election.LastUpdatedAt),
            ClosedProgressStatus = (ElectionClosedProgressStatusProto)(int)election.ClosedProgressStatus,
            OpenArtifactId = election.OpenArtifactId?.ToString() ?? string.Empty,
            CloseArtifactId = election.CloseArtifactId?.ToString() ?? string.Empty,
            FinalizeArtifactId = election.FinalizeArtifactId?.ToString() ?? string.Empty,
            TallyReadyArtifactId = election.TallyReadyArtifactId?.ToString() ?? string.Empty,
            UnofficialResultArtifactId = election.UnofficialResultArtifactId?.ToString() ?? string.Empty,
            OfficialResultArtifactId = election.OfficialResultArtifactId?.ToString() ?? string.Empty,
        };

        view.ApprovedClientApplications.AddRange(election.ApprovedClientApplications.Select(x => x.ToProto()));
        view.Options.AddRange(election.Options.Select(x => x.ToProto()));
        view.AcknowledgedWarningCodes.AddRange(election.AcknowledgedWarningCodes.Select(x => (ElectionWarningCodeProto)(int)x));

        if (election.RequiredApprovalCount.HasValue)
        {
            view.RequiredApprovalCount = election.RequiredApprovalCount.Value;
        }

        if (election.OpenedAt.HasValue)
        {
            view.OpenedAt = ToTimestamp(election.OpenedAt.Value);
        }

        if (election.ClosedAt.HasValue)
        {
            view.ClosedAt = ToTimestamp(election.ClosedAt.Value);
        }

        if (election.FinalizedAt.HasValue)
        {
            view.FinalizedAt = ToTimestamp(election.FinalizedAt.Value);
        }

        if (election.TallyReadyAt.HasValue)
        {
            view.TallyReadyAt = ToTimestamp(election.TallyReadyAt.Value);
        }

        if (election.VoteAcceptanceLockedAt.HasValue)
        {
            view.VoteAcceptanceLockedAt = ToTimestamp(election.VoteAcceptanceLockedAt.Value);
        }

        return view;
    }

    public static ElectionSummary ToSummaryProto(this ElectionRecord election) =>
        new()
        {
            ElectionId = election.ElectionId.ToString(),
            Title = election.Title,
            OwnerPublicAddress = election.OwnerPublicAddress,
            LifecycleState = (ElectionLifecycleStateProto)(int)election.LifecycleState,
            BindingStatus = (ElectionBindingStatusProto)(int)election.BindingStatus,
            GovernanceMode = (ElectionGovernanceModeProto)(int)election.GovernanceMode,
            CurrentDraftRevision = election.CurrentDraftRevision,
            LastUpdatedAt = ToTimestamp(election.LastUpdatedAt),
        };

    public static ElectionDraftSnapshot ToProto(this ElectionDraftSnapshotRecord snapshot)
    {
        var proto = new ElectionDraftSnapshot
        {
            Id = snapshot.Id.ToString(),
            ElectionId = snapshot.ElectionId.ToString(),
            DraftRevision = snapshot.DraftRevision,
            Metadata = snapshot.Metadata.ToProto(),
            Policy = snapshot.Policy.ToProto(),
            SnapshotReason = snapshot.SnapshotReason,
            RecordedAt = ToTimestamp(snapshot.RecordedAt),
            RecordedByPublicAddress = snapshot.RecordedByPublicAddress,
        };

        proto.Options.AddRange(snapshot.Options.Select(x => x.ToProto()));
        proto.AcknowledgedWarningCodes.AddRange(snapshot.AcknowledgedWarningCodes.Select(x => (ElectionWarningCodeProto)(int)x));
        return proto;
    }

    public static ElectionBoundaryArtifact ToProto(this ElectionBoundaryArtifactRecord artifact)
    {
        var proto = new ElectionBoundaryArtifact
        {
            Id = artifact.Id.ToString(),
            ElectionId = artifact.ElectionId.ToString(),
            ArtifactType = artifact.ArtifactType switch
            {
                ElectionBoundaryArtifactType.Open => ElectionBoundaryArtifactTypeProto.OpenArtifact,
                ElectionBoundaryArtifactType.Close => ElectionBoundaryArtifactTypeProto.CloseArtifact,
                ElectionBoundaryArtifactType.TallyReady => ElectionBoundaryArtifactTypeProto.TallyReadyArtifact,
                ElectionBoundaryArtifactType.Finalize => ElectionBoundaryArtifactTypeProto.FinalizeArtifact,
                _ => throw new ArgumentOutOfRangeException(nameof(artifact)),
            },
            LifecycleState = (ElectionLifecycleStateProto)(int)artifact.LifecycleState,
            SourceDraftRevision = artifact.SourceDraftRevision,
            Metadata = artifact.Metadata.ToProto(),
            Policy = artifact.Policy.ToProto(),
            FrozenEligibleVoterSetHash = ToByteString(artifact.FrozenEligibleVoterSetHash),
            TrusteePolicyExecutionReference = artifact.TrusteePolicyExecutionReference ?? string.Empty,
            ReportingPolicyExecutionReference = artifact.ReportingPolicyExecutionReference ?? string.Empty,
            ReviewWindowExecutionReference = artifact.ReviewWindowExecutionReference ?? string.Empty,
            AcceptedBallotSetHash = ToByteString(artifact.AcceptedBallotSetHash),
            FinalEncryptedTallyHash = ToByteString(artifact.FinalEncryptedTallyHash),
            PublishedBallotStreamHash = ToByteString(artifact.PublishedBallotStreamHash),
            RecordedAt = ToTimestamp(artifact.RecordedAt),
            RecordedByPublicAddress = artifact.RecordedByPublicAddress,
        };

        proto.Options.AddRange(artifact.Options.Select(x => x.ToProto()));
        proto.AcknowledgedWarningCodes.AddRange(artifact.AcknowledgedWarningCodes.Select(x => (ElectionWarningCodeProto)(int)x));

        if (artifact.TrusteeSnapshot is not null)
        {
            proto.TrusteeSnapshot = artifact.TrusteeSnapshot.ToProto();
        }

        if (artifact.CeremonySnapshot is not null)
        {
            proto.CeremonySnapshot = artifact.CeremonySnapshot.ToProto();
        }

        if (artifact.AcceptedBallotCount.HasValue)
        {
            proto.AcceptedBallotCount = artifact.AcceptedBallotCount.Value;
        }

        if (artifact.PublishedBallotCount.HasValue)
        {
            proto.PublishedBallotCount = artifact.PublishedBallotCount.Value;
        }

        return proto;
    }

    public static ElectionWarningAcknowledgement ToProto(this ElectionWarningAcknowledgementRecord acknowledgement) =>
        new()
        {
            Id = acknowledgement.Id.ToString(),
            ElectionId = acknowledgement.ElectionId.ToString(),
            WarningCode = (ElectionWarningCodeProto)(int)acknowledgement.WarningCode,
            DraftRevision = acknowledgement.DraftRevision,
            AcknowledgedByPublicAddress = acknowledgement.AcknowledgedByPublicAddress,
            AcknowledgedAt = ToTimestamp(acknowledgement.AcknowledgedAt),
        };

    public static ElectionTrusteeInvitation ToProto(this ElectionTrusteeInvitationRecord invitation)
    {
        var proto = new ElectionTrusteeInvitation
        {
            Id = invitation.Id.ToString(),
            ElectionId = invitation.ElectionId.ToString(),
            TrusteeUserAddress = invitation.TrusteeUserAddress,
            TrusteeDisplayName = invitation.TrusteeDisplayName ?? string.Empty,
            InvitedByPublicAddress = invitation.InvitedByPublicAddress,
            LinkedMessageId = invitation.LinkedMessageId?.ToString() ?? string.Empty,
            Status = (ElectionTrusteeInvitationStatusProto)(int)invitation.Status,
            SentAtDraftRevision = invitation.SentAtDraftRevision,
            SentAt = ToTimestamp(invitation.SentAt),
        };

        if (invitation.ResolvedAtDraftRevision.HasValue)
        {
            proto.ResolvedAtDraftRevision = invitation.ResolvedAtDraftRevision.Value;
        }

        if (invitation.RespondedAt.HasValue)
        {
            proto.RespondedAt = ToTimestamp(invitation.RespondedAt.Value);
        }

        if (invitation.RevokedAt.HasValue)
        {
            proto.RevokedAt = ToTimestamp(invitation.RevokedAt.Value);
        }

        return proto;
    }

    public static ElectionGovernedProposal ToProto(this ElectionGovernedProposalRecord proposal)
    {
        var proto = new ElectionGovernedProposal
        {
            Id = proposal.Id.ToString(),
            ElectionId = proposal.ElectionId.ToString(),
            ActionType = (ElectionGovernedActionTypeProto)(int)proposal.ActionType,
            LifecycleStateAtCreation = (ElectionLifecycleStateProto)(int)proposal.LifecycleStateAtCreation,
            ProposedByPublicAddress = proposal.ProposedByPublicAddress,
            CreatedAt = ToTimestamp(proposal.CreatedAt),
            ExecutionStatus = (ElectionGovernedProposalExecutionStatusProto)(int)proposal.ExecutionStatus,
            ExecutionFailureReason = proposal.ExecutionFailureReason ?? string.Empty,
            LastExecutionTriggeredByPublicAddress = proposal.LastExecutionTriggeredByPublicAddress ?? string.Empty,
        };

        if (proposal.LastExecutionAttemptedAt.HasValue)
        {
            proto.LastExecutionAttemptedAt = ToTimestamp(proposal.LastExecutionAttemptedAt.Value);
        }

        if (proposal.ExecutedAt.HasValue)
        {
            proto.ExecutedAt = ToTimestamp(proposal.ExecutedAt.Value);
        }

        return proto;
    }

    public static ElectionGovernedProposalApproval ToProto(this ElectionGovernedProposalApprovalRecord approval) =>
        new()
        {
            Id = approval.Id.ToString(),
            ProposalId = approval.ProposalId.ToString(),
            ElectionId = approval.ElectionId.ToString(),
            ActionType = (ElectionGovernedActionTypeProto)(int)approval.ActionType,
            LifecycleStateAtProposalCreation = (ElectionLifecycleStateProto)(int)approval.LifecycleStateAtProposalCreation,
            TrusteeUserAddress = approval.TrusteeUserAddress,
            TrusteeDisplayName = approval.TrusteeDisplayName ?? string.Empty,
            ApprovalNote = approval.ApprovalNote ?? string.Empty,
            ApprovedAt = ToTimestamp(approval.ApprovedAt),
        };

    public static ElectionMetadata ToProto(this ElectionMetadataSnapshot metadata) =>
        new()
        {
            Title = metadata.Title,
            ShortDescription = metadata.ShortDescription ?? string.Empty,
            OwnerPublicAddress = metadata.OwnerPublicAddress,
            ExternalReferenceCode = metadata.ExternalReferenceCode ?? string.Empty,
        };

    public static ElectionFrozenPolicy ToProto(this ElectionFrozenPolicySnapshot policy)
    {
        var proto = new ElectionFrozenPolicy
        {
            ElectionClass = (ElectionClassProto)(int)policy.ElectionClass,
            BindingStatus = (ElectionBindingStatusProto)(int)policy.BindingStatus,
            GovernanceMode = (ElectionGovernanceModeProto)(int)policy.GovernanceMode,
            DisclosureMode = (ElectionDisclosureModeProto)(int)policy.DisclosureMode,
            ParticipationPrivacyMode = (ParticipationPrivacyModeProto)(int)policy.ParticipationPrivacyMode,
            VoteUpdatePolicy = (VoteUpdatePolicyProto)(int)policy.VoteUpdatePolicy,
            EligibilitySourceType = (EligibilitySourceTypeProto)(int)policy.EligibilitySourceType,
            EligibilityMutationPolicy = (EligibilityMutationPolicyProto)(int)policy.EligibilityMutationPolicy,
            OutcomeRule = policy.OutcomeRule.ToProto(),
            ProtocolOmegaVersion = policy.ProtocolOmegaVersion,
            ReportingPolicy = (ReportingPolicyProto)(int)policy.ReportingPolicy,
            ReviewWindowPolicy = (ReviewWindowPolicyProto)(int)policy.ReviewWindowPolicy,
        };

        proto.ApprovedClientApplications.AddRange(policy.ApprovedClientApplications.Select(x => x.ToProto()));

        if (policy.RequiredApprovalCount.HasValue)
        {
            proto.RequiredApprovalCount = policy.RequiredApprovalCount.Value;
        }

        return proto;
    }

    public static HushNetwork.proto.ElectionTrusteeBoundarySnapshot ToProto(this HushShared.Elections.Model.ElectionTrusteeBoundarySnapshot snapshot)
    {
        var proto = new HushNetwork.proto.ElectionTrusteeBoundarySnapshot
        {
            RequiredApprovalCount = snapshot.RequiredApprovalCount,
            EveryAcceptedTrusteeMustApprove = snapshot.EveryAcceptedTrusteeMustApprove,
        };

        proto.AcceptedTrustees.AddRange(snapshot.AcceptedTrustees.Select(x => new HushNetwork.proto.ElectionTrusteeReference
        {
            TrusteeUserAddress = x.TrusteeUserAddress,
            TrusteeDisplayName = x.TrusteeDisplayName ?? string.Empty,
        }));

        return proto;
    }

    public static ElectionCeremonyProfile ToProto(this ElectionCeremonyProfileRecord profile) =>
        new()
        {
            ProfileId = profile.ProfileId,
            DisplayName = profile.DisplayName,
            Description = profile.Description,
            ProviderKey = profile.ProviderKey,
            ProfileVersion = profile.ProfileVersion,
            TrusteeCount = profile.TrusteeCount,
            RequiredApprovalCount = profile.RequiredApprovalCount,
            DevOnly = profile.DevOnly,
            RegisteredAt = ToTimestamp(profile.RegisteredAt),
            LastUpdatedAt = ToTimestamp(profile.LastUpdatedAt),
        };

    public static HushNetwork.proto.ElectionCeremonyBindingSnapshot ToProto(this HushShared.Elections.Model.ElectionCeremonyBindingSnapshot snapshot)
    {
        var proto = new HushNetwork.proto.ElectionCeremonyBindingSnapshot
        {
            CeremonyVersionId = snapshot.CeremonyVersionId.ToString(),
            VersionNumber = snapshot.CeremonyVersionNumber,
            ProfileId = snapshot.ProfileId,
            TrusteeCount = snapshot.BoundTrusteeCount,
            RequiredApprovalCount = snapshot.RequiredApprovalCount,
            TallyPublicKeyFingerprint = snapshot.TallyPublicKeyFingerprint,
        };

        proto.CompletedTrustees.AddRange(snapshot.ActiveTrustees.Select(x => new HushNetwork.proto.ElectionTrusteeReference
        {
            TrusteeUserAddress = x.TrusteeUserAddress,
            TrusteeDisplayName = x.TrusteeDisplayName ?? string.Empty,
        }));

        return proto;
    }

    public static ElectionCeremonyVersion ToProto(this ElectionCeremonyVersionRecord version)
    {
        var proto = new ElectionCeremonyVersion
        {
            Id = version.Id.ToString(),
            ElectionId = version.ElectionId.ToString(),
            VersionNumber = version.VersionNumber,
            ProfileId = version.ProfileId,
            Status = (ElectionCeremonyVersionStatusProto)(int)version.Status,
            TrusteeCount = version.TrusteeCount,
            RequiredApprovalCount = version.RequiredApprovalCount,
            StartedByPublicAddress = version.StartedByPublicAddress,
            StartedAt = ToTimestamp(version.StartedAt),
            SupersededReason = version.SupersededReason ?? string.Empty,
            TallyPublicKeyFingerprint = version.TallyPublicKeyFingerprint ?? string.Empty,
        };

        proto.BoundTrustees.AddRange(version.BoundTrustees.Select(x => new HushNetwork.proto.ElectionTrusteeReference
        {
            TrusteeUserAddress = x.TrusteeUserAddress,
            TrusteeDisplayName = x.TrusteeDisplayName ?? string.Empty,
        }));

        if (version.CompletedAt.HasValue)
        {
            proto.CompletedAt = ToTimestamp(version.CompletedAt.Value);
        }

        if (version.SupersededAt.HasValue)
        {
            proto.SupersededAt = ToTimestamp(version.SupersededAt.Value);
        }

        return proto;
    }

    public static ElectionCeremonyTranscriptEvent ToProto(this ElectionCeremonyTranscriptEventRecord transcriptEvent)
    {
        var proto = new ElectionCeremonyTranscriptEvent
        {
            Id = transcriptEvent.Id.ToString(),
            ElectionId = transcriptEvent.ElectionId.ToString(),
            CeremonyVersionId = transcriptEvent.CeremonyVersionId.ToString(),
            VersionNumber = transcriptEvent.VersionNumber,
            EventType = (ElectionCeremonyTranscriptEventTypeProto)(int)transcriptEvent.EventType,
            ActorPublicAddress = transcriptEvent.ActorPublicAddress ?? string.Empty,
            TrusteeUserAddress = transcriptEvent.TrusteeUserAddress ?? string.Empty,
            TrusteeDisplayName = transcriptEvent.TrusteeDisplayName ?? string.Empty,
            EventSummary = transcriptEvent.EventSummary,
            EvidenceReference = transcriptEvent.EvidenceReference ?? string.Empty,
            RestartReason = transcriptEvent.RestartReason ?? string.Empty,
            TallyPublicKeyFingerprint = transcriptEvent.TallyPublicKeyFingerprint ?? string.Empty,
            OccurredAt = ToTimestamp(transcriptEvent.OccurredAt),
            HasTrusteeState = transcriptEvent.TrusteeState.HasValue,
        };

        if (transcriptEvent.TrusteeState.HasValue)
        {
            proto.TrusteeState = (ElectionTrusteeCeremonyStateProto)(int)transcriptEvent.TrusteeState.Value;
        }

        return proto;
    }

    public static ElectionCeremonyMessageEnvelope ToProto(this ElectionCeremonyMessageEnvelopeRecord messageEnvelope) =>
        new()
        {
            Id = messageEnvelope.Id.ToString(),
            ElectionId = messageEnvelope.ElectionId.ToString(),
            CeremonyVersionId = messageEnvelope.CeremonyVersionId.ToString(),
            VersionNumber = messageEnvelope.VersionNumber,
            ProfileId = messageEnvelope.ProfileId,
            SenderTrusteeUserAddress = messageEnvelope.SenderTrusteeUserAddress,
            RecipientTrusteeUserAddress = messageEnvelope.RecipientTrusteeUserAddress ?? string.Empty,
            MessageType = messageEnvelope.MessageType,
            PayloadVersion = messageEnvelope.PayloadVersion,
            EncryptedPayload = ToByteString(messageEnvelope.EncryptedPayload),
            PayloadFingerprint = messageEnvelope.PayloadFingerprint,
            SubmittedAt = ToTimestamp(messageEnvelope.SubmittedAt),
        };

    public static ElectionCeremonyTrusteeState ToProto(this ElectionCeremonyTrusteeStateRecord trusteeState)
    {
        var proto = new ElectionCeremonyTrusteeState
        {
            Id = trusteeState.Id.ToString(),
            ElectionId = trusteeState.ElectionId.ToString(),
            CeremonyVersionId = trusteeState.CeremonyVersionId.ToString(),
            TrusteeUserAddress = trusteeState.TrusteeUserAddress,
            TrusteeDisplayName = trusteeState.TrusteeDisplayName ?? string.Empty,
            State = (ElectionTrusteeCeremonyStateProto)(int)trusteeState.State,
            TransportPublicKeyFingerprint = trusteeState.TransportPublicKeyFingerprint ?? string.Empty,
            ValidationFailureReason = trusteeState.ValidationFailureReason ?? string.Empty,
            ShareVersion = trusteeState.ShareVersion ?? string.Empty,
            LastUpdatedAt = ToTimestamp(trusteeState.LastUpdatedAt),
        };

        if (trusteeState.TransportPublicKeyPublishedAt.HasValue)
        {
            proto.TransportPublicKeyPublishedAt = ToTimestamp(trusteeState.TransportPublicKeyPublishedAt.Value);
        }

        if (trusteeState.JoinedAt.HasValue)
        {
            proto.JoinedAt = ToTimestamp(trusteeState.JoinedAt.Value);
        }

        if (trusteeState.SelfTestSucceededAt.HasValue)
        {
            proto.SelfTestSucceededAt = ToTimestamp(trusteeState.SelfTestSucceededAt.Value);
        }

        if (trusteeState.MaterialSubmittedAt.HasValue)
        {
            proto.MaterialSubmittedAt = ToTimestamp(trusteeState.MaterialSubmittedAt.Value);
        }

        if (trusteeState.ValidationFailedAt.HasValue)
        {
            proto.ValidationFailedAt = ToTimestamp(trusteeState.ValidationFailedAt.Value);
        }

        if (trusteeState.CompletedAt.HasValue)
        {
            proto.CompletedAt = ToTimestamp(trusteeState.CompletedAt.Value);
        }

        if (trusteeState.RemovedAt.HasValue)
        {
            proto.RemovedAt = ToTimestamp(trusteeState.RemovedAt.Value);
        }

        return proto;
    }

    public static ElectionCeremonyShareCustody ToProto(this ElectionCeremonyShareCustodyRecord shareCustody)
    {
        var proto = new ElectionCeremonyShareCustody
        {
            Id = shareCustody.Id.ToString(),
            ElectionId = shareCustody.ElectionId.ToString(),
            CeremonyVersionId = shareCustody.CeremonyVersionId.ToString(),
            TrusteeUserAddress = shareCustody.TrusteeUserAddress,
            ShareVersion = shareCustody.ShareVersion,
            PasswordProtected = shareCustody.PasswordProtected,
            Status = (ElectionCeremonyShareCustodyStatusProto)(int)shareCustody.Status,
            LastImportFailureReason = shareCustody.LastImportFailureReason ?? string.Empty,
            LastUpdatedAt = ToTimestamp(shareCustody.LastUpdatedAt),
        };

        if (shareCustody.LastExportedAt.HasValue)
        {
            proto.LastExportedAt = ToTimestamp(shareCustody.LastExportedAt.Value);
        }

        if (shareCustody.LastImportedAt.HasValue)
        {
            proto.LastImportedAt = ToTimestamp(shareCustody.LastImportedAt.Value);
        }

        if (shareCustody.LastImportFailedAt.HasValue)
        {
            proto.LastImportFailedAt = ToTimestamp(shareCustody.LastImportFailedAt.Value);
        }

        return proto;
    }

    public static ElectionFinalizationSession ToProto(this ElectionFinalizationSessionRecord session)
    {
        var proto = new ElectionFinalizationSession
        {
            Id = session.Id.ToString(),
            ElectionId = session.ElectionId.ToString(),
            GovernedProposalId = session.GovernedProposalId?.ToString() ?? string.Empty,
            GovernanceMode = (ElectionGovernanceModeProto)(int)session.GovernanceMode,
            SessionPurpose = (ElectionFinalizationSessionPurposeProto)(int)session.SessionPurpose,
            CloseArtifactId = session.CloseArtifactId.ToString(),
            AcceptedBallotSetHash = ToByteString(session.AcceptedBallotSetHash),
            FinalEncryptedTallyHash = ToByteString(session.FinalEncryptedTallyHash),
            TargetTallyId = session.TargetTallyId,
            RequiredShareCount = session.RequiredShareCount,
            Status = (ElectionFinalizationSessionStatusProto)(int)session.Status,
            CreatedAt = ToTimestamp(session.CreatedAt),
            CreatedByPublicAddress = session.CreatedByPublicAddress,
            ReleaseEvidenceId = session.ReleaseEvidenceId?.ToString() ?? string.Empty,
            LatestTransactionId = session.LatestTransactionId?.ToString() ?? string.Empty,
            LatestBlockId = session.LatestBlockId?.ToString() ?? string.Empty,
        };

        proto.EligibleTrustees.AddRange(session.EligibleTrustees.Select(x => x.ToProto()));

        if (session.CeremonySnapshot is not null)
        {
            proto.CeremonySnapshot = session.CeremonySnapshot.ToProto();
        }

        if (session.CompletedAt.HasValue)
        {
            proto.CompletedAt = ToTimestamp(session.CompletedAt.Value);
        }

        if (session.LatestBlockHeight.HasValue)
        {
            proto.LatestBlockHeight = session.LatestBlockHeight.Value;
        }

        return proto;
    }

    public static ElectionFinalizationShare ToProto(this ElectionFinalizationShareRecord share)
    {
        var proto = new ElectionFinalizationShare
        {
            Id = share.Id.ToString(),
            FinalizationSessionId = share.FinalizationSessionId.ToString(),
            ElectionId = share.ElectionId.ToString(),
            TrusteeUserAddress = share.TrusteeUserAddress,
            TrusteeDisplayName = share.TrusteeDisplayName ?? string.Empty,
            SubmittedByPublicAddress = share.SubmittedByPublicAddress,
            ShareIndex = share.ShareIndex,
            ShareVersion = share.ShareVersion,
            TargetType = (ElectionFinalizationTargetTypeProto)(int)share.TargetType,
            ClaimedCloseArtifactId = share.ClaimedCloseArtifactId.ToString(),
            ClaimedAcceptedBallotSetHash = ToByteString(share.ClaimedAcceptedBallotSetHash),
            ClaimedFinalEncryptedTallyHash = ToByteString(share.ClaimedFinalEncryptedTallyHash),
            ClaimedTargetTallyId = share.ClaimedTargetTallyId,
            ClaimedCeremonyVersionId = share.ClaimedCeremonyVersionId?.ToString() ?? string.Empty,
            ClaimedTallyPublicKeyFingerprint = share.ClaimedTallyPublicKeyFingerprint ?? string.Empty,
            Status = (ElectionFinalizationShareStatusProto)(int)share.Status,
            FailureCode = share.FailureCode ?? string.Empty,
            FailureReason = share.FailureReason ?? string.Empty,
            SubmittedAt = ToTimestamp(share.SubmittedAt),
            SourceTransactionId = share.SourceTransactionId?.ToString() ?? string.Empty,
            SourceBlockId = share.SourceBlockId?.ToString() ?? string.Empty,
        };

        if (share.SourceBlockHeight.HasValue)
        {
            proto.SourceBlockHeight = share.SourceBlockHeight.Value;
        }

        return proto;
    }

    public static ElectionFinalizationReleaseEvidence ToProto(this ElectionFinalizationReleaseEvidenceRecord releaseEvidence)
    {
        var proto = new ElectionFinalizationReleaseEvidence
        {
            Id = releaseEvidence.Id.ToString(),
            FinalizationSessionId = releaseEvidence.FinalizationSessionId.ToString(),
            ElectionId = releaseEvidence.ElectionId.ToString(),
            SessionPurpose = (ElectionFinalizationSessionPurposeProto)(int)releaseEvidence.SessionPurpose,
            ReleaseMode = (ElectionFinalizationReleaseModeProto)(int)releaseEvidence.ReleaseMode,
            CloseArtifactId = releaseEvidence.CloseArtifactId.ToString(),
            AcceptedBallotSetHash = ToByteString(releaseEvidence.AcceptedBallotSetHash),
            FinalEncryptedTallyHash = ToByteString(releaseEvidence.FinalEncryptedTallyHash),
            TargetTallyId = releaseEvidence.TargetTallyId,
            AcceptedShareCount = releaseEvidence.AcceptedShareCount,
            CompletedAt = ToTimestamp(releaseEvidence.CompletedAt),
            CompletedByPublicAddress = releaseEvidence.CompletedByPublicAddress,
            SourceTransactionId = releaseEvidence.SourceTransactionId?.ToString() ?? string.Empty,
            SourceBlockId = releaseEvidence.SourceBlockId?.ToString() ?? string.Empty,
        };

        proto.AcceptedTrustees.AddRange(releaseEvidence.AcceptedTrustees.Select(x => x.ToProto()));

        if (releaseEvidence.SourceBlockHeight.HasValue)
        {
            proto.SourceBlockHeight = releaseEvidence.SourceBlockHeight.Value;
        }

        return proto;
    }

    public static ElectionResultArtifact ToProto(this ElectionResultArtifactRecord artifact)
    {
        var proto = new ElectionResultArtifact
        {
            Id = artifact.Id.ToString(),
            ElectionId = artifact.ElectionId.ToString(),
            ArtifactKind = (ElectionResultArtifactKindProto)(int)artifact.ArtifactKind,
            Visibility = (ElectionResultArtifactVisibilityProto)(int)artifact.Visibility,
            Title = artifact.Title,
            BlankCount = artifact.BlankCount,
            TotalVotedCount = artifact.TotalVotedCount,
            EligibleToVoteCount = artifact.EligibleToVoteCount,
            DidNotVoteCount = artifact.DidNotVoteCount,
            TallyReadyArtifactId = artifact.TallyReadyArtifactId?.ToString() ?? string.Empty,
            SourceResultArtifactId = artifact.SourceResultArtifactId?.ToString() ?? string.Empty,
            EncryptedPayload = artifact.EncryptedPayload ?? string.Empty,
            PublicPayload = artifact.PublicPayload ?? string.Empty,
            RecordedAt = ToTimestamp(artifact.RecordedAt),
            RecordedByPublicAddress = artifact.RecordedByPublicAddress,
        };

        proto.NamedOptionResults.AddRange(artifact.NamedOptionResults.Select(x => x.ToProto()));
        proto.DenominatorEvidence = artifact.DenominatorEvidence.ToProto();
        return proto;
    }

    public static ElectionReportPackageSummaryView ToProto(this ElectionReportPackageRecord reportPackage) =>
        new()
        {
            Id = reportPackage.Id.ToString(),
            Status = (ElectionReportPackageStatusProto)(int)reportPackage.Status,
            AttemptNumber = reportPackage.AttemptNumber,
            PreviousAttemptId = reportPackage.PreviousAttemptId?.ToString() ?? string.Empty,
            FinalizationSessionId = reportPackage.FinalizationSessionId?.ToString() ?? string.Empty,
            TallyReadyArtifactId = reportPackage.TallyReadyArtifactId.ToString(),
            UnofficialResultArtifactId = reportPackage.UnofficialResultArtifactId.ToString(),
            OfficialResultArtifactId = reportPackage.OfficialResultArtifactId?.ToString() ?? string.Empty,
            FinalizeArtifactId = reportPackage.FinalizeArtifactId?.ToString() ?? string.Empty,
            CloseBoundaryArtifactId = reportPackage.CloseBoundaryArtifactId?.ToString() ?? string.Empty,
            CloseEligibilitySnapshotId = reportPackage.CloseEligibilitySnapshotId?.ToString() ?? string.Empty,
            FinalizationReleaseEvidenceId = reportPackage.FinalizationReleaseEvidenceId?.ToString() ?? string.Empty,
            FrozenEvidenceHash = ToByteString(reportPackage.FrozenEvidenceHash),
            FrozenEvidenceFingerprint = reportPackage.FrozenEvidenceFingerprint,
            PackageHash = ToByteString(reportPackage.PackageHash),
            ArtifactCount = reportPackage.ArtifactCount,
            FailureCode = reportPackage.FailureCode ?? string.Empty,
            FailureReason = reportPackage.FailureReason ?? string.Empty,
            AttemptedAt = ToTimestamp(reportPackage.AttemptedAt),
            SealedAt = reportPackage.SealedAt.HasValue
                ? ToTimestamp(reportPackage.SealedAt.Value)
                : new Timestamp(),
            HasSealedAt = reportPackage.SealedAt.HasValue,
            AttemptedByPublicAddress = reportPackage.AttemptedByPublicAddress,
        };

    public static ElectionReportArtifactView ToProto(this ElectionReportArtifactRecord reportArtifact) =>
        new()
        {
            Id = reportArtifact.Id.ToString(),
            ReportPackageId = reportArtifact.ReportPackageId.ToString(),
            ElectionId = reportArtifact.ElectionId.ToString(),
            ArtifactKind = (ElectionReportArtifactKindProto)(int)reportArtifact.ArtifactKind,
            Format = (ElectionReportArtifactFormatProto)(int)reportArtifact.Format,
            AccessScope = (ElectionReportArtifactAccessScopeProto)(int)reportArtifact.AccessScope,
            SortOrder = reportArtifact.SortOrder,
            Title = reportArtifact.Title,
            FileName = reportArtifact.FileName,
            MediaType = reportArtifact.MediaType,
            ContentHash = ToByteString(reportArtifact.ContentHash),
            Content = reportArtifact.Content,
            PairedArtifactId = reportArtifact.PairedArtifactId?.ToString() ?? string.Empty,
            RecordedAt = ToTimestamp(reportArtifact.RecordedAt),
        };

    public static OutcomeRule ToProto(this OutcomeRuleDefinition outcomeRule) =>
        new()
        {
            Kind = (OutcomeRuleKindProto)(int)outcomeRule.Kind,
            TemplateKey = outcomeRule.TemplateKey,
            SeatCount = outcomeRule.SeatCount,
            BlankVoteCountsForTurnout = outcomeRule.BlankVoteCountsForTurnout,
            BlankVoteExcludedFromWinnerSelection = outcomeRule.BlankVoteExcludedFromWinnerSelection,
            BlankVoteExcludedFromThresholdDenominator = outcomeRule.BlankVoteExcludedFromThresholdDenominator,
            TieResolutionRule = outcomeRule.TieResolutionRule,
            CalculationBasis = outcomeRule.CalculationBasis,
        };

    public static ApprovedClientApplication ToProto(this ApprovedClientApplicationRecord application) =>
        new()
        {
            ApplicationId = application.ApplicationId,
            Version = application.Version,
        };

    public static ElectionOption ToProto(this ElectionOptionDefinition option) =>
        new()
        {
            OptionId = option.OptionId,
            DisplayLabel = option.DisplayLabel,
            ShortDescription = option.ShortDescription ?? string.Empty,
            BallotOrder = option.BallotOrder,
            IsBlankOption = option.IsBlankOption,
        };

    public static HushNetwork.proto.ElectionResultOptionCount ToProto(this HushShared.Elections.Model.ElectionResultOptionCount optionCount) =>
        new()
        {
            OptionId = optionCount.OptionId,
            DisplayLabel = optionCount.DisplayLabel,
            ShortDescription = optionCount.ShortDescription ?? string.Empty,
            BallotOrder = optionCount.BallotOrder,
            Rank = optionCount.Rank,
            VoteCount = optionCount.VoteCount,
        };

    public static HushNetwork.proto.ElectionResultDenominatorEvidence ToProto(this HushShared.Elections.Model.ElectionResultDenominatorEvidence evidence) =>
        new()
        {
            SnapshotType = (ElectionEligibilitySnapshotTypeProto)(int)evidence.SnapshotType,
            EligibilitySnapshotId = evidence.EligibilitySnapshotId.ToString(),
            BoundaryArtifactId = evidence.BoundaryArtifactId?.ToString() ?? string.Empty,
            ActiveDenominatorSetHash = ToByteString(evidence.ActiveDenominatorSetHash),
        };

    public static HushNetwork.proto.ElectionTrusteeReference ToProto(
        this HushShared.Elections.Model.ElectionTrusteeReference reference) =>
        new()
        {
            TrusteeUserAddress = reference.TrusteeUserAddress,
            TrusteeDisplayName = reference.TrusteeDisplayName ?? string.Empty,
        };

    public static ElectionId ParseElectionId(string electionId)
    {
        if (!Guid.TryParse(electionId, out var value))
        {
            throw new FormatException($"ElectionId '{electionId}' is not a valid GUID.");
        }

        return new ElectionId(value);
    }

    public static Guid ParseGuid(string value, string fieldName)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new FormatException($"{fieldName} '{value}' is not a valid GUID.");
        }

        return guid;
    }

    public static byte[]? ToNullableBytes(this ByteString value) =>
        value is null || value.Length == 0 ? null : value.ToByteArray();

    private static string? NormalizeOptionalString(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static Timestamp ToTimestamp(DateTime value) =>
        Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ByteString ToByteString(byte[]? value) =>
        value is null ? ByteString.Empty : ByteString.CopyFrom(value);
}

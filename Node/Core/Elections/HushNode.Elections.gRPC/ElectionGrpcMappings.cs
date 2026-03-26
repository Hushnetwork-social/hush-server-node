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
            CurrentDraftRevision = election.CurrentDraftRevision,
            CreatedAt = ToTimestamp(election.CreatedAt),
            LastUpdatedAt = ToTimestamp(election.LastUpdatedAt),
            OpenArtifactId = election.OpenArtifactId?.ToString() ?? string.Empty,
            CloseArtifactId = election.CloseArtifactId?.ToString() ?? string.Empty,
            FinalizeArtifactId = election.FinalizeArtifactId?.ToString() ?? string.Empty,
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
            RecordedAt = ToTimestamp(artifact.RecordedAt),
            RecordedByPublicAddress = artifact.RecordedByPublicAddress,
        };

        proto.Options.AddRange(artifact.Options.Select(x => x.ToProto()));
        proto.AcknowledgedWarningCodes.AddRange(artifact.AcknowledgedWarningCodes.Select(x => (ElectionWarningCodeProto)(int)x));

        if (artifact.TrusteeSnapshot is not null)
        {
            proto.TrusteeSnapshot = artifact.TrusteeSnapshot.ToProto();
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

using HushNetwork.proto;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

internal static partial class ElectionGrpcMappings
{
    public static ElectionAnomalyOwnThreadView ToProto(this ElectionAnomalyOwnThreadProjection projection)
    {
        var view = new ElectionAnomalyOwnThreadView
        {
            AnomalyThreadId = projection.AnomalyThreadId.ToString(),
            ElectionId = projection.ElectionId.ToString(),
            CategoryId = projection.CategoryId,
            CaseStateId = projection.CaseStateId,
            CurrentThreadHash = projection.CurrentThreadHash,
            SeverityCandidateId = projection.SeverityCandidateId ?? string.Empty,
            GovernedDecisionRef = projection.GovernedDecisionRef ?? string.Empty,
            HasOpenClarificationRequest = projection.HasOpenClarificationRequest,
            CreatedAt = ToTimestamp(projection.CreatedAtUtc),
            UpdatedAt = ToTimestamp(projection.UpdatedAtUtc),
        };

        view.Messages.AddRange(projection.Messages.Select(ToProto));
        return view;
    }

    public static ElectionAnomalyTrusteeCountsView ToProto(this ElectionAnomalyTrusteeCountsProjection projection)
    {
        var view = new ElectionAnomalyTrusteeCountsView
        {
            ElectionId = projection.ElectionId.ToString(),
            TotalThreadCount = projection.TotalThreadCount,
            ContinuitySummary = projection.ContinuitySummary.ToProto(),
        };

        view.CategoryCounts.AddRange(projection.CategoryCounts.Select(ToProto));
        view.CaseStateCounts.AddRange(projection.CaseStateCounts.Select(ToProto));
        return view;
    }

    public static ElectionAnomalyOwnerTriageView ToProto(this ElectionAnomalyOwnerTriageProjection projection)
    {
        var view = new ElectionAnomalyOwnerTriageView
        {
            ElectionId = projection.ElectionId.ToString(),
            TotalThreadCount = projection.TotalThreadCount,
            OpenThreadCount = projection.OpenThreadCount,
            AwaitingInformationThreadCount = projection.AwaitingInformationThreadCount,
            ResponsePresentThreadCount = projection.ResponsePresentThreadCount,
            ExternalClaimantThreadCount = projection.ExternalClaimantThreadCount,
            DecryptableMessageCount = projection.DecryptableMessageCount,
            PendingRewrapMessageCount = projection.PendingRewrapMessageCount,
            MissingOwnerWrapMessageCount = projection.MissingOwnerWrapMessageCount,
            AttachmentManifestCount = projection.AttachmentManifestCount,
            GovernedContinuityHandoffStatusId = projection.GovernedContinuityHandoffStatusId,
            ContinuitySummary = projection.ContinuitySummary.ToProto(),
        };

        view.CategoryCounts.AddRange(projection.CategoryCounts.Select(ToProto));
        view.CaseStateCounts.AddRange(projection.CaseStateCounts.Select(ToProto));
        view.Threads.AddRange(projection.Threads.Select(ToProto));
        return view;
    }

    public static ElectionAnomalyAuditorRestrictedReviewView ToProto(
        this ElectionAnomalyAuditorRestrictedReviewProjection projection)
    {
        var view = new ElectionAnomalyAuditorRestrictedReviewView
        {
            ElectionId = projection.ElectionId.ToString(),
            TotalThreadCount = projection.Threads.Count,
            DecryptableMessageCount = projection.Threads
                .SelectMany(thread => thread.Messages)
                .Count(message => !string.IsNullOrWhiteSpace(message.CallerAuditorWrap?.EncryptedContentKey)),
            PendingRewrapMessageCount = projection.Threads
                .SelectMany(thread => thread.Messages)
                .Count(message => string.Equals(
                    message.CallerAuditorWrap?.WrapStatusId,
                    ElectionAnomalyRecipientWrapStatusIds.PendingBackfill,
                    StringComparison.Ordinal)),
            MissingWrapMessageCount = projection.Threads
                .SelectMany(thread => thread.Messages)
                .Count(message => message.CallerAuditorWrap is null),
            AttachmentManifestCount = projection.Threads
                .SelectMany(thread => thread.Messages)
                .Count(message => !string.IsNullOrWhiteSpace(message.AttachmentManifestHash)),
        };

        view.Threads.AddRange(projection.Threads.Select(ToProto));
        return view;
    }

    private static ElectionAnomalyMessageView ToProto(ElectionAnomalyEncryptedMessageProjection projection)
    {
        var view = new ElectionAnomalyMessageView
        {
            MessageId = projection.MessageId.ToString(),
            MessageKindId = projection.MessageKindId,
            RecordedAt = ToTimestamp(projection.RecordedAtUtc),
            EncryptedBody = projection.EncryptedBody,
            EncryptedBodyHash = projection.EncryptedBodyHash,
            PlaintextCharacterCount = projection.PlaintextCharacterCount,
            ClarificationRequestId = projection.ClarificationRequestId?.ToString() ?? string.Empty,
            HasClarificationRequest = projection.ClarificationRequestId.HasValue,
            AttachmentManifestHash = projection.AttachmentManifestHash ?? string.Empty,
        };

        view.RecipientWraps.AddRange(projection.RecipientWraps.Select(ToProto));
        return view;
    }

    private static ElectionAnomalyRecipientWrapView ToProto(ElectionAnomalyRecipientWrapProjection projection) =>
        new()
        {
            RecipientRoleId = projection.RecipientRoleId,
            WrapStatusId = projection.WrapStatusId,
            RecipientPublicAddress = projection.RecipientPublicAddress ?? string.Empty,
            RecipientKeyFingerprint = projection.RecipientKeyFingerprint ?? string.Empty,
            EncryptedContentKey = projection.EncryptedContentKey ?? string.Empty,
            WrapAlgorithm = projection.WrapAlgorithm ?? string.Empty,
        };

    private static ElectionAnomalyAuditorRestrictedThreadView ToProto(
        ElectionAnomalyAuditorRestrictedThreadProjection projection)
    {
        var view = new ElectionAnomalyAuditorRestrictedThreadView
        {
            AnomalyThreadId = projection.AnomalyThreadId.ToString(),
            ElectionId = projection.ElectionId.ToString(),
            CategoryId = projection.CategoryId,
            CaseStateId = projection.CaseStateId,
            CurrentThreadHash = projection.CurrentThreadHash,
            SeverityCandidateId = projection.SeverityCandidateId ?? string.Empty,
            GovernedDecisionRef = projection.GovernedDecisionRef ?? string.Empty,
            HasOpenClarificationRequest = projection.HasOpenClarificationRequest,
            CreatedAt = ToTimestamp(projection.CreatedAtUtc),
            UpdatedAt = ToTimestamp(projection.UpdatedAtUtc),
        };

        view.Messages.AddRange(projection.Messages.Select(ToProto));
        return view;
    }

    private static ElectionAnomalyOwnerTriageThreadView ToProto(
        ElectionAnomalyOwnerTriageThreadProjection projection)
    {
        var view = new ElectionAnomalyOwnerTriageThreadView
        {
            AnomalyThreadId = projection.AnomalyThreadId.ToString(),
            ElectionId = projection.ElectionId.ToString(),
            CategoryId = projection.CategoryId,
            CaseStateId = projection.CaseStateId,
            CurrentThreadHash = projection.CurrentThreadHash,
            SeverityCandidateId = projection.SeverityCandidateId ?? string.Empty,
            GovernedDecisionRef = projection.GovernedDecisionRef ?? string.Empty,
            SubmitterActorPublicAddress = projection.SubmitterActorPublicAddress ?? string.Empty,
            SubmitterRoleContextId = projection.SubmitterRoleContextId ?? string.Empty,
            LifecycleStateAtSubmission = (ElectionLifecycleStateProto)(int)projection.LifecycleStateAtSubmission,
            HasOpenClarificationRequest = projection.HasOpenClarificationRequest,
            OpenClarificationRequestId = projection.OpenClarificationRequestId?.ToString() ?? string.Empty,
            HasOpenClarificationRequestId = projection.OpenClarificationRequestId.HasValue,
            CreatedAt = ToTimestamp(projection.CreatedAtUtc),
            UpdatedAt = ToTimestamp(projection.UpdatedAtUtc),
        };

        view.Messages.AddRange(projection.Messages.Select(ToProto));
        return view;
    }

    private static ElectionAnomalyRestrictedMessageView ToProto(ElectionAnomalyRestrictedMessageProjection projection)
    {
        var view = new ElectionAnomalyRestrictedMessageView
        {
            MessageId = projection.MessageId.ToString(),
            MessageKindId = projection.MessageKindId,
            RecordedAt = ToTimestamp(projection.RecordedAtUtc),
            EncryptedBody = projection.EncryptedBody,
            EncryptedBodyHash = projection.EncryptedBodyHash,
            PlaintextCharacterCount = projection.PlaintextCharacterCount,
            HasCallerAuditorWrap = projection.CallerAuditorWrap is not null,
            ClarificationRequestId = projection.ClarificationRequestId?.ToString() ?? string.Empty,
            HasClarificationRequest = projection.ClarificationRequestId.HasValue,
            AttachmentManifestHash = projection.AttachmentManifestHash ?? string.Empty,
        };

        view.RecipientStatuses.AddRange(projection.RecipientWraps.Select(ToProto));
        if (projection.CallerAuditorWrap is not null)
        {
            view.CallerAuditorWrap = ToProto(projection.CallerAuditorWrap);
        }

        return view;
    }

    private static ElectionAnomalyOwnerMessageView ToProto(ElectionAnomalyOwnerMessageProjection projection)
    {
        var view = new ElectionAnomalyOwnerMessageView
        {
            MessageId = projection.MessageId.ToString(),
            MessageKindId = projection.MessageKindId,
            RecordedAt = ToTimestamp(projection.RecordedAtUtc),
            EncryptedBody = projection.EncryptedBody,
            EncryptedBodyHash = projection.EncryptedBodyHash,
            PlaintextCharacterCount = projection.PlaintextCharacterCount,
            HasCallerOwnerWrap = projection.CallerOwnerWrap is not null,
            ClarificationRequestId = projection.ClarificationRequestId?.ToString() ?? string.Empty,
            HasClarificationRequest = projection.ClarificationRequestId.HasValue,
            AttachmentManifestHash = projection.AttachmentManifestHash ?? string.Empty,
        };

        view.RecipientStatuses.AddRange(projection.RecipientWraps.Select(ToProto));
        if (projection.CallerOwnerWrap is not null)
        {
            view.CallerOwnerWrap = ToProto(projection.CallerOwnerWrap);
        }

        return view;
    }

    private static ElectionAnomalyRestrictedRecipientStatusView ToProto(
        ElectionAnomalyRestrictedRecipientWrapProjection projection) =>
        new()
        {
            RecipientRoleId = projection.RecipientRoleId,
            WrapStatusId = projection.WrapStatusId,
        };

    private static ElectionAnomalyAuditorCallerWrapView ToProto(
        ElectionAnomalyAuditorCallerWrapProjection projection) =>
        new()
        {
            WrapStatusId = projection.WrapStatusId,
            RecipientKeyFingerprint = projection.RecipientKeyFingerprint ?? string.Empty,
            EncryptedContentKey = projection.EncryptedContentKey ?? string.Empty,
            WrapAlgorithm = projection.WrapAlgorithm ?? string.Empty,
        };

    private static ElectionAnomalyOwnerCallerWrapView ToProto(
        ElectionAnomalyOwnerCallerWrapProjection projection) =>
        new()
        {
            WrapStatusId = projection.WrapStatusId,
            RecipientKeyFingerprint = projection.RecipientKeyFingerprint ?? string.Empty,
            EncryptedContentKey = projection.EncryptedContentKey ?? string.Empty,
            WrapAlgorithm = projection.WrapAlgorithm ?? string.Empty,
        };

    private static ElectionAnomalyCategoryCountView ToProto(ElectionAnomalyCategoryCountProjection projection) =>
        new()
        {
            CategoryId = projection.CategoryId,
            Count = projection.Count,
        };

    private static ElectionAnomalyCaseStateCountView ToProto(ElectionAnomalyCaseStateCountProjection projection) =>
        new()
        {
            CaseStateId = projection.CaseStateId,
            Count = projection.Count,
        };

    private static ElectionAnomalyTrusteeContinuitySummaryView ToProto(
        this ElectionAnomalyTrusteeContinuitySummaryProjection projection) =>
        new()
        {
            TrusteeContinuityThreadCount = projection.TrusteeContinuityThreadCount,
            OpenContinuityThreadCount = projection.OpenContinuityThreadCount,
            AwaitingInformationContinuityThreadCount = projection.AwaitingInformationContinuityThreadCount,
            ClosedContinuityThreadCount = projection.ClosedContinuityThreadCount,
            GovernedDecisionLinkedCount = projection.GovernedDecisionLinkedCount,
            HasContinuityIssue = projection.HasContinuityIssue,
        };
}

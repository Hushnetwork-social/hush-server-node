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
}

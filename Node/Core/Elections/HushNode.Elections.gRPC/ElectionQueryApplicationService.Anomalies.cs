using System.Text.Json;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

public partial class ElectionQueryApplicationService
{
    public async Task<ElectionAnomalyOwnThreadProjection?> GetElectionAnomalyOwnThreadAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var ownThread = threads.FirstOrDefault(thread =>
            ElectionAnomalyAuthorization.CanActorReadOwnThread(thread, actorPublicAddress).CanRead);
        if (ownThread is null)
        {
            return null;
        }

        var messages = await BuildEncryptedMessageProjectionsAsync(repository, ownThread);
        return new ElectionAnomalyOwnThreadProjection(
            ownThread.Id,
            ownThread.ElectionId,
            ownThread.CurrentCategoryId,
            ownThread.CurrentCaseStateId,
            ownThread.CurrentThreadHash,
            ownThread.SeverityCandidateId,
            ownThread.GovernedDecisionRef,
            ownThread.HasOpenClarificationRequest,
            ownThread.CreatedAt,
            ownThread.LastUpdatedAt,
            messages);
    }

    public async Task<IReadOnlyList<ElectionAnomalyOwnerTriageProjection>> GetElectionAnomalyOwnerTriageAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(electionId);
        if (election is null ||
            !string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return Array.Empty<ElectionAnomalyOwnerTriageProjection>();
        }

        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var projections = new List<ElectionAnomalyOwnerTriageProjection>();
        foreach (var thread in threads.OrderByDescending(x => x.LastUpdatedAt).ThenBy(x => x.Id))
        {
            var messages = await BuildEncryptedMessageProjectionsAsync(repository, thread);
            projections.Add(new ElectionAnomalyOwnerTriageProjection(
                thread.Id,
                thread.ElectionId,
                thread.CurrentCategoryId,
                thread.CurrentCaseStateId,
                thread.CurrentThreadHash,
                thread.SeverityCandidateId,
                thread.GovernedDecisionRef,
                thread.SubmitterActorPublicAddress,
                thread.SubmitterRoleContextId,
                thread.HasOpenClarificationRequest,
                thread.CreatedAt,
                thread.LastUpdatedAt,
                messages));
        }

        return projections;
    }

    public async Task<ElectionAnomalyTrusteeCountsProjection?> GetElectionAnomalyTrusteeCountsAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var acceptedTrustee = (await repository.GetAcceptedTrusteeInvitationsByActorAsync(actorPublicAddress))
            .Any(x => x.ElectionId == electionId && x.Status == ElectionTrusteeInvitationStatus.Accepted);
        if (!acceptedTrustee)
        {
            return null;
        }

        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        return new ElectionAnomalyTrusteeCountsProjection(
            electionId,
            threads.Count,
            threads
                .GroupBy(x => x.CurrentCategoryId, StringComparer.Ordinal)
                .OrderBy(x => x.Key)
                .Select(x => new ElectionAnomalyCategoryCountProjection(x.Key, x.Count()))
                .ToArray(),
            threads
                .GroupBy(x => x.CurrentCaseStateId, StringComparer.Ordinal)
                .OrderBy(x => x.Key)
                .Select(x => new ElectionAnomalyCaseStateCountProjection(x.Key, x.Count()))
                .ToArray());
    }

    public async Task<ElectionAnomalyAuditorRestrictedReviewProjection?> GetElectionAnomalyAuditorRestrictedReviewAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var reportAccessGrant = await repository.GetReportAccessGrantAsync(electionId, actorPublicAddress);
        if (reportAccessGrant?.GrantRole != ElectionReportAccessGrantRole.DesignatedAuditor)
        {
            return null;
        }

        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var projections = new List<ElectionAnomalyAuditorRestrictedThreadProjection>();
        foreach (var thread in threads.OrderByDescending(x => x.LastUpdatedAt).ThenBy(x => x.Id))
        {
            var messages = await BuildRestrictedMessageProjectionsAsync(repository, thread);
            projections.Add(new ElectionAnomalyAuditorRestrictedThreadProjection(
                thread.Id,
                thread.ElectionId,
                thread.CurrentCategoryId,
                thread.CurrentCaseStateId,
                thread.CurrentThreadHash,
                thread.SeverityCandidateId,
                thread.GovernedDecisionRef,
                thread.HasOpenClarificationRequest,
                thread.CreatedAt,
                thread.LastUpdatedAt,
                messages));
        }

        return new ElectionAnomalyAuditorRestrictedReviewProjection(electionId, projections);
    }

    public async Task<ElectionAnomalyReportManifestSeedProjection?> GetElectionAnomalyReportManifestSeedAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(electionId);
        var reportAccessGrant = await repository.GetReportAccessGrantAsync(electionId, actorPublicAddress);
        var isOwner = election is not null &&
            string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal);
        var isAuditor = reportAccessGrant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor;
        if (!isOwner && !isAuditor)
        {
            return null;
        }

        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var threadProjections = new List<ElectionAnomalyReportManifestThreadProjection>();
        foreach (var thread in threads.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            var wraps = await repository.GetAnomalyRecipientWrapsAsync(thread.Id);
            threadProjections.Add(new ElectionAnomalyReportManifestThreadProjection(
                thread.Id,
                thread.ElectionId,
                thread.CurrentCategoryId,
                thread.CurrentCaseStateId,
                thread.CurrentThreadHash,
                thread.CreatedAt,
                thread.LastUpdatedAt,
                wraps
                    .GroupBy(x => new { x.RecipientRoleId, x.WrapStatusId })
                    .OrderBy(x => x.Key.RecipientRoleId)
                    .ThenBy(x => x.Key.WrapStatusId)
                    .Select(x => new ElectionAnomalyRestrictedRecipientWrapProjection(
                        x.Key.RecipientRoleId,
                        x.Key.WrapStatusId))
                    .ToArray()));
        }

        return new ElectionAnomalyReportManifestSeedProjection(
            electionId,
            threads.Count,
            threads
                .GroupBy(x => x.CurrentCategoryId, StringComparer.Ordinal)
                .OrderBy(x => x.Key)
                .Select(x => new ElectionAnomalyCategoryCountProjection(x.Key, x.Count()))
                .ToArray(),
            threads
                .GroupBy(x => x.CurrentCaseStateId, StringComparer.Ordinal)
                .OrderBy(x => x.Key)
                .Select(x => new ElectionAnomalyCaseStateCountProjection(x.Key, x.Count()))
                .ToArray(),
            threadProjections);
    }

    private static async Task<IReadOnlyList<ElectionAnomalyEncryptedMessageProjection>> BuildEncryptedMessageProjectionsAsync(
        IElectionsRepository repository,
        ElectionAnomalyThreadRecord thread)
    {
        var messages = await repository.GetAnomalyMessageEnvelopesAsync(thread.Id);
        var wraps = await repository.GetAnomalyRecipientWrapsAsync(thread.Id);
        var clarificationRequestIds = await ResolveClarificationRequestIdsByEventIdAsync(repository, thread.Id);
        var wrapsByMessageId = wraps
            .GroupBy(x => x.MessageEnvelopeId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        return messages
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(message =>
            {
                wrapsByMessageId.TryGetValue(message.Id, out var messageWraps);
                clarificationRequestIds.TryGetValue(message.EventId, out var clarificationRequestId);
                return new ElectionAnomalyEncryptedMessageProjection(
                    message.Id,
                    message.MessageKindId,
                    message.EncryptedBody,
                    message.EncryptedBodyHash,
                    message.PlaintextCharacterCount,
                    (messageWraps ?? Array.Empty<ElectionAnomalyRecipientWrapRecord>())
                        .Select(wrap => new ElectionAnomalyRecipientWrapProjection(
                            wrap.RecipientRoleId,
                            wrap.WrapStatusId,
                            wrap.RecipientPublicAddress,
                            wrap.RecipientKeyFingerprint))
                        .ToArray(),
                    clarificationRequestId);
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<ElectionAnomalyRestrictedMessageProjection>> BuildRestrictedMessageProjectionsAsync(
        IElectionsRepository repository,
        ElectionAnomalyThreadRecord thread)
    {
        var messages = await repository.GetAnomalyMessageEnvelopesAsync(thread.Id);
        var wraps = await repository.GetAnomalyRecipientWrapsAsync(thread.Id);
        var clarificationRequestIds = await ResolveClarificationRequestIdsByEventIdAsync(repository, thread.Id);
        var wrapsByMessageId = wraps
            .GroupBy(x => x.MessageEnvelopeId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        return messages
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(message =>
            {
                wrapsByMessageId.TryGetValue(message.Id, out var messageWraps);
                clarificationRequestIds.TryGetValue(message.EventId, out var clarificationRequestId);
                return new ElectionAnomalyRestrictedMessageProjection(
                    message.Id,
                    message.MessageKindId,
                    message.EncryptedBody,
                    message.EncryptedBodyHash,
                    message.PlaintextCharacterCount,
                    (messageWraps ?? Array.Empty<ElectionAnomalyRecipientWrapRecord>())
                        .Select(wrap => new ElectionAnomalyRestrictedRecipientWrapProjection(
                            wrap.RecipientRoleId,
                            wrap.WrapStatusId))
                        .ToArray(),
                    clarificationRequestId);
            })
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid?>> ResolveClarificationRequestIdsByEventIdAsync(
        IElectionsRepository repository,
        Guid anomalyThreadId)
    {
        var events = await repository.GetAnomalyThreadEventsAsync(anomalyThreadId);
        return events.ToDictionary(
            x => x.Id,
            x => TryReadClarificationRequestId(x.EventPayloadJson));
    }

    private static Guid? TryReadClarificationRequestId(string eventPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(eventPayloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(eventPayloadJson);
            return document.RootElement.TryGetProperty("ClarificationRequestId", out var property) &&
                   property.TryGetGuid(out var clarificationRequestId)
                ? clarificationRequestId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

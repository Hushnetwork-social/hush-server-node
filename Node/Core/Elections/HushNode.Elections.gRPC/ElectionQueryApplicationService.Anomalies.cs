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

        var messages = await BuildEncryptedMessageProjectionsAsync(repository, ownThread, actorPublicAddress);
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

    public async Task<ElectionAnomalyOwnerTriageProjection?> GetElectionAnomalyOwnerTriageAsync(
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
        if (election is null ||
            !string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return null;
        }

        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var projections = new List<ElectionAnomalyOwnerTriageThreadProjection>();
        foreach (var thread in threads.OrderByDescending(x => x.LastUpdatedAt).ThenBy(x => x.Id))
        {
            var messages = await BuildOwnerTriageMessageProjectionsAsync(repository, thread, actorPublicAddress);
            projections.Add(new ElectionAnomalyOwnerTriageThreadProjection(
                thread.Id,
                thread.ElectionId,
                thread.CurrentCategoryId,
                thread.CurrentCaseStateId,
                thread.CurrentThreadHash,
                thread.SeverityCandidateId,
                thread.GovernedDecisionRef,
                thread.SubmitterActorPublicAddress,
                thread.SubmitterRoleContextId,
                thread.LifecycleStateAtSubmission,
                thread.HasOpenClarificationRequest,
                thread.OpenClarificationRequestId,
                thread.CreatedAt,
                thread.LastUpdatedAt,
                messages));
        }

        var allMessages = projections.SelectMany(x => x.Messages).ToArray();
        var continuitySummary = BuildTrusteeContinuitySummary(threads);
        return new ElectionAnomalyOwnerTriageProjection(
            electionId,
            threads.Count,
            threads.Count(IsOpenContinuityThread),
            threads.Count(x => string.Equals(
                x.CurrentCaseStateId,
                ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                StringComparison.Ordinal)),
            projections.Count(x => x.Messages.Any(message => string.Equals(
                message.MessageKindId,
                ElectionAnomalyMessageKindIds.AuthorityResponse,
                StringComparison.Ordinal))),
            threads.Count(x => string.Equals(
                x.SubmitterRoleContextId,
                ElectionAnomalyActorRoleContextIds.ExternalClaimantRegistrar,
                StringComparison.Ordinal)),
            allMessages.Count(message => !string.IsNullOrWhiteSpace(message.CallerOwnerWrap?.EncryptedContentKey)),
            allMessages.Count(message => message.RecipientWraps.Any(wrap => string.Equals(
                wrap.WrapStatusId,
                ElectionAnomalyRecipientWrapStatusIds.PendingBackfill,
                StringComparison.Ordinal))),
            allMessages.Count(message => message.CallerOwnerWrap is null),
            allMessages.Count(message => !string.IsNullOrWhiteSpace(message.AttachmentManifestHash)),
            ResolveGovernedContinuityHandoffStatusId(continuitySummary),
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
            continuitySummary,
            projections);
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
                .ToArray(),
            BuildTrusteeContinuitySummary(threads));
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
            var messages = await BuildRestrictedMessageProjectionsAsync(repository, thread, actorPublicAddress);
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
        ElectionAnomalyThreadRecord thread,
        string? callerPublicAddressForWrapMaterial = null)
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
                    message.RecordedAt,
                    message.EncryptedBody,
                    message.EncryptedBodyHash,
                    message.PlaintextCharacterCount,
                    (messageWraps ?? Array.Empty<ElectionAnomalyRecipientWrapRecord>())
                        .Select(wrap =>
                        {
                            var canExposeWrapMaterial = CanExposeRecipientWrapMaterial(
                                wrap,
                                callerPublicAddressForWrapMaterial);
                            return new ElectionAnomalyRecipientWrapProjection(
                                wrap.RecipientRoleId,
                                wrap.WrapStatusId,
                                wrap.RecipientPublicAddress,
                                wrap.RecipientKeyFingerprint,
                                canExposeWrapMaterial ? wrap.EncryptedContentKey : null,
                                canExposeWrapMaterial ? wrap.WrapAlgorithm : null);
                        })
                        .ToArray(),
                    clarificationRequestId);
            })
            .ToArray();
    }

    private static bool CanExposeRecipientWrapMaterial(
        ElectionAnomalyRecipientWrapRecord wrap,
        string? callerPublicAddressForWrapMaterial) =>
        !string.IsNullOrWhiteSpace(callerPublicAddressForWrapMaterial) &&
        string.Equals(
            wrap.RecipientPublicAddress,
            callerPublicAddressForWrapMaterial,
            StringComparison.Ordinal);

    private static ElectionAnomalyTrusteeContinuitySummaryProjection BuildTrusteeContinuitySummary(
        IReadOnlyList<ElectionAnomalyThreadRecord> threads)
    {
        var continuityThreads = threads
            .Where(x => string.Equals(
                x.CurrentCategoryId,
                ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
                StringComparison.Ordinal))
            .ToArray();

        return new ElectionAnomalyTrusteeContinuitySummaryProjection(
            TrusteeContinuityThreadCount: continuityThreads.Length,
            OpenContinuityThreadCount: continuityThreads.Count(IsOpenContinuityThread),
            AwaitingInformationContinuityThreadCount: continuityThreads.Count(x => string.Equals(
                x.CurrentCaseStateId,
                ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                StringComparison.Ordinal)),
            ClosedContinuityThreadCount: continuityThreads.Count(IsClosedContinuityThread),
            GovernedDecisionLinkedCount: continuityThreads.Count(x => !string.IsNullOrWhiteSpace(x.GovernedDecisionRef)),
            HasContinuityIssue: continuityThreads.Length > 0);
    }

    private static bool IsOpenContinuityThread(ElectionAnomalyThreadRecord thread) =>
        !IsClosedContinuityThread(thread);

    private static bool IsClosedContinuityThread(ElectionAnomalyThreadRecord thread) =>
        string.Equals(thread.CurrentCaseStateId, ElectionAnomalyCaseStateIds.ResolvedNonBlocking, StringComparison.Ordinal) ||
        string.Equals(thread.CurrentCaseStateId, ElectionAnomalyCaseStateIds.ClosedDuplicateFollowup, StringComparison.Ordinal) ||
        string.Equals(thread.CurrentCaseStateId, ElectionAnomalyCaseStateIds.ClosedNoFurtherSubmitterInput, StringComparison.Ordinal);

    private static string ResolveGovernedContinuityHandoffStatusId(
        ElectionAnomalyTrusteeContinuitySummaryProjection continuitySummary)
    {
        if (!continuitySummary.HasContinuityIssue)
        {
            return "continuity_normal";
        }

        return continuitySummary.GovernedDecisionLinkedCount > 0
            ? "governed_path_linked"
            : "governed_path_unavailable";
    }

    private static async Task<IReadOnlyList<ElectionAnomalyOwnerMessageProjection>> BuildOwnerTriageMessageProjectionsAsync(
        IElectionsRepository repository,
        ElectionAnomalyThreadRecord thread,
        string callerOwnerPublicAddress)
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
                return new ElectionAnomalyOwnerMessageProjection(
                    message.Id,
                    message.MessageKindId,
                    message.RecordedAt,
                    message.EncryptedBody,
                    message.EncryptedBodyHash,
                    message.PlaintextCharacterCount,
                    (messageWraps ?? Array.Empty<ElectionAnomalyRecipientWrapRecord>())
                        .Select(wrap => new ElectionAnomalyRestrictedRecipientWrapProjection(
                            wrap.RecipientRoleId,
                            wrap.WrapStatusId))
                        .ToArray(),
                    BuildCallerOwnerWrapProjection(messageWraps, callerOwnerPublicAddress),
                    clarificationRequestId);
            })
            .ToArray();
    }

    private static ElectionAnomalyOwnerCallerWrapProjection? BuildCallerOwnerWrapProjection(
        IReadOnlyList<ElectionAnomalyRecipientWrapRecord>? messageWraps,
        string callerOwnerPublicAddress)
    {
        if (messageWraps is null || string.IsNullOrWhiteSpace(callerOwnerPublicAddress))
        {
            return null;
        }

        var callerWrap = messageWraps
            .Where(wrap =>
                string.Equals(
                    wrap.RecipientRoleId,
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    StringComparison.Ordinal) &&
                string.Equals(
                    wrap.RecipientPublicAddress,
                    callerOwnerPublicAddress,
                    StringComparison.Ordinal))
            .OrderByDescending(wrap => string.Equals(
                wrap.WrapStatusId,
                ElectionAnomalyRecipientWrapStatusIds.Available,
                StringComparison.Ordinal))
            .ThenByDescending(wrap => wrap.RecordedAt)
            .FirstOrDefault();
        if (callerWrap is null)
        {
            return null;
        }

        var canExposeWrapMaterial = string.Equals(
            callerWrap.WrapStatusId,
            ElectionAnomalyRecipientWrapStatusIds.Available,
            StringComparison.Ordinal);
        return new ElectionAnomalyOwnerCallerWrapProjection(
            callerWrap.WrapStatusId,
            callerWrap.RecipientKeyFingerprint,
            canExposeWrapMaterial ? callerWrap.EncryptedContentKey : null,
            canExposeWrapMaterial ? callerWrap.WrapAlgorithm : null);
    }

    private static async Task<IReadOnlyList<ElectionAnomalyRestrictedMessageProjection>> BuildRestrictedMessageProjectionsAsync(
        IElectionsRepository repository,
        ElectionAnomalyThreadRecord thread,
        string? callerPublicAddressForAuditorWrapMaterial = null)
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
                    message.RecordedAt,
                    message.EncryptedBody,
                    message.EncryptedBodyHash,
                    message.PlaintextCharacterCount,
                    (messageWraps ?? Array.Empty<ElectionAnomalyRecipientWrapRecord>())
                        .Select(wrap => new ElectionAnomalyRestrictedRecipientWrapProjection(
                            wrap.RecipientRoleId,
                            wrap.WrapStatusId))
                        .ToArray(),
                    BuildCallerAuditorWrapProjection(messageWraps, callerPublicAddressForAuditorWrapMaterial),
                    clarificationRequestId);
            })
            .ToArray();
    }

    private static ElectionAnomalyAuditorCallerWrapProjection? BuildCallerAuditorWrapProjection(
        IReadOnlyList<ElectionAnomalyRecipientWrapRecord>? messageWraps,
        string? callerPublicAddressForAuditorWrapMaterial)
    {
        if (messageWraps is null || string.IsNullOrWhiteSpace(callerPublicAddressForAuditorWrapMaterial))
        {
            return null;
        }

        var callerWrap = messageWraps
            .Where(wrap =>
                string.Equals(
                    wrap.RecipientRoleId,
                    ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                    StringComparison.Ordinal) &&
                string.Equals(
                    wrap.RecipientPublicAddress,
                    callerPublicAddressForAuditorWrapMaterial,
                    StringComparison.Ordinal))
            .OrderByDescending(wrap => string.Equals(
                wrap.WrapStatusId,
                ElectionAnomalyRecipientWrapStatusIds.Available,
                StringComparison.Ordinal))
            .ThenByDescending(wrap => wrap.RecordedAt)
            .FirstOrDefault();
        if (callerWrap is null)
        {
            return null;
        }

        var canExposeWrapMaterial = string.Equals(
            callerWrap.WrapStatusId,
            ElectionAnomalyRecipientWrapStatusIds.Available,
            StringComparison.Ordinal);
        return new ElectionAnomalyAuditorCallerWrapProjection(
            callerWrap.WrapStatusId,
            callerWrap.RecipientKeyFingerprint,
            canExposeWrapMaterial ? callerWrap.EncryptedContentKey : null,
            canExposeWrapMaterial ? callerWrap.WrapAlgorithm : null);
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

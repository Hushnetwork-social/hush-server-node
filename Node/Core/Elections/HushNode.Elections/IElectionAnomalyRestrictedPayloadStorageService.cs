using HushShared.Elections.Model;
using HushNode.Elections.Storage;
using Olimpo.EntityFramework.Persistency;
using System.Security.Cryptography;

namespace HushNode.Elections;

public interface IElectionAnomalyRestrictedPayloadStorageService
{
    Task<ElectionAnomalyRestrictedPayloadStageResult> StageAsync(
        ElectionAnomalyRestrictedPayloadStageRequest request,
        CancellationToken cancellationToken = default);

    Task<ElectionAnomalyRestrictedPayloadRetrieveResult> RetrieveAsync(
        ElectionAnomalyRestrictedPayloadRetrieveRequest request,
        CancellationToken cancellationToken = default);

    Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> MarkScannerStatusAsync(
        ElectionAnomalyRestrictedPayloadScannerStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> MarkQuarantinedAsync(
        ElectionAnomalyRestrictedPayloadQuarantineRequest request,
        CancellationToken cancellationToken = default);

    Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> MarkPayloadMissingAsync(
        ElectionAnomalyRestrictedPayloadMissingRequest request,
        CancellationToken cancellationToken = default);

    Task<ElectionAnomalyRestrictedPayloadRecord> StoreAsync(
        ElectionId electionId,
        Guid anomalyThreadId,
        byte[] encryptedPayload,
        string encryptedPayloadHash,
        string contentHash,
        long sizeBytes,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<ElectionAnomalyRestrictedPayloadRecord?> GetMetadataAsync(
        string payloadReference,
        CancellationToken cancellationToken = default);
}

public static class ElectionAnomalyRestrictedPayloadReferences
{
    public static string Create(Guid payloadId) =>
        $"{ElectionAnomalyRestrictedPayloadReference.Prefix}{payloadId:D}";
}

public sealed record ElectionAnomalyRestrictedPayloadStageRequest(
    ElectionId ElectionId,
    Guid AnomalyThreadId,
    string ActorPublicAddress,
    string AttachmentKindId,
    byte[] EncryptedPayload,
    string EncryptedPayloadHash,
    string ContentHash,
    long SizeBytes,
    string MimeType,
    Guid? ClarificationRequestId);

public sealed record ElectionAnomalyRestrictedPayloadRetrieveRequest(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string PayloadReference);

public sealed record ElectionAnomalyRestrictedPayloadScannerStatusRequest(
    ElectionId ElectionId,
    string PayloadReference,
    string ScannerStatusId);

public sealed record ElectionAnomalyRestrictedPayloadQuarantineRequest(
    ElectionId ElectionId,
    string PayloadReference);

public sealed record ElectionAnomalyRestrictedPayloadMissingRequest(
    ElectionId ElectionId,
    string PayloadReference);

public sealed record ElectionAnomalyRestrictedPayloadStageResult(
    bool Success,
    string? ErrorMessage,
    string? ValidationCode,
    ElectionAnomalyRestrictedPayloadRecord? PayloadRecord)
{
    public static ElectionAnomalyRestrictedPayloadStageResult Accepted(
        ElectionAnomalyRestrictedPayloadRecord payloadRecord) =>
        new(true, null, null, payloadRecord);

    public static ElectionAnomalyRestrictedPayloadStageResult Rejected(
        string validationCode,
        string errorMessage) =>
        new(false, errorMessage, validationCode, null);
}

public sealed record ElectionAnomalyRestrictedPayloadRetrieveResult(
    bool Success,
    string? ErrorMessage,
    string? ValidationCode,
    ElectionAnomalyRestrictedPayloadRecord? PayloadRecord)
{
    public static ElectionAnomalyRestrictedPayloadRetrieveResult Accepted(
        ElectionAnomalyRestrictedPayloadRecord payloadRecord) =>
        new(true, null, null, payloadRecord);

    public static ElectionAnomalyRestrictedPayloadRetrieveResult Rejected(
        string validationCode,
        string errorMessage) =>
        new(false, errorMessage, validationCode, null);
}

public sealed record ElectionAnomalyRestrictedPayloadStatusUpdateResult(
    bool Success,
    string? ErrorMessage,
    string? ValidationCode,
    ElectionAnomalyRestrictedPayloadRecord? PayloadRecord,
    int UpdatedAttachmentManifestCount)
{
    public static ElectionAnomalyRestrictedPayloadStatusUpdateResult Accepted(
        ElectionAnomalyRestrictedPayloadRecord? payloadRecord,
        int updatedAttachmentManifestCount) =>
        new(true, null, null, payloadRecord, updatedAttachmentManifestCount);

    public static ElectionAnomalyRestrictedPayloadStatusUpdateResult Rejected(
        string validationCode,
        string errorMessage) =>
        new(false, errorMessage, validationCode, null, 0);
}

public sealed class ElectionAnomalyRestrictedPayloadStorageService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : IElectionAnomalyRestrictedPayloadStorageService
{
    private const long EncryptedPayloadTransportOverheadAllowanceBytes = 64L * 1024L;

    public async Task<ElectionAnomalyRestrictedPayloadStageResult> StageAsync(
        ElectionAnomalyRestrictedPayloadStageRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var initialRejection = ValidateStageRequestShape(request);
        if (initialRejection is not null)
        {
            return initialRejection;
        }

        var isSubmitterClarificationEvidence = string.Equals(
            request.AttachmentKindId,
            ElectionAnomalyAttachmentKindIds.AuthorityRequestedEvidence,
            StringComparison.Ordinal);
        var perPayloadLimit = isSubmitterClarificationEvidence
            ? ElectionAnomalyLimits.SubmitterClarificationEvidenceMaxBytes
            : ElectionAnomalyLimits.AuthorityEvidenceMaxBytes;

        if (request.SizeBytes > perPayloadLimit ||
            request.EncryptedPayload.LongLength > perPayloadLimit + EncryptedPayloadTransportOverheadAllowanceBytes)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentSizeExceeded,
                "Anomaly evidence payload exceeds the configured per-payload size limit.");
        }

        var actualEncryptedPayloadHash = ComputeSha256Reference(request.EncryptedPayload);
        if (!string.Equals(actualEncryptedPayloadHash, request.EncryptedPayloadHash, StringComparison.Ordinal))
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentHashInvalid,
                "Encrypted anomaly evidence hash does not match the staged payload bytes.");
        }

        using var unitOfWork = unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(request.ElectionId);
        var thread = await repository.GetAnomalyThreadAsync(request.AnomalyThreadId);
        if (election is null || thread is null || thread.ElectionId != request.ElectionId)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.ClarificationRequestNotOpen,
                "Anomaly thread was not found for the restricted evidence payload.");
        }

        var authorizationRejection = await ValidateStageAuthorizationAsync(
            repository,
            election,
            thread,
            request,
            isSubmitterClarificationEvidence);
        if (authorizationRejection is not null)
        {
            return authorizationRejection;
        }

        var payloadId = Guid.NewGuid();
        var record = new ElectionAnomalyRestrictedPayloadRecord(
            payloadId,
            request.ElectionId,
            request.AnomalyThreadId,
            ElectionAnomalyRestrictedPayloadReferences.Create(payloadId),
            request.EncryptedPayload,
            request.EncryptedPayloadHash,
            request.ContentHash,
            request.SizeBytes,
            request.MimeType.Trim(),
            ElectionAnomalyEvidenceScannerStatusIds.Pending,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            DateTime.UtcNow);

        await repository.SaveAnomalyRestrictedPayloadAsync(record);
        await unitOfWork.CommitAsync();

        return ElectionAnomalyRestrictedPayloadStageResult.Accepted(record);
    }

    public async Task<ElectionAnomalyRestrictedPayloadRetrieveResult> RetrieveAsync(
        ElectionAnomalyRestrictedPayloadRetrieveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ActorPublicAddress))
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.BodyRequired,
                "Restricted anomaly evidence payload retrieval request is incomplete.");
        }

        var payloadReference = request.PayloadReference?.Trim() ?? string.Empty;
        if (!ElectionAnomalyRestrictedPayloadReference.IsValid(payloadReference))
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload reference is invalid.");
        }

        using var unitOfWork = unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var payloadRecord = await repository.GetAnomalyRestrictedPayloadAsync(payloadReference);
        if (payloadRecord is null || payloadRecord.ElectionId != request.ElectionId)
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload was not found for this election.");
        }

        if (!string.Equals(
                payloadRecord.PayloadAvailabilityStatusId,
                ElectionAnomalyPayloadAvailabilityStatusIds.Available,
                StringComparison.Ordinal))
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload is not available.");
        }

        if (string.Equals(
                payloadRecord.ScannerStatusId,
                ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
                StringComparison.Ordinal))
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                "Restricted anomaly evidence payload is quarantined.");
        }

        var actualEncryptedPayloadHash = ComputeSha256Reference(payloadRecord.EncryptedPayload);
        if (!string.Equals(actualEncryptedPayloadHash, payloadRecord.EncryptedPayloadHash, StringComparison.Ordinal))
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentHashInvalid,
                "Restricted anomaly evidence payload hash does not match the stored bytes.");
        }

        var election = await repository.GetElectionAsync(request.ElectionId);
        var thread = await repository.GetAnomalyThreadAsync(payloadRecord.AnomalyThreadId);
        if (election is null || thread is null || thread.ElectionId != request.ElectionId)
        {
            return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload is not bound to an active anomaly thread.");
        }

        var authorizationRejection = await ValidateRetrieveAuthorizationAsync(
            repository,
            election,
            thread,
            request.ActorPublicAddress);
        if (authorizationRejection is not null)
        {
            return authorizationRejection;
        }

        return ElectionAnomalyRestrictedPayloadRetrieveResult.Accepted(payloadRecord);
    }

    public async Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> MarkScannerStatusAsync(
        ElectionAnomalyRestrictedPayloadScannerStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ElectionAnomalyEvidenceScannerStatusIds.IsKnown(request.ScannerStatusId))
        {
            return ElectionAnomalyRestrictedPayloadStatusUpdateResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                "Restricted anomaly evidence scanner status is invalid.");
        }

        return await UpdatePayloadAndManifestStatusAsync(
            request.ElectionId,
            request.PayloadReference,
            allowMissingPayload: false,
            payload => payload with
            {
                ScannerStatusId = request.ScannerStatusId,
                PayloadAvailabilityStatusId = string.Equals(
                    request.ScannerStatusId,
                    ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
                    StringComparison.Ordinal)
                    ? ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined
                    : payload.PayloadAvailabilityStatusId,
                LastCheckedAt = DateTime.UtcNow,
            },
            manifest => manifest with
            {
                ScannerStatusId = request.ScannerStatusId,
                PayloadAvailabilityStatusId = string.Equals(
                    request.ScannerStatusId,
                    ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
                    StringComparison.Ordinal)
                    ? ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined
                    : manifest.PayloadAvailabilityStatusId,
                ValidationStatusId = ResolveAttachmentValidationStatusId(request.ScannerStatusId),
            },
            cancellationToken);
    }

    public async Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> MarkQuarantinedAsync(
        ElectionAnomalyRestrictedPayloadQuarantineRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdatePayloadAndManifestStatusAsync(
            request.ElectionId,
            request.PayloadReference,
            allowMissingPayload: false,
            payload => payload with
            {
                ScannerStatusId = ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
                PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined,
                LastCheckedAt = DateTime.UtcNow,
            },
            manifest => manifest with
            {
                ScannerStatusId = ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
                PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined,
                ValidationStatusId = ElectionAnomalyAttachmentValidationStatusIds.Rejected,
            },
            cancellationToken);
    }

    public async Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> MarkPayloadMissingAsync(
        ElectionAnomalyRestrictedPayloadMissingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdatePayloadAndManifestStatusAsync(
            request.ElectionId,
            request.PayloadReference,
            allowMissingPayload: true,
            payload => payload with
            {
                PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
                LastCheckedAt = DateTime.UtcNow,
            },
            manifest => manifest with
            {
                PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
            },
            cancellationToken);
    }

    public async Task<ElectionAnomalyRestrictedPayloadRecord> StoreAsync(
        ElectionId electionId,
        Guid anomalyThreadId,
        byte[] encryptedPayload,
        string encryptedPayloadHash,
        string contentHash,
        long sizeBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payloadId = Guid.NewGuid();
        var record = new ElectionAnomalyRestrictedPayloadRecord(
            payloadId,
            electionId,
            anomalyThreadId,
            ElectionAnomalyRestrictedPayloadReferences.Create(payloadId),
            encryptedPayload,
            encryptedPayloadHash,
            contentHash,
            sizeBytes,
            mimeType,
            ElectionAnomalyEvidenceScannerStatusIds.Pending,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            DateTime.UtcNow);

        using var unitOfWork = unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        await repository.SaveAnomalyRestrictedPayloadAsync(record);
        await unitOfWork.CommitAsync();

        return record;
    }

    public async Task<ElectionAnomalyRestrictedPayloadRecord?> GetMetadataAsync(
        string payloadReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ElectionAnomalyRestrictedPayloadReference.IsValid(payloadReference))
        {
            return null;
        }

        using var unitOfWork = unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        return await repository.GetAnomalyRestrictedPayloadAsync(payloadReference);
    }

    private static ElectionAnomalyRestrictedPayloadStageResult? ValidateStageRequestShape(
        ElectionAnomalyRestrictedPayloadStageRequest request)
    {
        if (request.AnomalyThreadId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ActorPublicAddress) ||
            string.IsNullOrWhiteSpace(request.AttachmentKindId) ||
            request.EncryptedPayload.Length == 0 ||
            string.IsNullOrWhiteSpace(request.EncryptedPayloadHash) ||
            string.IsNullOrWhiteSpace(request.ContentHash) ||
            string.IsNullOrWhiteSpace(request.MimeType) ||
            request.SizeBytes <= 0)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.BodyRequired,
                "Restricted anomaly evidence payload staging request is incomplete.");
        }

        if (!ElectionAnomalyAttachmentKindIds.IsKnown(request.AttachmentKindId))
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentKindInvalid,
                "Anomaly evidence attachment kind is not supported.");
        }

        if (string.Equals(
                request.AttachmentKindId,
                ElectionAnomalyAttachmentKindIds.SubmitterEvidence,
                StringComparison.Ordinal))
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentSubmitterNotAllowed,
                "Unprompted submitter anomaly evidence is not enabled in v1.");
        }

        if (string.Equals(
                request.AttachmentKindId,
                ElectionAnomalyAttachmentKindIds.RestrictedOperationalEvidence,
                StringComparison.Ordinal))
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentOperationalEvidenceDisabled,
                "Restricted operational anomaly evidence is disabled unless policy enables it.");
        }

        if (!ElectionAnomalyEvidenceMimeTypes.IsAllowed(request.MimeType))
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentMimeTypeInvalid,
                "Anomaly evidence MIME type is not allowed.");
        }

        if (!IsSha256Reference(request.EncryptedPayloadHash) ||
            !IsSha256Reference(request.ContentHash))
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentHashInvalid,
                "Anomaly evidence payload hashes must be lowercase sha256 references.");
        }

        return null;
    }

    private async Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> UpdatePayloadAndManifestStatusAsync(
        ElectionId electionId,
        string payloadReference,
        bool allowMissingPayload,
        Func<ElectionAnomalyRestrictedPayloadRecord, ElectionAnomalyRestrictedPayloadRecord> updatePayload,
        Func<ElectionAnomalyAttachmentManifestRecord, ElectionAnomalyAttachmentManifestRecord> updateManifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedPayloadReference = payloadReference?.Trim() ?? string.Empty;
        if (!ElectionAnomalyRestrictedPayloadReference.IsValid(normalizedPayloadReference))
        {
            return ElectionAnomalyRestrictedPayloadStatusUpdateResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload reference is invalid.");
        }

        using var unitOfWork = unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var payloadRecord = await repository.GetAnomalyRestrictedPayloadAsync(normalizedPayloadReference);
        var attachmentManifests = (await repository.GetAnomalyAttachmentManifestsByPayloadReferenceAsync(
                normalizedPayloadReference))
            .Where(manifest => manifest.ElectionId == electionId)
            .ToArray();

        if (payloadRecord is not null && payloadRecord.ElectionId != electionId)
        {
            return ElectionAnomalyRestrictedPayloadStatusUpdateResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload was not found for this election.");
        }

        if (payloadRecord is null && (!allowMissingPayload || attachmentManifests.Length == 0))
        {
            return ElectionAnomalyRestrictedPayloadStatusUpdateResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload was not found for this election.");
        }

        ElectionAnomalyRestrictedPayloadRecord? updatedPayload = null;
        if (payloadRecord is not null)
        {
            updatedPayload = updatePayload(payloadRecord);
            await repository.UpdateAnomalyRestrictedPayloadAsync(updatedPayload);
        }

        foreach (var attachmentManifest in attachmentManifests)
        {
            await repository.UpdateAnomalyAttachmentManifestAsync(updateManifest(attachmentManifest));
        }

        await unitOfWork.CommitAsync();

        return ElectionAnomalyRestrictedPayloadStatusUpdateResult.Accepted(
            updatedPayload,
            attachmentManifests.Length);
    }

    private static string ResolveAttachmentValidationStatusId(string scannerStatusId) =>
        scannerStatusId switch
        {
            ElectionAnomalyEvidenceScannerStatusIds.NotRequired =>
                ElectionAnomalyAttachmentValidationStatusIds.ManifestOnly,
            ElectionAnomalyEvidenceScannerStatusIds.Clear =>
                ElectionAnomalyAttachmentValidationStatusIds.Accepted,
            ElectionAnomalyEvidenceScannerStatusIds.Quarantined =>
                ElectionAnomalyAttachmentValidationStatusIds.Rejected,
            _ => ElectionAnomalyAttachmentValidationStatusIds.PendingScan,
        };

    private static async Task<ElectionAnomalyRestrictedPayloadRetrieveResult?> ValidateRetrieveAuthorizationAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionAnomalyThreadRecord thread,
        string actorPublicAddress)
    {
        if (IsAnomalyAuthorityActor(election, actorPublicAddress))
        {
            return null;
        }

        var auditorGrant = await repository.GetReportAccessGrantAsync(election.ElectionId, actorPublicAddress);
        if (auditorGrant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor &&
            string.Equals(auditorGrant.ActorPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return null;
        }

        var readDecision = ElectionAnomalyAuthorization.CanActorReadOwnThread(thread, actorPublicAddress);
        if (readDecision.CanRead)
        {
            return null;
        }

        return ElectionAnomalyRestrictedPayloadRetrieveResult.Rejected(
            readDecision.ValidationCode ?? ElectionAnomalyValidationCodes.ReadForbidden,
            "Only the original anomaly submitter, election authority, or designated auditor can retrieve this restricted evidence payload.");
    }

    private static async Task<ElectionAnomalyRestrictedPayloadStageResult?> ValidateStageAuthorizationAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionAnomalyThreadRecord thread,
        ElectionAnomalyRestrictedPayloadStageRequest request,
        bool isSubmitterClarificationEvidence)
    {
        if (IsAnomalyAuthorityActor(election, request.ActorPublicAddress))
        {
            if (!string.Equals(
                    request.AttachmentKindId,
                    ElectionAnomalyAttachmentKindIds.AuthorityEvidence,
                    StringComparison.Ordinal))
            {
                return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                    ElectionAnomalyValidationCodes.AttachmentKindInvalid,
                    "Election authority staged evidence must use authority evidence in v1.");
            }

            if (request.ClarificationRequestId.HasValue)
            {
                return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                    ElectionAnomalyValidationCodes.AttachmentRequestMismatch,
                    "Election authority evidence is attached to the anomaly thread, not a submitter clarification request.");
            }

            var authorityManifests = (await repository.GetAnomalyAttachmentManifestsAsync(thread.Id))
                .Where(manifest => string.Equals(
                    manifest.AttachmentKindId,
                    ElectionAnomalyAttachmentKindIds.AuthorityEvidence,
                    StringComparison.Ordinal))
                .ToArray();
            if (authorityManifests.Length >= ElectionAnomalyLimits.AuthorityEvidenceMaxCount)
            {
                return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                    ElectionAnomalyValidationCodes.AttachmentCountExceeded,
                    "Authority anomaly evidence exceeds the configured count limit for this thread.");
            }

            if (authorityManifests.Sum(manifest => manifest.SizeBytes) + request.SizeBytes >
                ElectionAnomalyLimits.AuthorityEvidenceMaxTotalBytes)
            {
                return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                    ElectionAnomalyValidationCodes.AttachmentSizeExceeded,
                    "Authority anomaly evidence exceeds the configured total size limit for this thread.");
            }

            return null;
        }

        if (!isSubmitterClarificationEvidence)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentKindInvalid,
                "Submitters can only stage evidence requested by an open authority clarification request.");
        }

        var readDecision = ElectionAnomalyAuthorization.CanActorReadOwnThread(thread, request.ActorPublicAddress);
        if (!readDecision.CanRead)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                readDecision.ValidationCode ?? ElectionAnomalyValidationCodes.ReadForbidden,
                "Only the original anomaly submitter or election authority can stage restricted evidence.");
        }

        if (!thread.HasOpenClarificationRequest)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.FollowupNotRequested,
                "Submitter restricted evidence requires an open authority request.");
        }

        if (!request.ClarificationRequestId.HasValue ||
            thread.OpenClarificationRequestId != request.ClarificationRequestId)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.ClarificationRequestNotOpen,
                "Submitter restricted evidence does not match the open authority request.");
        }

        var requestedManifests = (await repository.GetAnomalyAttachmentManifestsAsync(thread.Id))
            .Where(manifest =>
                string.Equals(
                    manifest.AttachmentKindId,
                    ElectionAnomalyAttachmentKindIds.AuthorityRequestedEvidence,
                    StringComparison.Ordinal) &&
                manifest.ClarificationRequestId == request.ClarificationRequestId)
            .ToArray();
        if (requestedManifests.Length >= ElectionAnomalyLimits.SubmitterClarificationEvidenceMaxCount)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentCountExceeded,
                "Submitter clarification evidence exceeds the configured count limit for this request.");
        }

        if (requestedManifests.Sum(manifest => manifest.SizeBytes) + request.SizeBytes >
            ElectionAnomalyLimits.SubmitterClarificationEvidenceMaxTotalBytes)
        {
            return ElectionAnomalyRestrictedPayloadStageResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentSizeExceeded,
                "Submitter clarification evidence exceeds the configured total size limit for this request.");
        }

        return null;
    }

    private static bool IsAnomalyAuthorityActor(ElectionRecord election, string actorPublicAddress) =>
        string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal);

    private static string ComputeSha256Reference(byte[] value) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant()}";

    private static bool IsSha256Reference(string? value)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(prefix, StringComparison.Ordinal) ||
            value.Length != prefix.Length + 64)
        {
            return false;
        }

        return value[prefix.Length..].All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

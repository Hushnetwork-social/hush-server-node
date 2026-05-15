using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionAnomalyTrustedScannerOrchestrationService
{
    Task<ElectionAnomalyTrustedScannerHandoffResult> ApplyTrustedScannerHandoffAsync(
        ElectionAnomalyTrustedScannerHandoffRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ElectionAnomalyTrustedScannerHandoffRequest(
    ElectionId ElectionId,
    string PayloadReference,
    string EncryptedPayloadHash,
    string ContentHash,
    string ScannerStatusId,
    string ScannerName,
    string ScannerRunId,
    string? DiagnosticMessage = null);

public sealed record ElectionAnomalyTrustedScannerHandoffResult(
    bool Success,
    string? ErrorMessage,
    string? ValidationCode,
    ElectionAnomalyRestrictedPayloadRecord? PayloadRecord,
    int UpdatedAttachmentManifestCount)
{
    public static ElectionAnomalyTrustedScannerHandoffResult Accepted(
        ElectionAnomalyRestrictedPayloadRecord? payloadRecord,
        int updatedAttachmentManifestCount) =>
        new(true, null, null, payloadRecord, updatedAttachmentManifestCount);

    public static ElectionAnomalyTrustedScannerHandoffResult Rejected(
        string validationCode,
        string errorMessage) =>
        new(false, errorMessage, validationCode, null, 0);
}

public sealed class ElectionAnomalyTrustedScannerOrchestrationService(
    IElectionAnomalyRestrictedPayloadStorageService restrictedPayloadStorageService)
    : IElectionAnomalyTrustedScannerOrchestrationService
{
    public async Task<ElectionAnomalyTrustedScannerHandoffResult> ApplyTrustedScannerHandoffAsync(
        ElectionAnomalyTrustedScannerHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var shapeRejection = ValidateRequestShape(request);
        if (shapeRejection is not null)
        {
            return shapeRejection;
        }

        var payloadReference = request.PayloadReference.Trim();
        var payloadRecord = await restrictedPayloadStorageService.GetMetadataAsync(
            payloadReference,
            cancellationToken);
        if (payloadRecord is null || payloadRecord.ElectionId != request.ElectionId)
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload was not found for this election.");
        }

        if (!string.Equals(payloadRecord.EncryptedPayloadHash, request.EncryptedPayloadHash.Trim(), StringComparison.Ordinal) ||
            !string.Equals(payloadRecord.ContentHash, request.ContentHash.Trim(), StringComparison.Ordinal))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentHashInvalid,
                "Trusted scanner handoff hashes do not match the restricted anomaly evidence payload.");
        }

        if (string.Equals(
                payloadRecord.PayloadAvailabilityStatusId,
                ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined,
                StringComparison.Ordinal) &&
            !string.Equals(request.ScannerStatusId, ElectionAnomalyEvidenceScannerStatusIds.Quarantined, StringComparison.Ordinal))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                "Quarantined anomaly evidence cannot be cleared by a later trusted scanner handoff.");
        }

        if (!string.Equals(
                payloadRecord.PayloadAvailabilityStatusId,
                ElectionAnomalyPayloadAvailabilityStatusIds.Available,
                StringComparison.Ordinal) &&
            !string.Equals(request.ScannerStatusId, ElectionAnomalyEvidenceScannerStatusIds.Quarantined, StringComparison.Ordinal))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                "Trusted scanner handoff can only clear available restricted anomaly evidence payloads.");
        }

        var updateResult = await ApplyStatusUpdateAsync(
            request.ElectionId,
            payloadReference,
            request.ScannerStatusId,
            cancellationToken);

        return updateResult.Success
            ? ElectionAnomalyTrustedScannerHandoffResult.Accepted(
                updateResult.PayloadRecord,
                updateResult.UpdatedAttachmentManifestCount)
            : ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                updateResult.ValidationCode ?? ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                updateResult.ErrorMessage ?? "Trusted scanner handoff could not update restricted anomaly evidence status.");
    }

    private async Task<ElectionAnomalyRestrictedPayloadStatusUpdateResult> ApplyStatusUpdateAsync(
        ElectionId electionId,
        string payloadReference,
        string scannerStatusId,
        CancellationToken cancellationToken) =>
        string.Equals(scannerStatusId, ElectionAnomalyEvidenceScannerStatusIds.Quarantined, StringComparison.Ordinal)
            ? await restrictedPayloadStorageService.MarkQuarantinedAsync(
                new ElectionAnomalyRestrictedPayloadQuarantineRequest(electionId, payloadReference),
                cancellationToken)
            : await restrictedPayloadStorageService.MarkScannerStatusAsync(
                new ElectionAnomalyRestrictedPayloadScannerStatusRequest(electionId, payloadReference, scannerStatusId),
                cancellationToken);

    private static ElectionAnomalyTrustedScannerHandoffResult? ValidateRequestShape(
        ElectionAnomalyTrustedScannerHandoffRequest request)
    {
        if (request.ElectionId == ElectionId.Empty ||
            string.IsNullOrWhiteSpace(request.PayloadReference) ||
            string.IsNullOrWhiteSpace(request.EncryptedPayloadHash) ||
            string.IsNullOrWhiteSpace(request.ContentHash) ||
            string.IsNullOrWhiteSpace(request.ScannerStatusId) ||
            string.IsNullOrWhiteSpace(request.ScannerName) ||
            string.IsNullOrWhiteSpace(request.ScannerRunId))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.BodyRequired,
                "Trusted scanner handoff request is incomplete.");
        }

        if (!ElectionAnomalyRestrictedPayloadReference.IsValid(request.PayloadReference.Trim()))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence payload reference is invalid.");
        }

        if (!IsSha256Reference(request.EncryptedPayloadHash) ||
            !IsSha256Reference(request.ContentHash))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentHashInvalid,
                "Trusted scanner handoff hashes must be lowercase sha256 references.");
        }

        if (!IsTerminalTrustedScannerStatus(request.ScannerStatusId))
        {
            return ElectionAnomalyTrustedScannerHandoffResult.Rejected(
                ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                "Trusted scanner handoff must provide a terminal scanner status.");
        }

        return null;
    }

    private static bool IsTerminalTrustedScannerStatus(string scannerStatusId) =>
        string.Equals(scannerStatusId, ElectionAnomalyEvidenceScannerStatusIds.Clear, StringComparison.Ordinal) ||
        string.Equals(scannerStatusId, ElectionAnomalyEvidenceScannerStatusIds.Quarantined, StringComparison.Ordinal) ||
        string.Equals(scannerStatusId, ElectionAnomalyEvidenceScannerStatusIds.ScannerUnavailable, StringComparison.Ordinal);

    private static bool IsSha256Reference(string? value)
    {
        const string prefix = "sha256:";

        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.Length == prefix.Length + 64 &&
               value[prefix.Length..].All(IsLowerHex);
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}

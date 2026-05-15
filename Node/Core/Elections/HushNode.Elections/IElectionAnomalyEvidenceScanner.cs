using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionAnomalyEvidenceScanner
{
    Task<ElectionAnomalyEvidenceScanResult> ScanAsync(
        ElectionAnomalyEvidenceScanRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ElectionAnomalyEvidenceScanRequest(
    ElectionId ElectionId,
    Guid AnomalyThreadId,
    string PayloadReference,
    byte[] PayloadBytes,
    string MimeType,
    bool PayloadIsEncrypted);

public sealed record ElectionAnomalyEvidenceScanResult(
    string ScannerStatusId,
    string? ValidationCode,
    string DiagnosticMessage)
{
    public static ElectionAnomalyEvidenceScanResult Pending(string diagnosticMessage) =>
        new(ElectionAnomalyEvidenceScannerStatusIds.Pending, null, diagnosticMessage);

    public static ElectionAnomalyEvidenceScanResult Clear(string diagnosticMessage) =>
        new(ElectionAnomalyEvidenceScannerStatusIds.Clear, null, diagnosticMessage);

    public static ElectionAnomalyEvidenceScanResult Quarantined(
        string validationCode,
        string diagnosticMessage) =>
        new(ElectionAnomalyEvidenceScannerStatusIds.Quarantined, validationCode, diagnosticMessage);

    public static ElectionAnomalyEvidenceScanResult ScannerUnavailable(string diagnosticMessage) =>
        new(ElectionAnomalyEvidenceScannerStatusIds.ScannerUnavailable, null, diagnosticMessage);
}

public sealed class ElectionAnomalyEvidenceScanner : IElectionAnomalyEvidenceScanner
{
    private static readonly HashSet<string> QuarantinedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-msdownload",
        "application/x-dosexec",
        "application/x-executable",
        "application/x-sh",
        "application/x-bat",
        "application/zip",
        "application/x-zip-compressed",
        "application/vnd.microsoft.portable-executable",
    };

    public Task<ElectionAnomalyEvidenceScanResult> ScanAsync(
        ElectionAnomalyEvidenceScanRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.PayloadBytes.Length == 0)
        {
            return Task.FromResult(ElectionAnomalyEvidenceScanResult.ScannerUnavailable(
                "Restricted anomaly evidence scanner received no payload bytes."));
        }

        if (!ElectionAnomalyRestrictedPayloadReference.IsValid(request.PayloadReference))
        {
            return Task.FromResult(ElectionAnomalyEvidenceScanResult.Quarantined(
                ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid,
                "Restricted anomaly evidence scanner received an invalid payload reference."));
        }

        if (!ElectionAnomalyEvidenceMimeTypes.IsAllowed(request.MimeType) ||
            QuarantinedMimeTypes.Contains(request.MimeType.Trim()))
        {
            return Task.FromResult(ElectionAnomalyEvidenceScanResult.Quarantined(
                ElectionAnomalyValidationCodes.AttachmentMimeTypeInvalid,
                "Restricted anomaly evidence scanner rejected an unsupported or executable-like MIME type."));
        }

        if (request.PayloadIsEncrypted)
        {
            return Task.FromResult(ElectionAnomalyEvidenceScanResult.Pending(
                "Restricted anomaly evidence payload is encrypted; plaintext scanning requires a trusted scanner handoff."));
        }

        if (LooksLikeExecutablePayload(request.PayloadBytes))
        {
            return Task.FromResult(ElectionAnomalyEvidenceScanResult.Quarantined(
                ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid,
                "Restricted anomaly evidence scanner detected executable-like payload bytes."));
        }

        return Task.FromResult(ElectionAnomalyEvidenceScanResult.Clear(
            "Restricted anomaly evidence payload passed the default policy scanner."));
    }

    private static bool LooksLikeExecutablePayload(byte[] payloadBytes) =>
        payloadBytes.Length >= 2 &&
        payloadBytes[0] == 0x4d &&
        payloadBytes[1] == 0x5a;
}

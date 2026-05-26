using System.Data;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public sealed class ElectionWebClientDeploymentProofObservationService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : IElectionWebClientDeploymentProofObservationService
{
    public async Task<WebClientDeploymentProofObservationResult> RecordAsync(
        WebClientDeploymentProofObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var observedAtUtc = DateTime.UtcNow;
        var record = CreateRecord(request, observedAtUtc);

        using var unitOfWork = unitOfWorkProvider.CreateWritable(IsolationLevel.ReadCommitted);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        await repository.SaveWebClientDeploymentProofObservationAsync(record);
        await unitOfWork.CommitAsync();

        return new WebClientDeploymentProofObservationResult(
            WasRecorded: true,
            record.EvidenceStatus,
            record.MismatchCode,
            ResolvePublicSummary(record));
    }

    internal static ElectionWebClientDeploymentProofObservationRecord CreateRecord(
        WebClientDeploymentProofObservationRequest request,
        DateTime observedAtUtc)
    {
        try
        {
            var evidenceStatus = ResolveEvidenceStatus(request);
            var mismatchCode = ResolveMismatchCode(request, evidenceStatus);
            var isMissing = evidenceStatus == ElectionDeploymentProofEvidenceStatus.Missing;
            var generatedAtUtc = TryParseUtc(request.GeneratedAtUtc);

            return new ElectionWebClientDeploymentProofObservationRecord(
                Guid.NewGuid(),
                NormalizeOptional(request.ElectionId),
                NormalizeRequired(request.ObservationScope, nameof(request.ObservationScope)),
                request.SchemaVersion,
                request.ComponentId,
                isMissing ? null : NormalizeOptional(request.DeploymentProofId),
                isMissing ? null : NormalizeOptional(request.DeploymentTarget),
                isMissing ? null : NormalizeOptional(request.SourceRef),
                isMissing ? null : NormalizeOptional(request.WebArtifactHash),
                isMissing ? null : NormalizeOptional(request.ClientBundleHash),
                isMissing ? null : NormalizeOptional(request.PackageHash),
                isMissing ? null : NormalizeOptional(request.PublicPackageRef),
                request.DeploymentProtocolVersion,
                evidenceStatus,
                mismatchCode,
                observedAtUtc,
                generatedAtUtc);
        }
        catch (ArgumentException)
        {
            return new ElectionWebClientDeploymentProofObservationRecord(
                Guid.NewGuid(),
                NormalizeOptional(request.ElectionId),
                NormalizeRequired(request.ObservationScope, nameof(request.ObservationScope)),
                ElectionDeploymentProofConstants.WebClientDeploymentProofHandshakeSchemaVersion,
                ElectionDeploymentProofConstants.WebClientComponentId,
                DeploymentProofId: null,
                DeploymentTarget: null,
                SourceRef: null,
                WebArtifactHash: null,
                ClientBundleHash: null,
                PackageHash: null,
                PublicPackageRef: null,
                ElectionDeploymentProofConstants.DeploymentProtocolVersion,
                ElectionDeploymentProofEvidenceStatus.Blocked,
                ElectionDeploymentProofConstants.Feat144WebClientProofPrivateOnlyCode,
                observedAtUtc,
                GeneratedAtUtc: null);
        }
    }

    private static ElectionDeploymentProofEvidenceStatus ResolveEvidenceStatus(
        WebClientDeploymentProofObservationRequest request)
    {
        var normalizedStatus = NormalizeOptional(request.EvidenceStatus)?.Replace("-", "_", StringComparison.Ordinal);
        var hasProofIdentity =
            !string.IsNullOrWhiteSpace(request.DeploymentProofId) ||
            !string.IsNullOrWhiteSpace(request.ClientBundleHash) ||
            !string.IsNullOrWhiteSpace(request.WebArtifactHash);

        if (string.Equals(normalizedStatus, "missing", StringComparison.OrdinalIgnoreCase) || !hasProofIdentity)
        {
            return ElectionDeploymentProofEvidenceStatus.Missing;
        }

        if (!string.Equals(
                request.SchemaVersion,
                ElectionDeploymentProofConstants.WebClientDeploymentProofHandshakeSchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.ComponentId,
                ElectionDeploymentProofConstants.WebClientComponentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.DeploymentProtocolVersion,
                ElectionDeploymentProofConstants.DeploymentProtocolVersion,
                StringComparison.Ordinal))
        {
            return ElectionDeploymentProofEvidenceStatus.Unknown;
        }

        return normalizedStatus?.ToLowerInvariant() switch
        {
            "accepted" => ElectionDeploymentProofEvidenceStatus.Accepted,
            "accepted_with_limitations" => ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations,
            "degraded" => ElectionDeploymentProofEvidenceStatus.Degraded,
            "stale" => ElectionDeploymentProofEvidenceStatus.Stale,
            "superseded" => ElectionDeploymentProofEvidenceStatus.Superseded,
            "private_only" => ElectionDeploymentProofEvidenceStatus.Blocked,
            "unknown" => ElectionDeploymentProofEvidenceStatus.Unknown,
            _ => ElectionDeploymentProofEvidenceStatus.Unknown,
        };
    }

    private static string? ResolveMismatchCode(
        WebClientDeploymentProofObservationRequest request,
        ElectionDeploymentProofEvidenceStatus evidenceStatus)
    {
        var normalizedStatus = NormalizeOptional(request.EvidenceStatus)?.Replace("-", "_", StringComparison.Ordinal);
        if (string.Equals(normalizedStatus, "private_only", StringComparison.OrdinalIgnoreCase))
        {
            return ElectionDeploymentProofConstants.Feat144WebClientProofPrivateOnlyCode;
        }

        return evidenceStatus switch
        {
            ElectionDeploymentProofEvidenceStatus.Missing =>
                ElectionDeploymentProofConstants.Feat144WebClientProofMissingCode,
            ElectionDeploymentProofEvidenceStatus.Stale =>
                ElectionDeploymentProofConstants.Feat144WebClientProofStaleCode,
            ElectionDeploymentProofEvidenceStatus.Superseded =>
                ElectionDeploymentProofConstants.Feat144WebClientProofSupersededCode,
            ElectionDeploymentProofEvidenceStatus.Unknown =>
                ElectionDeploymentProofConstants.Feat144WebClientProofUnknownCode,
            ElectionDeploymentProofEvidenceStatus.Blocked =>
                ElectionDeploymentProofConstants.Feat144WebClientProofPrivateOnlyCode,
            _ => null,
        };
    }

    private static string ResolvePublicSummary(ElectionWebClientDeploymentProofObservationRecord record) =>
        record.EvidenceStatus switch
        {
            ElectionDeploymentProofEvidenceStatus.Accepted =>
                "WebClient deployment proof metadata was observed from the browser bundle.",
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations =>
                "WebClient deployment proof metadata was observed with explicit browser/cache limitations.",
            ElectionDeploymentProofEvidenceStatus.Missing =>
                "The browser did not provide WebClient deployment proof metadata for the observed action.",
            ElectionDeploymentProofEvidenceStatus.Mismatch =>
                "The observed WebClient proof does not match the expected deployment proof.",
            ElectionDeploymentProofEvidenceStatus.Blocked =>
                "The WebClient proof observation contained restricted material and cannot support public proof claims.",
            _ => "WebClient deployment proof metadata was observed with a downgraded or unknown status.",
        };

    private static DateTime? TryParseUtc(string? value) =>
        DateTime.TryParse(
            value,
            provider: null,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    private static string NormalizeRequired(string? value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

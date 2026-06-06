using System.Reflection;
using System.Security.Cryptography;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IActiveDeploymentProofProvider
{
    Task<ActiveDeploymentProofContext> GetActiveDeploymentProofContextAsync(
        ElectionDeploymentProofProfile profile,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveDeploymentProofEvent>> GetDeploymentEventsSinceAsync(
        ElectionDeploymentProofProfile profile,
        DateTime sinceUtc,
        DateTime untilUtc,
        CancellationToken cancellationToken = default);

    Task<ActiveProofFamilyStatus> ResolveProofFamilyStatusAsync(
        string proofFamilyId,
        string? activeServerProofId,
        CancellationToken cancellationToken = default);
}

public interface IElectionDeploymentProofProfilePolicy
{
    ElectionDeploymentProofProfile ResolveProfile(ElectionRecord election);

    ElectionDeploymentProofOpenPolicyResult EvaluateOpen(
        ElectionRecord election,
        ActiveDeploymentProofContext activeContext);
}

public sealed class ElectionDeploymentProofProfilePolicy(
    ElectionDeploymentProofOptions options) : IElectionDeploymentProofProfilePolicy
{
    public ElectionDeploymentProofProfile ResolveProfile(ElectionRecord election)
    {
        ArgumentNullException.ThrowIfNull(election);

        var normalizedProfileId = NormalizeProfileId(election.SelectedProfileId);
        var profileClass = ResolveProfileClass(election, normalizedProfileId);

        return new ElectionDeploymentProofProfile(
            normalizedProfileId,
            election.SelectedProfileDevOnly,
            election.BindingStatus,
            election.GovernanceMode,
            profileClass);
    }

    public ElectionDeploymentProofOpenPolicyResult EvaluateOpen(
        ElectionRecord election,
        ActiveDeploymentProofContext activeContext)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        var profile = ResolveProfile(election);
        if (profile.ProfileClass == ElectionDeploymentProofProfileClass.LocalDevelopment)
        {
            return ElectionDeploymentProofOpenPolicyResult.Allow(
                activeContext.ProviderStatus,
                ElectionDeploymentProofClaimEffect.NoClaim,
                "Local/dev election profile does not produce deployment proof readiness or pilot claims.");
        }

        if (profile.ProfileClass == ElectionDeploymentProofProfileClass.Unsupported)
        {
            return ElectionDeploymentProofOpenPolicyResult.Block(
                activeContext.ProviderStatus,
                ElectionDeploymentProofClaimEffect.Blocked,
                ["deployment_profile_unsupported"],
                "Unsupported deployment profile cannot produce claim-bearing deployment proof evidence.");
        }

        var blockingProviderCode = MapBlockingStatusCode(activeContext.ProviderStatus);
        if (blockingProviderCode is not null)
        {
            return ElectionDeploymentProofOpenPolicyResult.Block(
                activeContext.ProviderStatus,
                ElectionDeploymentProofClaimEffect.Blocked,
                [blockingProviderCode],
                "Claim-bearing elections require an accepted active server deployment proof before opening.");
        }

        if (activeContext.ServerProof is null)
        {
            return ElectionDeploymentProofOpenPolicyResult.Block(
                ElectionDeploymentProofEvidenceStatus.Missing,
                ElectionDeploymentProofClaimEffect.Blocked,
                ["deployment_proof_missing"],
                "Claim-bearing elections require an active HushServerNode deployment proof before opening.");
        }

        var blockingServerCode = MapBlockingStatusCode(activeContext.ServerProof.EvidenceStatus);
        if (blockingServerCode is not null)
        {
            return ElectionDeploymentProofOpenPolicyResult.Block(
                activeContext.ServerProof.EvidenceStatus,
                ElectionDeploymentProofClaimEffect.Blocked,
                [blockingServerCode],
                "Active HushServerNode deployment proof blocks this election from opening.");
        }

        var claimEffect = activeContext.ProviderStatus switch
        {
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations =>
                ElectionDeploymentProofClaimEffect.AcceptedWithLimitations,
            ElectionDeploymentProofEvidenceStatus.Degraded =>
                ElectionDeploymentProofClaimEffect.Downgraded,
            _ => ElectionDeploymentProofClaimEffect.Accepted,
        };

        return ElectionDeploymentProofOpenPolicyResult.Allow(
            activeContext.ProviderStatus,
            claimEffect,
            claimEffect == ElectionDeploymentProofClaimEffect.Accepted
                ? "Active deployment proof accepted for claim-bearing election open."
                : "Active deployment proof allows open with explicit deployment proof limitations.");
    }

    private ElectionDeploymentProofProfileClass ResolveProfileClass(
        ElectionRecord election,
        string normalizedProfileId)
    {
        if (options.LocalDevelopmentProfileIds.Contains(normalizedProfileId, StringComparer.OrdinalIgnoreCase) ||
            election.SelectedProfileDevOnly ||
            election.BindingStatus == ElectionBindingStatus.NonBinding)
        {
            return ElectionDeploymentProofProfileClass.LocalDevelopment;
        }

        if (options.ControlledPilotProfileIds.Contains(normalizedProfileId, StringComparer.OrdinalIgnoreCase))
        {
            return ElectionDeploymentProofProfileClass.ControlledPilot;
        }

        if (options.ProductionLikeProfileIds.Contains(normalizedProfileId, StringComparer.OrdinalIgnoreCase))
        {
            return ElectionDeploymentProofProfileClass.HushManagedProductionLike;
        }

        return ElectionDeploymentProofProfileClass.Unsupported;
    }

    private static string? MapBlockingStatusCode(ElectionDeploymentProofEvidenceStatus status) =>
        status switch
        {
            ElectionDeploymentProofEvidenceStatus.Missing or ElectionDeploymentProofEvidenceStatus.NotRequired =>
                "deployment_proof_missing",
            ElectionDeploymentProofEvidenceStatus.Stale => "deployment_proof_stale",
            ElectionDeploymentProofEvidenceStatus.Superseded => "deployment_proof_superseded",
            ElectionDeploymentProofEvidenceStatus.Blocked => "deployment_proof_blocked",
            ElectionDeploymentProofEvidenceStatus.Unknown or ElectionDeploymentProofEvidenceStatus.Mismatch =>
                "deployment_proof_unknown",
            ElectionDeploymentProofEvidenceStatus.NotYetSupported =>
                ElectionDeploymentProofConstants.Feat144WebClientProofNotSupportedCode,
            _ => null,
        };

    private static string NormalizeProfileId(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Selected profile id is required.", nameof(value))
            : value.Trim();
}

public sealed class LocalDevelopmentActiveDeploymentProofProvider : IActiveDeploymentProofProvider
{
    public Task<ActiveDeploymentProofContext> GetActiveDeploymentProofContextAsync(
        ElectionDeploymentProofProfile profile,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ActiveDeploymentProofContext(
            ElectionDeploymentProofEvidenceStatus.NotRequired,
            observedAtUtc,
            profile.ProfileId,
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            PublicCatalogRef: null,
            PlatformCeremonyId: null,
            ServerProof: null,
            ExpectedWebClientProof: null,
            ProviderErrors: Array.Empty<ActiveDeploymentProofProviderError>()));

    public Task<IReadOnlyList<ActiveDeploymentProofEvent>> GetDeploymentEventsSinceAsync(
        ElectionDeploymentProofProfile profile,
        DateTime sinceUtc,
        DateTime untilUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActiveDeploymentProofEvent>>(Array.Empty<ActiveDeploymentProofEvent>());

    public Task<ActiveProofFamilyStatus> ResolveProofFamilyStatusAsync(
        string proofFamilyId,
        string? activeServerProofId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ActiveProofFamilyStatus(
            proofFamilyId,
            ProofFamilyVersion: "v1",
            PackageId: null,
            PackageHash: null,
            PromotedRegisterRef: null,
            ElectionDeploymentProofConstants.Feat137SourceFeature,
            ElectionDeploymentProofEvidenceStatus.NotRequired,
            MismatchCode: null,
            "Local/dev provider does not produce proof-family readiness claims.",
            DateTime.UtcNow));
}

public sealed class DevelopmentRuntimeActiveDeploymentProofProvider(
    ElectionDeploymentProofProviderOptions options) : IActiveDeploymentProofProvider
{
    private const string ComponentId = "hush-server-node";

    private readonly Lazy<RuntimeProofSnapshot> _runtimeProof = new(() => BuildRuntimeProof(options));

    public Task<ActiveDeploymentProofContext> GetActiveDeploymentProofContextAsync(
        ElectionDeploymentProofProfile profile,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtimeProof = _runtimeProof.Value;

        return Task.FromResult(new ActiveDeploymentProofContext(
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations,
            observedAtUtc,
            string.IsNullOrWhiteSpace(options.DevelopmentRuntimeDeploymentTarget)
                ? ElectionDeploymentProofProviderOptions.DefaultDevelopmentRuntimeDeploymentTarget
                : options.DevelopmentRuntimeDeploymentTarget,
            ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            PublicCatalogRef: options.DevelopmentRuntimePublicPackageRef,
            PlatformCeremonyId: ElectionDeploymentProofProviderOptions.DevelopmentRuntimePlatformCeremonyId,
            ServerProof: runtimeProof.ServerProof,
            ExpectedWebClientProof: null,
            ProviderErrors: Array.Empty<ActiveDeploymentProofProviderError>()));
    }

    public Task<IReadOnlyList<ActiveDeploymentProofEvent>> GetDeploymentEventsSinceAsync(
        ElectionDeploymentProofProfile profile,
        DateTime sinceUtc,
        DateTime untilUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtimeProof = _runtimeProof.Value;
        if (runtimeProof.Event.OccurredAtUtc < sinceUtc || runtimeProof.Event.OccurredAtUtc > untilUtc)
        {
            return Task.FromResult<IReadOnlyList<ActiveDeploymentProofEvent>>(Array.Empty<ActiveDeploymentProofEvent>());
        }

        return Task.FromResult<IReadOnlyList<ActiveDeploymentProofEvent>>([runtimeProof.Event]);
    }

    public Task<ActiveProofFamilyStatus> ResolveProofFamilyStatusAsync(
        string proofFamilyId,
        string? activeServerProofId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ActiveProofFamilyStatus(
            NormalizeRequired(proofFamilyId, nameof(proofFamilyId)),
            ProofFamilyVersion: "v1",
            PackageId: "development-runtime-self-attestation",
            PackageHash: _runtimeProof.Value.ServerProof.PackageHash,
            PromotedRegisterRef: options.DevelopmentRuntimePublicPackageRef,
            ElectionDeploymentProofConstants.Feat137SourceFeature,
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations,
            MismatchCode: null,
            "Development runtime proof accepted for local rehearsal with explicit limitations.",
            DateTime.UtcNow));
    }

    private static RuntimeProofSnapshot BuildRuntimeProof(ElectionDeploymentProofProviderOptions options)
    {
        var assembly = typeof(DevelopmentRuntimeActiveDeploymentProofProvider).Assembly;
        var assemblyName = assembly.GetName();
        var assemblyVersion = assemblyName.Version?.ToString() ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var artifactHash = ComputeAssemblyHash(assembly);
        var proofId = BuildProofId(options, artifactHash);
        var sourceRef = string.IsNullOrWhiteSpace(informationalVersion)
            ? $"assembly:{assemblyName.Name}/{assemblyVersion}"
            : $"assembly:{assemblyName.Name}/{informationalVersion}";
        var publicPackageRef = string.IsNullOrWhiteSpace(options.DevelopmentRuntimePublicPackageRef)
            ? $"local-development-runtime://{ComponentId}/{proofId}"
            : options.DevelopmentRuntimePublicPackageRef;
        var serverProof = new ActiveDeploymentProofComponent(
            ElectionDeploymentProofComponentId.HushServerNode,
            proofId,
            ElectionDeploymentProofEvidenceStatus.Accepted,
            sourceRef,
            "sha256:" + artifactHash,
            artifactHash,
            publicPackageRef,
            PreviousProofId: null,
            SupersedesProofIds: Array.Empty<string>(),
            ElectionDeploymentProofObservationSource.Provider);
        var eventTime = DateTime.UtcNow;
        var deploymentEvent = new ActiveDeploymentProofEvent(
            $"DPE-DEV-RUNTIME-{artifactHash[..12].ToUpperInvariant()}",
            "development-runtime-self-attestation",
            DeploymentRunId: proofId,
            ElectionDeploymentProofComponentId.HushServerNode,
            BeforeProofId: null,
            AfterProofId: proofId,
            ElectionDeploymentProofImpactClassification.OperationalConfigChange,
            "Current HushServerNode development runtime hash captured for local rehearsal.",
            ["runtime-assembly-hash"],
            "accepted_with_limitations",
            "development-runtime-local-operator",
            eventTime,
            ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations);

        return new RuntimeProofSnapshot(serverProof, deploymentEvent);
    }

    private static string BuildProofId(ElectionDeploymentProofProviderOptions options, string artifactHash)
    {
        var prefix = string.IsNullOrWhiteSpace(options.DevelopmentRuntimeProofIdPrefix)
            ? ElectionDeploymentProofProviderOptions.DefaultDevelopmentRuntimeProofIdPrefix
            : options.DevelopmentRuntimeProofIdPrefix.Trim();

        return prefix + artifactHash[..12].ToUpperInvariant();
    }

    private static string ComputeAssemblyHash(Assembly assembly)
    {
        var location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assembly.FullName ?? assembly.GetName().Name ?? ComponentId)))
                .ToLowerInvariant();
        }

        using var stream = File.OpenRead(location);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string NormalizeRequired(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private sealed record RuntimeProofSnapshot(
        ActiveDeploymentProofComponent ServerProof,
        ActiveDeploymentProofEvent Event);
}

public sealed class FixtureActiveDeploymentProofProvider(
    ActiveDeploymentProofContext activeContext,
    IReadOnlyList<ActiveDeploymentProofEvent>? events = null,
    IReadOnlyList<ActiveProofFamilyStatus>? proofFamilies = null) : IActiveDeploymentProofProvider
{
    private readonly IReadOnlyList<ActiveDeploymentProofEvent> _events =
        events?.ToArray() ?? Array.Empty<ActiveDeploymentProofEvent>();

    private readonly IReadOnlyList<ActiveProofFamilyStatus> _proofFamilies =
        proofFamilies?.ToArray() ?? Array.Empty<ActiveProofFamilyStatus>();

    public Task<ActiveDeploymentProofContext> GetActiveDeploymentProofContextAsync(
        ElectionDeploymentProofProfile profile,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(activeContext with
        {
            ObservedAtUtc = observedAtUtc,
            DeploymentTarget = string.IsNullOrWhiteSpace(activeContext.DeploymentTarget)
                ? profile.ProfileId
                : activeContext.DeploymentTarget,
        });

    public Task<IReadOnlyList<ActiveDeploymentProofEvent>> GetDeploymentEventsSinceAsync(
        ElectionDeploymentProofProfile profile,
        DateTime sinceUtc,
        DateTime untilUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActiveDeploymentProofEvent>>(
            _events
                .Where(x => x.OccurredAtUtc >= sinceUtc && x.OccurredAtUtc <= untilUtc)
                .OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.EventPublicId)
                .ToArray());

    public Task<ActiveProofFamilyStatus> ResolveProofFamilyStatusAsync(
        string proofFamilyId,
        string? activeServerProofId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProofFamilyId = NormalizeRequired(proofFamilyId, nameof(proofFamilyId));
        var status = _proofFamilies.FirstOrDefault(x =>
            string.Equals(x.ProofFamilyId, normalizedProofFamilyId, StringComparison.Ordinal));

        return Task.FromResult(status ?? new ActiveProofFamilyStatus(
            normalizedProofFamilyId,
            ProofFamilyVersion: "v1",
            PackageId: null,
            PackageHash: null,
            PromotedRegisterRef: null,
            ElectionDeploymentProofConstants.Feat137SourceFeature,
            ElectionDeploymentProofEvidenceStatus.Missing,
            MismatchCode: "privacy_proof_missing",
            "Proof-family status is missing from the fixture provider.",
            DateTime.UtcNow));
    }

    private static string NormalizeRequired(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();
}

public sealed record ElectionDeploymentProofOptions(
    IReadOnlyList<string> ProductionLikeProfileIds,
    IReadOnlyList<string> ControlledPilotProfileIds,
    IReadOnlyList<string> LocalDevelopmentProfileIds)
{
    public static ElectionDeploymentProofOptions Default =>
        new(
            [
                ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
                "organizational_remote_voting_trustee_threshold_v1",
                "organizational_remote_voting_admin_only_v1",
            ],
            ControlledPilotProfileIds: Array.Empty<string>(),
            [
                ElectionSelectableProfileCatalog.TrusteeDevProfileId,
                ElectionSelectableProfileCatalog.AdminOnlyDevProfileId,
            ]);

    public IReadOnlyList<string> ProductionLikeProfileIds { get; init; } =
        NormalizeProfileIds(ProductionLikeProfileIds);

    public IReadOnlyList<string> ControlledPilotProfileIds { get; init; } =
        NormalizeProfileIds(ControlledPilotProfileIds);

    public IReadOnlyList<string> LocalDevelopmentProfileIds { get; init; } =
        NormalizeProfileIds(LocalDevelopmentProfileIds);

    private static IReadOnlyList<string> NormalizeProfileIds(IReadOnlyList<string>? profileIds) =>
        profileIds is null
            ? Array.Empty<string>()
            : profileIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}

public sealed record ElectionDeploymentProofProviderOptions(
    string Provider,
    string DevelopmentRuntimeDeploymentTarget,
    string? DevelopmentRuntimePublicPackageRef,
    string DevelopmentRuntimeProofIdPrefix)
{
    public const string ProviderLocalDevelopment = "local_development";
    public const string ProviderDevelopmentRuntime = "development_runtime";
    public const string DefaultDevelopmentRuntimeDeploymentTarget = "local-development-runtime";
    public const string DefaultDevelopmentRuntimeProofIdPrefix = "DPP-SERVER-DEV-";
    public const string DevelopmentRuntimePlatformCeremonyId = "development-runtime-self-attestation-v1";

    public static ElectionDeploymentProofProviderOptions Default =>
        new(
            ProviderLocalDevelopment,
            DefaultDevelopmentRuntimeDeploymentTarget,
            DevelopmentRuntimePublicPackageRef: null,
            DefaultDevelopmentRuntimeProofIdPrefix);

    public string Provider { get; init; } =
        string.IsNullOrWhiteSpace(Provider)
            ? ProviderLocalDevelopment
            : Provider.Trim();

    public string DevelopmentRuntimeDeploymentTarget { get; init; } =
        string.IsNullOrWhiteSpace(DevelopmentRuntimeDeploymentTarget)
            ? DefaultDevelopmentRuntimeDeploymentTarget
            : DevelopmentRuntimeDeploymentTarget.Trim();

    public string? DevelopmentRuntimePublicPackageRef { get; init; } =
        string.IsNullOrWhiteSpace(DevelopmentRuntimePublicPackageRef)
            ? null
            : DevelopmentRuntimePublicPackageRef.Trim();

    public string DevelopmentRuntimeProofIdPrefix { get; init; } =
        string.IsNullOrWhiteSpace(DevelopmentRuntimeProofIdPrefix)
            ? DefaultDevelopmentRuntimeProofIdPrefix
            : DevelopmentRuntimeProofIdPrefix.Trim();
}

public sealed record ElectionDeploymentProofProfile(
    string ProfileId,
    bool IsDevOnly,
    ElectionBindingStatus BindingStatus,
    ElectionGovernanceMode GovernanceMode,
    ElectionDeploymentProofProfileClass ProfileClass);

public enum ElectionDeploymentProofProfileClass
{
    HushManagedProductionLike = 0,
    ControlledPilot = 1,
    LocalDevelopment = 2,
    Unsupported = 3,
}

public sealed record ElectionDeploymentProofOpenPolicyResult(
    bool IsOpenAllowed,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    ElectionDeploymentProofClaimEffect ClaimEffect,
    IReadOnlyList<string> FailureCodes,
    string PublicSummary)
{
    public bool BlocksReadinessClaims =>
        !IsOpenAllowed ||
        ClaimEffect is
            ElectionDeploymentProofClaimEffect.Blocked or
            ElectionDeploymentProofClaimEffect.NoClaim or
            ElectionDeploymentProofClaimEffect.NotApplicable;

    public static ElectionDeploymentProofOpenPolicyResult Allow(
        ElectionDeploymentProofEvidenceStatus evidenceStatus,
        ElectionDeploymentProofClaimEffect claimEffect,
        string publicSummary) =>
        new(
            IsOpenAllowed: true,
            evidenceStatus,
            claimEffect,
            Array.Empty<string>(),
            NormalizeRequired(publicSummary, nameof(publicSummary)));

    public static ElectionDeploymentProofOpenPolicyResult Block(
        ElectionDeploymentProofEvidenceStatus evidenceStatus,
        ElectionDeploymentProofClaimEffect claimEffect,
        IReadOnlyList<string> failureCodes,
        string publicSummary) =>
        new(
            IsOpenAllowed: false,
            evidenceStatus,
            claimEffect,
            NormalizeFailureCodes(failureCodes),
            NormalizeRequired(publicSummary, nameof(publicSummary)));

    private static IReadOnlyList<string> NormalizeFailureCodes(IReadOnlyList<string>? failureCodes) =>
        failureCodes is null
            ? Array.Empty<string>()
            : failureCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private static string NormalizeRequired(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();
}

public sealed record ActiveDeploymentProofContext(
    ElectionDeploymentProofEvidenceStatus ProviderStatus,
    DateTime ObservedAtUtc,
    string DeploymentTarget,
    string DeploymentProtocolVersion,
    string? PublicCatalogRef,
    string? PlatformCeremonyId,
    ActiveDeploymentProofComponent? ServerProof,
    ActiveDeploymentProofComponent? ExpectedWebClientProof,
    IReadOnlyList<ActiveDeploymentProofProviderError> ProviderErrors)
{
    public IReadOnlyList<ActiveDeploymentProofProviderError> ProviderErrors { get; init; } =
        ProviderErrors?.ToArray() ?? Array.Empty<ActiveDeploymentProofProviderError>();

    public ActiveDeploymentProofComponent? ObservedWebClientProof { get; init; }
}

public sealed record ActiveDeploymentProofComponent(
    ElectionDeploymentProofComponentId ComponentId,
    string DeploymentProofId,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    string SourceRef,
    string ArtifactHash,
    string? PackageHash,
    string? PublicPackageRef,
    string? PreviousProofId,
    IReadOnlyList<string> SupersedesProofIds,
    ElectionDeploymentProofObservationSource ObservationSource)
{
    public IReadOnlyList<string> SupersedesProofIds { get; init; } =
        SupersedesProofIds?.ToArray() ?? Array.Empty<string>();
}

public sealed record ActiveDeploymentProofEvent(
    string EventPublicId,
    string EventType,
    string? DeploymentRunId,
    ElectionDeploymentProofComponentId ComponentId,
    string? BeforeProofId,
    string? AfterProofId,
    ElectionDeploymentProofImpactClassification Classification,
    string? Reason,
    IReadOnlyList<string> ChecksRerun,
    string? CheckResult,
    string? AccountabilityMarker,
    DateTime OccurredAtUtc,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus)
{
    public IReadOnlyList<string> ChecksRerun { get; init; } =
        ChecksRerun?.ToArray() ?? Array.Empty<string>();
}

public sealed record ActiveProofFamilyStatus(
    string ProofFamilyId,
    string ProofFamilyVersion,
    string? PackageId,
    string? PackageHash,
    string? PromotedRegisterRef,
    string SourceFeature,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    string? MismatchCode,
    string PublicSummary,
    DateTime ObservedAtUtc);

public sealed record ActiveDeploymentProofProviderError(
    string Code,
    string PublicMessage);

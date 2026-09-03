using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Internal entitlement application-service facade over the transaction coordinator. This is the
/// surface later features compose: FEAT-015 wraps it behind authenticated APIs and FEAT-018 reads
/// authoritative entitlements/revisions from its results. It never exposes persistence entities and
/// never accepts a raw client subject id; persistence/transaction semantics live only in the
/// coordinator. Phase 6 registers an instance with a fresh <c>DbContext</c> factory per attempt.
/// </summary>
public sealed class LicenceEntitlementService
{
    private readonly Func<DbContext> _contextFactory;
    private readonly LicenceServiceConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly LicenceTelemetry? _telemetry;

    public LicenceEntitlementService(
        Func<DbContext> contextFactory,
        LicenceServiceConfiguration configuration,
        TimeProvider? timeProvider = null,
        LicenceTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(configuration);

        _contextFactory = contextFactory;
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _telemetry = telemetry;
    }

    /// <summary>Resolves or atomically provisions the effective entitlement (see FeatureDescription GetOrProvision).</summary>
    public Task<LicenceResolutionResult> GetOrProvisionAsync(
        AuthenticatedIdentitySubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
            _contextFactory,
            _configuration,
            subject,
            _timeProvider,
            _telemetry,
            cancellationToken);
    }

    /// <summary>Activates a higher Veritas plan behind the server-only boundary (durable and idempotent).</summary>
    public Task<LicenceActivationResult> ActivateHigherPlanAsync(
        AuthenticatedIdentitySubject subject,
        LicenceActivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(command);
        return LicenceEntitlementCoordinator.ActivateHigherPlanAsync(
            _contextFactory,
            _configuration,
            subject,
            command,
            _timeProvider,
            _telemetry,
            cancellationToken);
    }
}

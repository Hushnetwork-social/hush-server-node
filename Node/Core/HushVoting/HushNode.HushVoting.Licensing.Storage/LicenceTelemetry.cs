using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Privacy-safe structured telemetry for the licensing service. Counters and durations use bounded,
/// closed-vocabulary labels (operation + outcome/retry) and never carry a subject address,
/// assignment id, plan value, operation id, or any other high-cardinality identity value. Logs use
/// stable codes and the internal subject identifier (UUID) only.
/// </summary>
public sealed class LicenceTelemetry : IDisposable
{
    private readonly Meter _meter;
    private readonly ILogger? _logger;

    private readonly Counter<long> _resolutionOutcomes;
    private readonly Counter<long> _activationOutcomes;
    private readonly Counter<long> _expiryNormalized;
    private readonly Counter<long> _transientRetries;
    private readonly Counter<long> _concurrencyConflicts;
    private readonly Counter<long> _storageUnavailable;
    private readonly Counter<long> _persistenceInvariantViolations;
    private readonly Counter<long> _catalogueIncompatible;
    private readonly Histogram<double> _operationDuration;

    public LicenceTelemetry(ILogger? logger = null)
        : this("HushNode.HushVoting.Licensing", "1.0.0", logger)
    {
    }

    public LicenceTelemetry(string meterName, string meterVersion, ILogger? logger = null)
    {
        _logger = logger;
        _meter = new Meter(meterName, meterVersion);

        _resolutionOutcomes = _meter.CreateCounter<long>(
            "hushvoting.licence.resolution.outcomes",
            description: "Count of GetOrProvision outcomes (bounded outcome label).");

        _activationOutcomes = _meter.CreateCounter<long>(
            "hushvoting.licence.activation.outcomes",
            description: "Count of activation outcomes (bounded outcome label).");

        _expiryNormalized = _meter.CreateCounter<long>(
            "hushvoting.licence.expiry.normalized",
            description: "Count of expiry-normalization state changes (no identity labels).");

        _transientRetries = _meter.CreateCounter<long>(
            "hushvoting.licence.transient.retries",
            description: "Recognized transient-race retries by operation.");

        _concurrencyConflicts = _meter.CreateCounter<long>(
            "hushvoting.licence.concurrency.conflicts",
            description: "Recognized concurrency conflicts by operation.");

        _storageUnavailable = _meter.CreateCounter<long>(
            "hushvoting.licence.storage.unavailable",
            description: "Storage-unavailable outcomes by operation.");

        _persistenceInvariantViolations = _meter.CreateCounter<long>(
            "hushvoting.licence.persistence.invariant.violations",
            description: "Persistence-invariant violations by operation.");

        _catalogueIncompatible = _meter.CreateCounter<long>(
            "hushvoting.licence.catalogue.incompatible",
            description: "Catalogue-incompatible readiness failures (wired by the Phase 6 readiness bootstrapper).");

        _operationDuration = _meter.CreateHistogram<double>(
            "hushvoting.licence.operation.duration",
            unit: "milliseconds",
            description: "Entitlement operation duration by operation and outcome (bounded labels).");
    }

    public void RecordResolutionOutcome(string outcomeWireName, long count = 1) =>
        _resolutionOutcomes.Add(count, KeyValue("outcome", outcomeWireName));

    public void RecordActivationOutcome(string outcomeWireName, long count = 1) =>
        _activationOutcomes.Add(count, KeyValue("outcome", outcomeWireName));

    public void RecordExpiryNormalized() => _expiryNormalized.Add(1);

    public void RecordTransientRetry(string operationName) =>
        _transientRetries.Add(1, KeyValue("operation", operationName));

    public void RecordConcurrencyConflict(string operationName) =>
        _concurrencyConflicts.Add(1, KeyValue("operation", operationName));

    public void RecordStorageUnavailable(string operationName) =>
        _storageUnavailable.Add(1, KeyValue("operation", operationName));

    public void RecordPersistenceInvariantViolation(string operationName) =>
        _persistenceInvariantViolations.Add(1, KeyValue("operation", operationName));

    public void RecordCatalogueIncompatible() => _catalogueIncompatible.Add(1);

    public void RecordOperationDuration(string operationName, string outcomeWireName, TimeSpan duration) =>
        _operationDuration.Record(
            duration.TotalMilliseconds,
            KeyValue("operation", operationName),
            KeyValue("outcome", outcomeWireName));

    // ---- Privacy-safe structured logs (stable codes + internal subject identifier only) ----

    public void LogOperationCompleted(string operationName, Guid subjectId, string outcomeWireName)
    {
        _logger?.LogInformation(
            "FEAT-013 licence operation completed. Operation: {Operation}, SubjectId: {SubjectId}, Outcome: {Outcome}",
            operationName,
            subjectId,
            outcomeWireName);
    }

    public void LogAuthorityUnavailable(string operationName, string stableCode)
    {
        _logger?.LogWarning(
            "FEAT-013 authority unavailable; no entitlement invented. Operation: {Operation}, StableCode: {StableCode}",
            operationName,
            stableCode);
    }

    private static KeyValuePair<string, object?> KeyValue(string key, object value) =>
        new(key, value);

    public void Dispose()
    {
        _meter.Dispose();
    }
}

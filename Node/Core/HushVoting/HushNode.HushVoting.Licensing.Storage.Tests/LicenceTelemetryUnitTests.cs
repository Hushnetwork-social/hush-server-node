using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// FEAT-013 Task 3.8 unit coverage for privacy-safe telemetry: counters/histograms carry only
/// bounded closed-vocabulary labels and record the expected outcome/retry/expiry events.
/// </summary>
public sealed class LicenceTelemetryUnitTests
{
    private sealed record InstrumentSample(long LongValue, double DoubleValue, string[] Tags);

    [Fact]
    public void Telemetry_records_bounded_label_counts_for_outcomes_retries_and_durations()
    {
        var meterName = $"HushNode.HushVoting.Licensing.Tests.{Guid.NewGuid():N}";
        var samples = new ConcurrentDictionary<string, InstrumentSample>();
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            samples[instrument.Name] = new InstrumentSample(value, 0, Tags(tags)));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            samples[instrument.Name] = new InstrumentSample(0, value, Tags(tags)));
        listener.Start();

        using var telemetry = new LicenceTelemetry(meterName, "1.0.0", logger: null);

        telemetry.RecordResolutionOutcome("provisioned_default");
        telemetry.RecordResolutionOutcome("provisioned_default");
        telemetry.RecordResolutionOutcome("expired_to_default");
        telemetry.RecordActivationOutcome("activated");
        telemetry.RecordActivationOutcome("idempotency_payload_mismatch");
        telemetry.RecordExpiryNormalized();
        telemetry.RecordTransientRetry("resolve");
        telemetry.RecordConcurrencyConflict("activate");
        telemetry.RecordStorageUnavailable("resolve");
        telemetry.RecordPersistenceInvariantViolation("activate");
        telemetry.RecordOperationDuration("resolve", "resolved_existing", TimeSpan.FromMilliseconds(12));

        // RecordExpiryNormalized and the retry/conflict counters are distinct instruments.
        samples.Should().ContainKey("hushvoting.licence.resolution.outcomes");
        samples.Should().ContainKey("hushvoting.licence.activation.outcomes");
        samples.Should().ContainKey("hushvoting.licence.expiry.normalized");
        samples.Should().ContainKey("hushvoting.licence.transient.retries");
        samples.Should().ContainKey("hushvoting.licence.concurrency.conflicts");
        samples.Should().ContainKey("hushvoting.licence.storage.unavailable");
        samples.Should().ContainKey("hushvoting.licence.persistence.invariant.violations");
        samples.Should().ContainKey("hushvoting.licence.operation.duration");

        // The samples dictionary holds the last observed sample per instrument; counters also
        // aggregate, so at minimum the last recorded label/value is visible. Outcome labels are
        // closed-vocabulary (never identity values).
        samples["hushvoting.licence.resolution.outcomes"].LongValue.Should().Be(1);
        samples["hushvoting.licence.resolution.outcomes"].Tags.Should().Contain("outcome=expired_to_default");
        samples["hushvoting.licence.activation.outcomes"].Tags.Should().Contain("outcome=idempotency_payload_mismatch");
        samples["hushvoting.licence.operation.duration"].Tags.Should().Contain("operation=resolve");
        samples["hushvoting.licence.operation.duration"].Tags.Should().Contain("outcome=resolved_existing");
        samples["hushvoting.licence.operation.duration"].DoubleValue.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Outcome_wire_names_provide_bounded_label_vocabulary()
    {
        var resolutionLabels = Enum.GetValues<LicenceResolutionOutcome>()
            .Select(LicenceEntitlementOutcomeNames.ToWireName)
            .ToArray();
        var activationLabels = Enum.GetValues<LicenceActivationOutcome>()
            .Select(LicenceEntitlementOutcomeNames.ToWireName)
            .ToArray();

        resolutionLabels.Should().OnlyHaveUniqueItems();
        activationLabels.Should().OnlyHaveUniqueItems();
        resolutionLabels.Should().NotContain(s => s.Contains('{') || s.Contains('}') || s.Contains('='));
        activationLabels.Should().NotContain(s => s.Contains('{') || s.Contains('}') || s.Contains('='));
        resolutionLabels.Should().HaveCount(Enum.GetValues<LicenceResolutionOutcome>().Length);
        activationLabels.Should().HaveCount(Enum.GetValues<LicenceActivationOutcome>().Length);
    }

    private static string[] Tags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var list = new List<string>(tags.Length);
        foreach (var tag in tags)
        {
            list.Add($"{tag.Key}={tag.Value}");
        }

        return list.ToArray();
    }
}

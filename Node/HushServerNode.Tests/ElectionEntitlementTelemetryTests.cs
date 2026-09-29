using System.Diagnostics.Metrics;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests;

public sealed class ElectionEntitlementTelemetryTests
{
    [Fact]
    public void UnknownInputCannotBecomeAMetricLabel_AndKnownRejectionKeepsItsReason()
    {
        using var telemetry = new ElectionEntitlementTelemetry();
        var observed = new List<Dictionary<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, source) =>
        {
            if (instrument.Meter.Name == ElectionEntitlementTelemetry.MeterName) source.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            value.Should().Be(1);
            observed.Add(tags.ToArray().ToDictionary(t => t.Key, t => t.Value));
        });
        listener.Start();
        telemetry.Record("untrusted actor/election text", ElectionEntitlementResults.Reject((ElectionEntitlementReason)999));
        telemetry.Record(EncryptedElectionEnvelopeActionTypes.OpenElection, ElectionEntitlementResults.Reject(ElectionEntitlementReason.NotActive));
        observed.Should().HaveCount(2);
        observed[0].Keys.Should().BeEquivalentTo("operation", "reason", "policy", "outcome");
        observed[0]["operation"].Should().Be("other");
        observed[0]["reason"].Should().Be("ENTITLEMENT_SEMANTICS_UNSUPPORTED");
        observed[1]["operation"].Should().Be("open");
        observed[1]["reason"].Should().Be("ENTITLEMENT_NOT_ACTIVE");
        observed[1]["policy"].Should().Be(ElectionEntitlementCapture.CurrentPolicyVersion);
        observed[1]["outcome"].Should().Be("failure");
    }
}

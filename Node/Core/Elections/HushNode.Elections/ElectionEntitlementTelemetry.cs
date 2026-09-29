using System.Diagnostics.Metrics;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionEntitlementTelemetry
{
    void Record(string action, ElectionCommandResult result);
}

/// <summary>Closed, low-cardinality outcomes. Never accepts identity, election, ballot,
/// payload, exception text, or caller-supplied metric labels.</summary>
public sealed class ElectionEntitlementTelemetry : IElectionEntitlementTelemetry, IDisposable
{
    public const string MeterName = "HushVoting.ElectionEntitlement";
    private readonly Meter _meter = new(MeterName, "1");
    private readonly Counter<long> _outcomes;
    public ElectionEntitlementTelemetry() => _outcomes = _meter.CreateCounter<long>("election.operation.outcomes");
    public void Record(string action, ElectionCommandResult result)
    {
        var operation = action switch
        {
            EncryptedElectionEnvelopeActionTypes.CreateDraft => "create",
            EncryptedElectionEnvelopeActionTypes.UpdateDraft => "update",
            EncryptedElectionEnvelopeActionTypes.ImportRoster => "roster",
            EncryptedElectionEnvelopeActionTypes.OpenElection => "open",
            EncryptedElectionEnvelopeActionTypes.CloseElection => "close",
            EncryptedElectionEnvelopeActionTypes.FinalizeElection => "finalize",
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast => "ballot",
            EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal => "approve",
            EncryptedElectionEnvelopeActionTypes.RetryGovernedProposalExecution => "retry",
            EncryptedElectionEnvelopeActionTypes.StartGovernedProposal => "proposal",
            _ => "other",
        };
        _outcomes.Add(1, new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("reason", ElectionEntitlementReasonNames.ToWire(result.EntitlementReason)),
            new KeyValuePair<string, object?>("policy", ElectionEntitlementCapture.CurrentPolicyVersion),
            new KeyValuePair<string, object?>("outcome", result.IsSuccess ? "success" : "failure"));
    }
    public void Dispose() => _meter.Dispose();
}

// FEAT-015 Task 3.1 — canonical licence serializer.
//
// Composes the frozen payload/transaction canonical JSON with the recorded outer values
// and recomputes the exact payload size. This is the single canonical serializer for the
// licence payload kind; the Phase 2.4 fixture writer and this class must agree byte-for-byte
// (proven by fixture-parity tests in Task 3.2).

using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class HushVotingLicenceCanonicalSerializer : IHushVotingLicenceCanonicalSerializer
{
    public string SerializeCanonicalUnsignedJson(
        SignedTransaction<HushVotingLicenceAssignmentPayload> transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(transaction.Payload);

        return HushVotingLicenceCanonicalJson.BuildCanonicalUnsignedJson(
            transaction.TransactionId.Value,
            transaction.PayloadKind,
            transaction.TransactionTimeStamp.Value,
            transaction.Payload,
            transaction.PayloadSize);
    }

    public int PayloadJsonUtf8Length(HushVotingLicenceAssignmentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload);
    }
}

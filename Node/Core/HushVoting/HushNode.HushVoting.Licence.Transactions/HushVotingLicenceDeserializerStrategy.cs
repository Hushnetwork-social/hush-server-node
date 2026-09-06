// FEAT-015 Task 6.3 — licence transaction deserializer strategy.
//
// Registered so the existing AbstractTransactionConverter can materialize the licence
// payload kind from signed/validated JSON. Exact retry and replay reuse the same bytes.

using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class HushVotingLicenceDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<HushVotingLicenceAssignmentPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<HushVotingLicenceAssignmentPayload>>(transactionJSON)!;
}

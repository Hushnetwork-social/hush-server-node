namespace HushShared.Blockchain.TransactionModel;

public sealed record TransactionValidationFailure(
    string Code,
    string Message);

public interface ITransactionValidationFailureReporter
{
    bool TryTakeValidationFailure(Guid transactionId, out TransactionValidationFailure failure);
}

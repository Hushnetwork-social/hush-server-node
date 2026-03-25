namespace HushServerNode.Testing.Elections;

internal sealed record ControlledElectionValidationResult(
    bool IsValid,
    string? FailureCode,
    string Notes)
{
    public static ControlledElectionValidationResult Success(string notes) =>
        new(true, null, notes);

    public static ControlledElectionValidationResult Failure(string failureCode, string notes) =>
        new(false, failureCode, notes);
}

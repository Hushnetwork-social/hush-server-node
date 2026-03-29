namespace HushShared.Elections.Model;

public record ElectionResultArtifactRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionResultArtifactKind ArtifactKind,
    ElectionResultArtifactVisibility Visibility,
    string Title,
    IReadOnlyList<ElectionResultOptionCount> NamedOptionResults,
    int BlankCount,
    int TotalVotedCount,
    int EligibleToVoteCount,
    int DidNotVoteCount,
    ElectionResultDenominatorEvidence DenominatorEvidence,
    Guid? TallyReadyArtifactId,
    Guid? SourceResultArtifactId,
    string? EncryptedPayload,
    string? PublicPayload,
    DateTime RecordedAt,
    string RecordedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string Title { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            Title,
            nameof(Title));

    public string RecordedByPublicAddress { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RecordedByPublicAddress,
            nameof(RecordedByPublicAddress));

    public string? EncryptedPayload { get; init; } =
        NormalizeOptionalValue(EncryptedPayload);

    public string? PublicPayload { get; init; } =
        NormalizeOptionalValue(PublicPayload);

    public bool IsParticipantEncrypted =>
        Visibility == ElectionResultArtifactVisibility.ParticipantEncrypted;

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

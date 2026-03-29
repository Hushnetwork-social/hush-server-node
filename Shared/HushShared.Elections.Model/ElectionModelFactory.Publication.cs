namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionBallotMemPoolRecord CreateBallotMemPoolEntry(
        ElectionId electionId,
        Guid acceptedBallotId,
        DateTime? queuedAt = null) =>
        new(
            Guid.NewGuid(),
            electionId,
            acceptedBallotId,
            queuedAt ?? DateTime.UtcNow);

    public static ElectionPublishedBallotRecord CreatePublishedBallotRecord(
        ElectionId electionId,
        long publicationSequence,
        string encryptedBallotPackage,
        string proofBundle,
        DateTime? publishedAt = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (publicationSequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(publicationSequence), publicationSequence, "Publication sequence must be at least 1.");
        }

        return new ElectionPublishedBallotRecord(
            Guid.NewGuid(),
            electionId,
            publicationSequence,
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(encryptedBallotPackage, nameof(encryptedBallotPackage)),
            ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(proofBundle, nameof(proofBundle)),
            publishedAt ?? DateTime.UtcNow,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionPublicationIssueRecord CreatePublicationIssue(
        ElectionId electionId,
        ElectionPublicationIssueCode issueCode,
        DateTime? observedAt = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        var timestamp = observedAt ?? DateTime.UtcNow;
        return new ElectionPublicationIssueRecord(
            Guid.NewGuid(),
            electionId,
            issueCode,
            OccurrenceCount: 1,
            FirstObservedAt: timestamp,
            LastObservedAt: timestamp,
            LatestBlockHeight: latestBlockHeight,
            LatestBlockId: latestBlockId);
    }
}

namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionResultArtifactRecord CreateResultArtifact(
        ElectionId electionId,
        ElectionResultArtifactKind artifactKind,
        ElectionResultArtifactVisibility visibility,
        string title,
        IReadOnlyList<ElectionResultOptionCount> namedOptionResults,
        int blankCount,
        int totalVotedCount,
        int eligibleToVoteCount,
        int didNotVoteCount,
        ElectionResultDenominatorEvidence denominatorEvidence,
        string recordedByPublicAddress,
        Guid? tallyReadyArtifactId = null,
        Guid? sourceResultArtifactId = null,
        string? encryptedPayload = null,
        string? publicPayload = null,
        DateTime? recordedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (namedOptionResults is null)
        {
            throw new ArgumentNullException(nameof(namedOptionResults));
        }

        var normalizedResults = namedOptionResults
            .Select(x => new ElectionResultOptionCount(
                NormalizeRequiredText(x.OptionId, nameof(namedOptionResults)),
                NormalizeRequiredText(x.DisplayLabel, nameof(namedOptionResults)),
                NormalizeOptionalText(x.ShortDescription),
                x.BallotOrder,
                x.Rank,
                x.VoteCount))
            .ToArray();

        if (blankCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blankCount), "Blank count cannot be negative.");
        }

        if (totalVotedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalVotedCount), "Total voted count cannot be negative.");
        }

        if (eligibleToVoteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eligibleToVoteCount), "Eligible-to-vote count cannot be negative.");
        }

        if (didNotVoteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(didNotVoteCount), "Did-not-vote count cannot be negative.");
        }

        return new ElectionResultArtifactRecord(
            Guid.NewGuid(),
            electionId,
            artifactKind,
            visibility,
            NormalizeRequiredText(title, nameof(title)),
            normalizedResults,
            blankCount,
            totalVotedCount,
            eligibleToVoteCount,
            didNotVoteCount,
            CloneDenominatorEvidence(denominatorEvidence),
            tallyReadyArtifactId,
            sourceResultArtifactId,
            NormalizeOptionalText(encryptedPayload),
            NormalizeOptionalText(publicPayload),
            recordedAt ?? DateTime.UtcNow,
            NormalizeRequiredText(recordedByPublicAddress, nameof(recordedByPublicAddress)),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    private static ElectionResultDenominatorEvidence CloneDenominatorEvidence(
        ElectionResultDenominatorEvidence evidence) =>
        new(
            evidence.SnapshotType,
            evidence.EligibilitySnapshotId,
            evidence.BoundaryArtifactId,
            CloneBytes(evidence.ActiveDenominatorSetHash) ?? Array.Empty<byte>());
}

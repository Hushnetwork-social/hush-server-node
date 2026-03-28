namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionRosterEntryRecord CreateRosterEntry(
        ElectionId electionId,
        string organizationVoterId,
        ElectionRosterContactType contactType,
        string contactValue,
        ElectionVotingRightStatus votingRightStatus = ElectionVotingRightStatus.Active,
        DateTime? importedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var timestamp = importedAt ?? DateTime.UtcNow;

        return new ElectionRosterEntryRecord(
            electionId,
            NormalizeRequiredText(organizationVoterId, nameof(organizationVoterId)),
            contactType,
            NormalizeRequiredText(contactValue, nameof(contactValue)),
            ElectionVoterLinkStatus.Unlinked,
            LinkedActorPublicAddress: null,
            LinkedAt: null,
            votingRightStatus,
            ImportedAt: timestamp,
            WasPresentAtOpen: false,
            WasActiveAtOpen: false,
            LastActivatedAt: null,
            LastActivatedByPublicAddress: null,
            LastUpdatedAt: timestamp,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionEligibilityActivationEventRecord CreateEligibilityActivationEvent(
        ElectionId electionId,
        string organizationVoterId,
        string attemptedByPublicAddress,
        ElectionEligibilityActivationOutcome outcome,
        ElectionEligibilityActivationBlockReason blockReason = ElectionEligibilityActivationBlockReason.None,
        DateTime? occurredAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (outcome == ElectionEligibilityActivationOutcome.Activated &&
            blockReason != ElectionEligibilityActivationBlockReason.None)
        {
            throw new ArgumentException("Successful activation events cannot include a block reason.", nameof(blockReason));
        }

        if (outcome == ElectionEligibilityActivationOutcome.Blocked &&
            blockReason == ElectionEligibilityActivationBlockReason.None)
        {
            throw new ArgumentException("Blocked activation events require a block reason.", nameof(blockReason));
        }

        return new ElectionEligibilityActivationEventRecord(
            Guid.NewGuid(),
            electionId,
            NormalizeRequiredText(organizationVoterId, nameof(organizationVoterId)),
            NormalizeRequiredText(attemptedByPublicAddress, nameof(attemptedByPublicAddress)),
            outcome,
            blockReason,
            occurredAt ?? DateTime.UtcNow,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionParticipationRecord CreateParticipationRecord(
        ElectionId electionId,
        string organizationVoterId,
        ElectionParticipationStatus participationStatus,
        DateTime? recordedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var timestamp = recordedAt ?? DateTime.UtcNow;

        return new ElectionParticipationRecord(
            electionId,
            NormalizeRequiredText(organizationVoterId, nameof(organizationVoterId)),
            participationStatus,
            RecordedAt: timestamp,
            LastUpdatedAt: timestamp,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    public static ElectionEligibilitySnapshotRecord CreateEligibilitySnapshot(
        ElectionId electionId,
        ElectionEligibilitySnapshotType snapshotType,
        EligibilityMutationPolicy eligibilityMutationPolicy,
        int rosteredCount,
        int linkedCount,
        int activeDenominatorCount,
        int countedParticipationCount,
        int blankCount,
        int didNotVoteCount,
        byte[] rosteredVoterSetHash,
        byte[] activeDenominatorSetHash,
        byte[] countedParticipationSetHash,
        string recordedByPublicAddress,
        Guid? boundaryArtifactId = null,
        DateTime? recordedAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        EnsureSnapshotCount(rosteredCount, nameof(rosteredCount));
        EnsureSnapshotCount(linkedCount, nameof(linkedCount));
        EnsureSnapshotCount(activeDenominatorCount, nameof(activeDenominatorCount));
        EnsureSnapshotCount(countedParticipationCount, nameof(countedParticipationCount));
        EnsureSnapshotCount(blankCount, nameof(blankCount));
        EnsureSnapshotCount(didNotVoteCount, nameof(didNotVoteCount));

        if (linkedCount > rosteredCount)
        {
            throw new ArgumentException("Linked count cannot exceed rostered count.", nameof(linkedCount));
        }

        if (activeDenominatorCount > rosteredCount)
        {
            throw new ArgumentException("Active denominator count cannot exceed rostered count.", nameof(activeDenominatorCount));
        }

        if (blankCount > countedParticipationCount)
        {
            throw new ArgumentException("Blank count cannot exceed counted participation count.", nameof(blankCount));
        }

        if (countedParticipationCount > activeDenominatorCount)
        {
            throw new ArgumentException("Counted participation count cannot exceed the active denominator count.", nameof(countedParticipationCount));
        }

        if (countedParticipationCount + didNotVoteCount != activeDenominatorCount)
        {
            throw new ArgumentException("Counted participation plus did-not-vote must equal the active denominator.", nameof(didNotVoteCount));
        }

        return new ElectionEligibilitySnapshotRecord(
            Guid.NewGuid(),
            electionId,
            snapshotType,
            eligibilityMutationPolicy,
            rosteredCount,
            linkedCount,
            activeDenominatorCount,
            countedParticipationCount,
            blankCount,
            didNotVoteCount,
            CloneRequiredBytes(rosteredVoterSetHash, nameof(rosteredVoterSetHash)),
            CloneRequiredBytes(activeDenominatorSetHash, nameof(activeDenominatorSetHash)),
            CloneRequiredBytes(countedParticipationSetHash, nameof(countedParticipationSetHash)),
            boundaryArtifactId,
            recordedAt ?? DateTime.UtcNow,
            NormalizeRequiredText(recordedByPublicAddress, nameof(recordedByPublicAddress)),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    private static void EnsureSnapshotCount(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Snapshot counts cannot be negative.");
        }
    }

    private static byte[] CloneRequiredBytes(byte[] value, string paramName)
    {
        if (value is null || value.Length == 0)
        {
            throw new ArgumentException("A non-empty hash is required.", paramName);
        }

        return CloneBytes(value)!;
    }
}

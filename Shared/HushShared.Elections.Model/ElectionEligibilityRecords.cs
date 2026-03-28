namespace HushShared.Elections.Model;

public record ElectionRosterEntryRecord(
    ElectionId ElectionId,
    string OrganizationVoterId,
    ElectionRosterContactType ContactType,
    string ContactValue,
    ElectionVoterLinkStatus LinkStatus,
    string? LinkedActorPublicAddress,
    DateTime? LinkedAt,
    ElectionVotingRightStatus VotingRightStatus,
    DateTime ImportedAt,
    bool WasPresentAtOpen,
    bool WasActiveAtOpen,
    DateTime? LastActivatedAt,
    string? LastActivatedByPublicAddress,
    DateTime LastUpdatedAt,
    Guid? LatestTransactionId,
    long? LatestBlockHeight,
    Guid? LatestBlockId)
{
    public bool IsLinked =>
        LinkStatus == ElectionVoterLinkStatus.Linked &&
        !string.IsNullOrWhiteSpace(LinkedActorPublicAddress);

    public bool IsActive => VotingRightStatus == ElectionVotingRightStatus.Active;

    public ElectionRosterEntryRecord LinkToActor(
        string actorPublicAddress,
        DateTime linkedAt,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        var normalizedActor = NormalizeRequiredActor(actorPublicAddress, nameof(actorPublicAddress));
        if (IsLinked &&
            !string.Equals(LinkedActorPublicAddress, normalizedActor, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Roster entry is already linked to a different actor.");
        }

        return this with
        {
            LinkStatus = ElectionVoterLinkStatus.Linked,
            LinkedActorPublicAddress = normalizedActor,
            LinkedAt = linkedAt,
            LastUpdatedAt = linkedAt,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    public ElectionRosterEntryRecord FreezeAtOpen(
        DateTime openedAt,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null) =>
        this with
        {
            WasPresentAtOpen = true,
            WasActiveAtOpen = VotingRightStatus == ElectionVotingRightStatus.Active,
            LastUpdatedAt = openedAt,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };

    public ElectionRosterEntryRecord MarkVotingRightActive(
        string activatedByPublicAddress,
        DateTime activatedAt,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null)
    {
        if (VotingRightStatus == ElectionVotingRightStatus.Active)
        {
            throw new InvalidOperationException("Roster entry is already active.");
        }

        return this with
        {
            VotingRightStatus = ElectionVotingRightStatus.Active,
            LastActivatedAt = activatedAt,
            LastActivatedByPublicAddress = NormalizeRequiredActor(activatedByPublicAddress, nameof(activatedByPublicAddress)),
            LastUpdatedAt = activatedAt,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
    }

    private static string NormalizeRequiredActor(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Actor public address is required.", paramName);
        }

        return value.Trim();
    }
}

public record ElectionEligibilityActivationEventRecord(
    Guid Id,
    ElectionId ElectionId,
    string OrganizationVoterId,
    string AttemptedByPublicAddress,
    ElectionEligibilityActivationOutcome Outcome,
    ElectionEligibilityActivationBlockReason BlockReason,
    DateTime OccurredAt,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

public record ElectionParticipationRecord(
    ElectionId ElectionId,
    string OrganizationVoterId,
    ElectionParticipationStatus ParticipationStatus,
    DateTime RecordedAt,
    DateTime LastUpdatedAt,
    Guid? LatestTransactionId,
    long? LatestBlockHeight,
    Guid? LatestBlockId)
{
    public bool CountsAsParticipation =>
        ParticipationStatus == ElectionParticipationStatus.CountedAsVoted ||
        ParticipationStatus == ElectionParticipationStatus.Blank;

    public ElectionParticipationRecord UpdateStatus(
        ElectionParticipationStatus participationStatus,
        DateTime updatedAt,
        Guid? latestTransactionId = null,
        long? latestBlockHeight = null,
        Guid? latestBlockId = null) =>
        this with
        {
            ParticipationStatus = participationStatus,
            LastUpdatedAt = updatedAt,
            LatestTransactionId = latestTransactionId,
            LatestBlockHeight = latestBlockHeight,
            LatestBlockId = latestBlockId,
        };
}

public record ElectionEligibilitySnapshotRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionEligibilitySnapshotType SnapshotType,
    EligibilityMutationPolicy EligibilityMutationPolicy,
    int RosteredCount,
    int LinkedCount,
    int ActiveDenominatorCount,
    int CountedParticipationCount,
    int BlankCount,
    int DidNotVoteCount,
    byte[] RosteredVoterSetHash,
    byte[] ActiveDenominatorSetHash,
    byte[] CountedParticipationSetHash,
    Guid? BoundaryArtifactId,
    DateTime RecordedAt,
    string RecordedByPublicAddress,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

namespace HushShared.Elections.Model;

public enum ElectionLifecycleState
{
    Draft = 0,
    Open = 1,
    Closed = 2,
    Finalized = 3,
}

public enum ElectionClass
{
    OrganizationalRemoteVoting = 0,
    PrivatePoll = 1,
    SeriousSecretBallotVoting = 2,
}

public enum ElectionBindingStatus
{
    Binding = 0,
    NonBinding = 1,
}

public enum ElectionGovernanceMode
{
    AdminOnly = 0,
    TrusteeThreshold = 1,
}

public enum ElectionDisclosureMode
{
    FinalResultsOnly = 0,
    SeparatedParticipationAndResultReports = 1,
    SeparatedParticipationAndPlaintextBallotReports = 2,
}

public enum ParticipationPrivacyMode
{
    PublicCheckoffAnonymousBallotPrivateChoice = 0,
}

public enum VoteUpdatePolicy
{
    SingleSubmissionOnly = 0,
    LatestValidVoteWins = 1,
}

public enum EligibilitySourceType
{
    OrganizationImportedRoster = 0,
}

public enum EligibilityMutationPolicy
{
    FrozenAtOpen = 0,
    LateActivationForRosteredVotersOnly = 1,
}

public enum OutcomeRuleKind
{
    SingleWinner = 0,
    PassFail = 1,
    TopN = 2,
}

public enum ReportingPolicy
{
    DefaultPhaseOnePackage = 0,
}

public enum ReviewWindowPolicy
{
    NoReviewWindow = 0,
    GovernedReviewWindowReserved = 1,
}

public enum ElectionBoundaryArtifactType
{
    Open = 0,
    Close = 1,
    Finalize = 2,
}

public enum ElectionGovernedActionType
{
    Open = 0,
    Close = 1,
    Finalize = 2,
}

public enum ElectionGovernedProposalExecutionStatus
{
    WaitingForApprovals = 0,
    ExecutionSucceeded = 1,
    ExecutionFailed = 2,
}

public enum ElectionWarningCode
{
    LowAnonymitySet = 0,
    AllTrusteesRequiredFragility = 1,
}

public enum ElectionTrusteeInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Revoked = 3,
}

public enum ElectionCeremonyVersionStatus
{
    InProgress = 0,
    Ready = 1,
    Superseded = 2,
}

public enum ElectionCeremonyTranscriptEventType
{
    VersionStarted = 0,
    TrusteeTransportKeyPublished = 1,
    TrusteeJoined = 2,
    TrusteeSelfTestSucceeded = 3,
    TrusteeMaterialSubmitted = 4,
    TrusteeValidationFailed = 5,
    TrusteeCompleted = 6,
    TrusteeRemoved = 7,
    VersionReady = 8,
    VersionSuperseded = 9,
}

public enum ElectionTrusteeCeremonyState
{
    Invited = 0,
    AcceptedTrustee = 1,
    CeremonyNotStarted = 2,
    CeremonyJoined = 3,
    CeremonyMaterialSubmitted = 4,
    CeremonyValidationFailed = 5,
    CeremonyCompleted = 6,
    Removed = 7,
}

public enum ElectionCeremonyShareCustodyStatus
{
    NotExported = 0,
    Exported = 1,
    Imported = 2,
    ImportFailed = 3,
}

public enum ElectionFinalizationSessionStatus
{
    AwaitingShares = 0,
    Completed = 1,
}

public enum ElectionFinalizationShareStatus
{
    Accepted = 0,
    Rejected = 1,
}

public enum ElectionFinalizationTargetType
{
    AggregateTally = 0,
    SingleBallot = 1,
}

public enum ElectionFinalizationReleaseMode
{
    AggregateTallyOnly = 0,
}

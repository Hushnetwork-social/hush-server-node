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

public enum ElectionRosterContactType
{
    Email = 0,
    Phone = 1,
}

public enum ElectionVoterLinkStatus
{
    Unlinked = 0,
    Linked = 1,
}

public enum ElectionVotingRightStatus
{
    Inactive = 0,
    Active = 1,
}

public enum ElectionParticipationStatus
{
    DidNotVote = 0,
    CountedAsVoted = 1,
    Blank = 2,
}

public enum ElectionEligibilityActivationOutcome
{
    Activated = 0,
    Blocked = 1,
}

public enum ElectionEligibilityActivationBlockReason
{
    None = 0,
    RosterEntryNotFound = 1,
    NotRosteredAtOpen = 2,
    AlreadyActive = 3,
    PolicyDisallowsLateActivation = 4,
    ElectionNotOpen = 5,
    NotLinkedToHushAccount = 6,
}

public enum ElectionEligibilitySnapshotType
{
    Open = 0,
    Close = 1,
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

public enum OfficialResultVisibilityPolicy
{
    ParticipantEncryptedOnly = 0,
    PublicPlaintext = 1,
}

public enum ElectionClosedProgressStatus
{
    None = 0,
    WaitingForTrusteeShares = 1,
    TallyCalculationInProgress = 2,
}

public enum ElectionBoundaryArtifactType
{
    Open = 0,
    Close = 1,
    TallyReady = 2,
    Finalize = 3,
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

public enum ElectionFinalizationSessionPurpose
{
    CloseCounting = 0,
    Finalization = 1,
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

public enum ElectionResultArtifactKind
{
    Unofficial = 0,
    Official = 1,
}

public enum ElectionResultArtifactVisibility
{
    ParticipantEncrypted = 0,
    PublicPlaintext = 1,
}

public enum ElectionReportPackageStatus
{
    GenerationFailed = 0,
    Sealed = 1,
}

public enum ElectionReportArtifactKind
{
    HumanManifest = 0,
    HumanResultReport = 1,
    HumanNamedParticipationRoster = 2,
    HumanAuditProvenanceReport = 3,
    HumanOutcomeDetermination = 4,
    HumanDisputeReviewIndex = 5,
    MachineManifest = 6,
    MachineEvidenceGraph = 7,
    MachineResultReportProjection = 8,
    MachineNamedParticipationRosterProjection = 9,
    MachineAuditProvenanceReportProjection = 10,
    MachineOutcomeDeterminationProjection = 11,
    MachineDisputeReviewIndexProjection = 12,
}

public enum ElectionReportArtifactFormat
{
    Markdown = 0,
    Json = 1,
}

public enum ElectionReportArtifactAccessScope
{
    OwnerAuditorOnly = 0,
    OwnerAuditorTrustee = 1,
}

public enum ElectionReportAccessGrantRole
{
    DesignatedAuditor = 0,
}

public enum ElectionPublicationIssueCode
{
    RerandomizationFallback = 0,
    UnsupportedBallotPayload = 1,
    ReplayMismatch = 2,
}

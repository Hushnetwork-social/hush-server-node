namespace HushShared.Elections.Model;

public enum ElectionLifecycleState
{
    Draft = 0,
    Open = 1,
    Closed = 2,
    Finalized = 3,
    Voided = 4,
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

public enum ElectionActorLinkMultiplicityPolicy
{
    SingleRosterEntryPerActor = 0,
    MultipleRosterEntriesPerActorAllowed = 1,
}

public enum ElectionIdentityLinkPolicy
{
    ContactCodeV1 = 0,
    PrelinkedHushAccountV1 = 1,
    OwnerManualVerificationV1 = 2,
}

public enum ElectionCheckoffVisibilityPolicy
{
    RestrictedOwnerAuditor = 0,
}

public enum ElectionContactCodeProviderReadiness
{
    Missing = 0,
    DevOnly = 1,
    Degraded = 2,
    Ready = 3,
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
    PublicationProofPending = 3,
    PublicationProofGenerating = 4,
    PublicationProofSelfVerifying = 5,
    PublicationProofFailed = 6,
    PublicationProofVerified = 7,
}

public enum ElectionBoundaryArtifactType
{
    Open = 0,
    Close = 1,
    TallyReady = 2,
    Finalize = 3,
    Void = 4,
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

public enum ElectionCloseCountingJobStatus
{
    Pending = 0,
    AwaitingShares = 1,
    ThresholdReached = 2,
    Running = 3,
    Publishing = 4,
    Completed = 5,
    Failed = 6,
    Superseded = 7,
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
    SupersededByVoid = 2,
}

public enum ElectionReportPackageKind
{
    FinalResult = 0,
    Void = 1,
    FailedFinalize = 2,
}

public enum ElectionOutcomeStatus
{
    None = 0,
    CleanFinalized = 1,
    FinalizedWithAnomaly = 2,
    Voided = 3,
    FailedToFinalize = 4,
}

public enum ElectionGovernedOutcomeDecisionType
{
    AcceptFixedUnofficialResultWithAnomaly = 0,
    RecordFailedFinalizeContinuity = 1,
}

public enum ElectionGovernedOutcomeFinalizationMode
{
    None = 0,
    CleanFinalization = 1,
    AbnormalFinalization = 2,
    FailedFinalization = 3,
}

public enum ElectionTrusteeContinuityStatus
{
    None = 0,
    KeyLost = 1,
}

public enum ElectionDeploymentProofEvidenceStatus
{
    Accepted = 0,
    AcceptedWithLimitations = 1,
    Degraded = 2,
    Blocked = 3,
    Missing = 4,
    Stale = 5,
    Superseded = 6,
    Unknown = 7,
    NotRequired = 8,
    Mismatch = 9,
    NotYetSupported = 10,
}

public enum ElectionDeploymentProofLedgerVisibility
{
    Public = 0,
    Restricted = 1,
    Internal = 2,
}

public enum ElectionDeploymentProofCheckpointType
{
    DraftToOpen = 0,
    OpenToClose = 1,
    CloseToFinalize = 2,
    ClosedToFinalizedWithAnomaly = 3,
    OpenToVoid = 4,
    CloseToVoid = 5,
    FinalPackageExport = 6,
}

public enum ElectionDeploymentProofComponentId
{
    HushServerNode = 0,
    HushWebClient = 1,
}

public enum ElectionDeploymentProofObservationSource
{
    Provider = 0,
    Fixture = 1,
    Catalog = 2,
    Feat144Handshake = 3,
    NotAvailable = 4,
}

public enum ElectionDeploymentProofImpactClassification
{
    NoChange = 0,
    WebsiteOnlyNoProtocolChange = 1,
    NonVotingServiceNoProtocolChange = 2,
    VotingProtocolNoChange = 3,
    VotingProtocolChange = 4,
    OperationalConfigChange = 5,
    EmergencyChange = 6,
    Rollback = 7,
    UnknownPendingClassification = 8,
}

public enum ElectionDeploymentProofClaimEffect
{
    Accepted = 0,
    AcceptedWithLimitations = 1,
    Downgraded = 2,
    Blocked = 3,
    NoClaim = 4,
    NotApplicable = 5,
}

public enum ElectionVoidEvidenceReferenceKind
{
    InternalAnomalyThread = 0,
    InternalTrusteeContinuity = 1,
    InternalOperationalIncident = 2,
    InternalSupportRecord = 3,
    ExternalGovernance = 4,
}

public enum ElectionVoidPublicationAttemptStatus
{
    Pending = 0,
    GenerationFailed = 1,
    Sealed = 2,
}

public enum ElectionVoidSupersededArtifactKind
{
    ReportPackage = 0,
    ReportArtifact = 1,
    VerificationPackage = 2,
    PublicStatus = 3,
}

public enum ElectionVoidEvidenceVisibility
{
    Public = 0,
    RestrictedOwnerAuditor = 1,
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
    MachineRestrictedAnomalyIntakeManifest = 13,
    MachineVoidDecision = 14,
    HumanVoidSummary = 15,
    MachineVoidPublicStatus = 16,
    MachineVoidSupersededArtifacts = 17,
    MachineVoidVerifierResult = 18,
    HumanRestrictedVoidEvidenceIndex = 19,
    MachineRestrictedHistoricalUnofficialResult = 20,
    MachineVoidPackageManifest = 21,
    MachineVoidPackageArchive = 22,
    MachineAbnormalFinalizationEvidence = 23,
    MachineDeploymentProofBindingLedger = 24,
}

public enum ElectionReportArtifactFormat
{
    Markdown = 0,
    Json = 1,
    Binary = 2,
}

public enum ElectionReportArtifactAccessScope
{
    OwnerAuditorOnly = 0,
    OwnerAuditorTrustee = 1,
    Public = 2,
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
    WitnessSealUnavailable = 3,
}

public enum ElectionBallotDefinitionMutationPolicy
{
    ImmutableAfterOpen = 0,
}

public enum ElectionPreparedBallotState
{
    Prepared = 0,
    Spoiled = 1,
    Cast = 2,
    Expired = 3,
}

public enum ElectionVoterCeremonyFinalState
{
    None = 0,
    FinalCastAccepted = 1,
    ExpiredWithoutCast = 2,
}

public enum ProtocolPackageKind
{
    Specification = 0,
    ProofAndCryptoReview = 1,
}

public enum ProtocolPackageApprovalStatus
{
    DraftPrivate = 0,
    ApprovedInternal = 1,
    Retired = 2,
}

public enum ProtocolPackageExternalReviewStatus
{
    NotReviewed = 0,
    ReviewRequested = 1,
    ReviewInProgress = 2,
    ReviewedWithFindings = 3,
    ReviewedAccepted = 4,
}

public enum ProtocolPackageAccessLocationKind
{
    PublicWebsite = 0,
    AuditorWelcomePackage = 1,
    ReviewerPortal = 2,
    ControlledDownload = 3,
    Repository = 4,
}

public enum ProtocolPackageBindingStatus
{
    Missing = 0,
    Latest = 1,
    Stale = 2,
    Incompatible = 3,
    Sealed = 4,
    ReferenceOnly = 5,
}

public enum ProtocolPackageBindingSource
{
    CatalogSelection = 0,
    OwnerRefresh = 1,
    SealedAtOpen = 2,
    MigrationBackfill = 3,
}

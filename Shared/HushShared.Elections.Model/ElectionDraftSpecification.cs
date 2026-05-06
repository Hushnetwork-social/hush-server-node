namespace HushShared.Elections.Model;

public record ElectionDraftSpecification(
    string Title,
    string? ShortDescription,
    string? ExternalReferenceCode,
    ElectionClass ElectionClass,
    ElectionBindingStatus BindingStatus,
    string SelectedProfileId,
    ElectionGovernanceMode GovernanceMode,
    ElectionDisclosureMode DisclosureMode,
    ParticipationPrivacyMode ParticipationPrivacyMode,
    VoteUpdatePolicy VoteUpdatePolicy,
    EligibilitySourceType EligibilitySourceType,
    EligibilityMutationPolicy EligibilityMutationPolicy,
    OutcomeRuleDefinition OutcomeRule,
    IReadOnlyList<ApprovedClientApplicationRecord> ApprovedClientApplications,
    string ProtocolOmegaVersion,
    ReportingPolicy ReportingPolicy,
    ReviewWindowPolicy ReviewWindowPolicy,
    IReadOnlyList<ElectionOptionDefinition> OwnerOptions,
    IReadOnlyList<ElectionWarningCode>? AcknowledgedWarningCodes = null,
    int? RequiredApprovalCount = null,
    OfficialResultVisibilityPolicy OfficialResultVisibilityPolicy = OfficialResultVisibilityPolicy.ParticipantEncryptedOnly,
    ElectionIdentityLinkPolicy IdentityLinkPolicy = ElectionIdentityLinkPolicy.ContactCodeV1,
    ElectionCheckoffVisibilityPolicy CheckoffVisibilityPolicy = ElectionCheckoffVisibilityPolicy.RestrictedOwnerAuditor,
    ElectionActorLinkMultiplicityPolicy ActorLinkMultiplicityPolicy = ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor,
    ElectionContactCodeProviderReadiness ContactCodeProviderReadiness = ElectionContactCodeProviderReadiness.DevOnly)
{
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionAnomalyAuthorization
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ElectionAnomalySubmitterResolution ResolveActorSubmitter(
        ElectionRecord election,
        string actorPublicAddress,
        DateTime submittedAt,
        ElectionRosterEntryRecord? linkedRosterEntry = null,
        ElectionTrusteeInvitationRecord? acceptedTrusteeInvitation = null,
        ElectionReportAccessGrantRecord? reportAccessGrant = null,
        string? requestedRoleContextId = null)
    {
        var actor = NormalizeAddress(actorPublicAddress);
        if (string.IsNullOrWhiteSpace(actor))
        {
            return ElectionAnomalySubmitterResolution.Unresolved(ElectionAnomalyValidationCodes.InvalidActionSignatory);
        }

        var candidates = new List<ElectionAnomalyRoleEvidence>();
        if (string.Equals(election.OwnerPublicAddress, actor, StringComparison.Ordinal))
        {
            candidates.Add(new ElectionAnomalyRoleEvidence(
                ElectionAnomalyActorRoleContextIds.ElectionOwner,
                ElectionAnomalyRoleEvidenceTypeIds.ElectionOwner,
                $"election-owner:{election.ElectionId}"));
        }

        if (acceptedTrusteeInvitation is not null &&
            acceptedTrusteeInvitation.Status == ElectionTrusteeInvitationStatus.Accepted &&
            string.Equals(acceptedTrusteeInvitation.TrusteeUserAddress, actor, StringComparison.Ordinal))
        {
            candidates.Add(new ElectionAnomalyRoleEvidence(
                ElectionAnomalyActorRoleContextIds.Trustee,
                ElectionAnomalyRoleEvidenceTypeIds.TrusteeInvitation,
                $"trustee-invitation:{acceptedTrusteeInvitation.Id}"));
        }

        if (linkedRosterEntry is not null &&
            linkedRosterEntry.IsLinked &&
            string.Equals(linkedRosterEntry.LinkedActorPublicAddress, actor, StringComparison.Ordinal))
        {
            candidates.Add(new ElectionAnomalyRoleEvidence(
                ElectionAnomalyActorRoleContextIds.Voter,
                ElectionAnomalyRoleEvidenceTypeIds.VoterRosterLink,
                $"roster-entry:{linkedRosterEntry.OrganizationVoterId}"));
        }

        if (reportAccessGrant is not null &&
            reportAccessGrant.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor &&
            string.Equals(reportAccessGrant.ActorPublicAddress, actor, StringComparison.Ordinal))
        {
            candidates.Add(new ElectionAnomalyRoleEvidence(
                ElectionAnomalyActorRoleContextIds.DesignatedAuditor,
                ElectionAnomalyRoleEvidenceTypeIds.DesignatedAuditorGrant,
                $"report-access-grant:{reportAccessGrant.Id}"));
        }

        var selectedEvidence = SelectEvidence(candidates, requestedRoleContextId);
        if (selectedEvidence is null)
        {
            return ElectionAnomalySubmitterResolution.Unresolved(ElectionAnomalyValidationCodes.PersonScopeUnresolved);
        }

        var windowDecision = EvaluateSubmissionWindow(election, submittedAt);
        if (!windowDecision.CanSubmit)
        {
            return ElectionAnomalySubmitterResolution.Unresolved(windowDecision.ValidationCode!);
        }

        return ElectionAnomalySubmitterResolution.Resolved(
            ComputePersonScopeId(election.ElectionId, "account", actor),
            actor,
            selectedEvidence,
            election.LifecycleState,
            election.AnomalySubmissionWindowClosesAt);
    }

    public static ElectionAnomalySubmitterResolution ResolveExternalClaimantSubmitter(
        ElectionRecord election,
        string actorPublicAddress,
        string externalClaimantReferenceHash,
        DateTime submittedAt)
    {
        var actor = NormalizeAddress(actorPublicAddress);
        if (!string.Equals(election.OwnerPublicAddress, actor, StringComparison.Ordinal))
        {
            return ElectionAnomalySubmitterResolution.Unresolved(ElectionAnomalyValidationCodes.InvalidActionSignatory);
        }

        var claimantReference = externalClaimantReferenceHash.Trim();
        if (string.IsNullOrWhiteSpace(claimantReference))
        {
            return ElectionAnomalySubmitterResolution.Unresolved(ElectionAnomalyValidationCodes.PersonScopeUnresolved);
        }

        var windowDecision = EvaluateSubmissionWindow(election, submittedAt);
        if (!windowDecision.CanSubmit)
        {
            return ElectionAnomalySubmitterResolution.Unresolved(windowDecision.ValidationCode!);
        }

        return ElectionAnomalySubmitterResolution.Resolved(
            ComputePersonScopeId(election.ElectionId, "external_claimant", claimantReference),
            actor,
            new ElectionAnomalyRoleEvidence(
                ElectionAnomalyActorRoleContextIds.ExternalClaimantRegistrar,
                ElectionAnomalyRoleEvidenceTypeIds.ExternalClaimantBridge,
                $"external-claimant:{claimantReference}"),
            election.LifecycleState,
            election.AnomalySubmissionWindowClosesAt);
    }

    public static ElectionAnomalySubmissionWindowDecision EvaluateSubmissionWindow(
        ElectionRecord election,
        DateTime submittedAt)
    {
        if (election.AnomalySubmissionWindowClosesAt.HasValue &&
            submittedAt > election.AnomalySubmissionWindowClosesAt.Value)
        {
            return new ElectionAnomalySubmissionWindowDecision(
                false,
                ElectionAnomalyValidationCodes.SubmissionWindowClosed);
        }

        return new ElectionAnomalySubmissionWindowDecision(true, null);
    }

    public static ElectionAnomalyOwnThreadReadDecision CanActorReadOwnThread(
        ElectionAnomalyThreadRecord thread,
        string actorPublicAddress)
    {
        var actor = NormalizeAddress(actorPublicAddress);
        if (string.IsNullOrWhiteSpace(actor))
        {
            return new ElectionAnomalyOwnThreadReadDecision(false, ElectionAnomalyValidationCodes.ReadForbidden);
        }

        var actorPersonScopeId = ComputePersonScopeId(thread.ElectionId, "account", actor);

        return string.Equals(actorPersonScopeId, thread.SubmitterPersonScopeId, StringComparison.Ordinal)
            ? new ElectionAnomalyOwnThreadReadDecision(true, null)
            : new ElectionAnomalyOwnThreadReadDecision(false, ElectionAnomalyValidationCodes.ReadForbidden);
    }

    public static ElectionAnomalyOwnThreadReadDecision CanActorRespondAsSubmitter(
        ElectionAnomalyThreadRecord thread,
        string actorPublicAddress)
    {
        var ownThreadDecision = CanActorReadOwnThread(thread, actorPublicAddress);
        if (ownThreadDecision.CanRead)
        {
            return ownThreadDecision;
        }

        var actor = NormalizeAddress(actorPublicAddress);
        if (string.IsNullOrWhiteSpace(actor))
        {
            return new ElectionAnomalyOwnThreadReadDecision(false, ElectionAnomalyValidationCodes.ReadForbidden);
        }

        var isExternalClaimantBridge =
            string.Equals(
                thread.SubmitterRoleContextId,
                ElectionAnomalyActorRoleContextIds.ExternalClaimantRegistrar,
                StringComparison.Ordinal) &&
            string.Equals(thread.SubmitterActorPublicAddress, actor, StringComparison.Ordinal);

        return isExternalClaimantBridge
            ? new ElectionAnomalyOwnThreadReadDecision(true, null)
            : new ElectionAnomalyOwnThreadReadDecision(false, ElectionAnomalyValidationCodes.ReadForbidden);
    }

    private static ElectionAnomalyRoleEvidence? SelectEvidence(
        IReadOnlyList<ElectionAnomalyRoleEvidence> candidates,
        string? requestedRoleContextId)
    {
        var requested = requestedRoleContextId?.Trim();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return candidates.FirstOrDefault(x => string.Equals(x.RoleContextId, requested, StringComparison.Ordinal));
        }

        return candidates
            .OrderBy(x => RolePriority(x.RoleContextId))
            .FirstOrDefault();
    }

    private static int RolePriority(string roleContextId) =>
        roleContextId switch
        {
            ElectionAnomalyActorRoleContextIds.ElectionOwner => 0,
            ElectionAnomalyActorRoleContextIds.Trustee => 1,
            ElectionAnomalyActorRoleContextIds.Voter => 2,
            ElectionAnomalyActorRoleContextIds.DesignatedAuditor => 3,
            _ => 10,
        };

    private static string ComputePersonScopeId(
        ElectionId electionId,
        string scopeKind,
        string scopeValue)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = ElectionAnomalyPersonScopeDerivationVersions.Current,
            electionId = electionId.ToString(),
            scopeKind,
            scopeValue = scopeValue.Trim(),
        }, JsonOptions);

        return $"sha256:{Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}";
    }

    private static string NormalizeAddress(string actorPublicAddress) =>
        actorPublicAddress.Trim();
}

public record ElectionAnomalyRoleEvidence(
    string RoleContextId,
    string EvidenceTypeId,
    string EvidenceReference);

public record ElectionAnomalySubmitterResolution(
    bool IsResolved,
    string? SubmitterPersonScopeId,
    string PersonScopeDerivationVersion,
    string? ActorPublicAddress,
    string? RoleContextId,
    string? RoleEvidenceTypeId,
    string? RoleEvidenceReference,
    ElectionLifecycleState? LifecycleStateAtSubmission,
    DateTime? SubmissionWindowClosesAt,
    string? ValidationCode)
{
    public static ElectionAnomalySubmitterResolution Resolved(
        string submitterPersonScopeId,
        string actorPublicAddress,
        ElectionAnomalyRoleEvidence evidence,
        ElectionLifecycleState lifecycleStateAtSubmission,
        DateTime? submissionWindowClosesAt) =>
        new(
            true,
            submitterPersonScopeId,
            ElectionAnomalyPersonScopeDerivationVersions.Current,
            actorPublicAddress,
            evidence.RoleContextId,
            evidence.EvidenceTypeId,
            evidence.EvidenceReference,
            lifecycleStateAtSubmission,
            submissionWindowClosesAt,
            ValidationCode: null);

    public static ElectionAnomalySubmitterResolution Unresolved(string validationCode) =>
        new(
            false,
            null,
            ElectionAnomalyPersonScopeDerivationVersions.Current,
            null,
            null,
            null,
            null,
            null,
            null,
            validationCode);
}

public record ElectionAnomalySubmissionWindowDecision(
    bool CanSubmit,
    string? ValidationCode);

public record ElectionAnomalyOwnThreadReadDecision(
    bool CanRead,
    string? ValidationCode);

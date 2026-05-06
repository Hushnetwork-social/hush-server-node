using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public sealed partial class ElectionVerificationPackageExportService
{
    private static ElectionRecordReferenceRecord BuildElectionRecord(
        ElectionVerificationPackageExportRequest request)
    {
        var binding = request.ProtocolPackageBinding!;
        return new ElectionRecordReferenceRecord(
            request.Election.ElectionId.ToString(),
            request.Election.LifecycleState.ToString(),
            binding.PackageId,
            binding.PackageVersion,
            binding.PackageApprovalStatus.ToString(),
            binding.SpecPackageHash,
            binding.ProofPackageHash,
            binding.ReleaseManifestHash,
            binding.SpecAccessLocations
                .Concat(binding.ProofAccessLocations)
                .Select(x => new VerificationAccessLocationRecord(x.LocationKind.ToString(), x.Location, x.ContentHash))
                .ToArray());
    }

    private static VerifierProfileRecord BuildProfile(string profileId)
    {
        var highAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        var restricted = string.Equals(profileId, VerificationProfileIds.RestrictedOwnerAuditorV1, StringComparison.Ordinal);
        return new VerifierProfileRecord(
            profileId,
            profileId,
            AllowsDraftProtocolPackage: !highAssurance,
            AllowsPendingLaterFeatureEvidence: !highAssurance,
            RequiresRestrictedEvidence: restricted,
            RequiresHighAssuranceEvidence: highAssurance,
            RequiredCheckCodes:
            [
                "VFY-MAN-000",
                "VFY-ELECTION-000",
                "VFY-ACCEPTED-000",
                "VFY-PUBLISHED-000",
                "VFY-SP04-000",
                "CTRL-000",
                "VFY-PRIVACY-000",
            ]);
    }

    private static AcceptedBallotSetArtifactRecord BuildAcceptedBallotSet(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.AcceptedBallots.Count,
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots)),
            request.AcceptedBallots
                .OrderBy(x => x.BallotNullifier, StringComparer.Ordinal)
                .Select(x => new AcceptedBallotArtifactRecord(
                    x.BallotNullifier,
                    x.EncryptedBallotPackage,
                    x.ProofBundle,
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.EncryptedBallotPackage),
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.ProofBundle),
                    x.PreparedBallotId,
                    x.PreparedBallotHash,
                    x.ReceiptCommitment,
                    x.ReceiptCommitmentScheme,
                    x.BallotDefinitionVersion,
                    x.BallotDefinitionHash))
                .ToArray());

    private static PublishedBallotStreamArtifactRecord BuildPublishedBallotStream(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.PublishedBallots.Count,
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots)),
            request.PublishedBallots
                .OrderBy(x => x.PublicationSequence)
                .Select(x => new PublishedBallotArtifactRecord(
                    x.PublicationSequence,
                    x.EncryptedBallotPackage,
                    x.ProofBundle,
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.EncryptedBallotPackage),
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.ProofBundle)))
                .ToArray());

    private static TallyReplayArtifactRecord BuildTallyReplay(ElectionVerificationPackageExportRequest request)
    {
        var highAssurance = string.Equals(request.VerifierProfileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        return new TallyReplayArtifactRecord(
            request.Election.ElectionId.ToString(),
            PublicationProofMode: "zk_rerandomization_shuffle_v1",
            highAssurance ? VerificationCheckStatus.Fail : VerificationCheckStatus.Warn,
            VerificationResultCodes.PublicationProofEvidencePending,
            highAssurance
                ? "High-assurance profile requires SP-07 publication proof evidence."
                : "SP-07 publication proof evidence is pending a later protocol package revision.");
    }

    private static TrusteeReleaseEvidenceArtifactRecord BuildTrusteeReleaseEvidence(
        ElectionVerificationPackageExportRequest request)
    {
        var highAssurance = IsSp06EvidenceExpected(request);
        return new TrusteeReleaseEvidenceArtifactRecord(
            request.Election.ElectionId.ToString(),
            request.FinalizationSessions.Count,
            request.FinalizationShares.Count(x => x.IsAccepted),
            request.FinalizationShares
                .Where(x => x.IsAccepted)
                .OrderBy(x => x.ShareIndex)
                .Select(x => new TrusteeReleaseShareEvidenceRecord(
                    highAssurance ? BuildTrusteeId(x.TrusteeUserAddress) : x.TrusteeUserAddress,
                    x.ShareIndex,
                    x.ShareMaterialHash,
                    x.Status.ToString()))
                .ToArray());
    }

    private static ResultBindingArtifactRecord BuildResultBinding(ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.ReportPackage!.Id.ToString(),
            VerificationCanonicalHash.ToLowerHex(request.ReportPackage.PackageHash),
            request.Election.FinalizeArtifactId?.ToString(),
            request.Election.OfficialResultArtifactId?.ToString(),
            request.Election.UnofficialResultArtifactId?.ToString());

    private static ElectionSp04EvidenceRecord BuildSp04Evidence(
        ElectionVerificationPackageExportRequest request)
    {
        var receiptCommitments = BuildSp04ReceiptCommitments(request);
        return new ElectionSp04EvidenceRecord(
            request.Election.ElectionId,
            new ElectionSp04PolicyRecord(
                ElectionSp04ProfileIds.ChallengeSpoilV1,
                RequiredChallengeCount: 1,
                PreparedPackageTtlSeconds: 900,
                request.Election.BallotDefinitionMutationPolicy ??
                    ElectionBallotDefinitionMutationPolicy.ImmutableAfterOpen),
            request.Election.BallotDefinitionVersion ?? 0,
            request.Election.BallotDefinitionHash ?? Array.Empty<byte>(),
            request.Election.BallotDefinitionSealedAt ?? DateTime.MinValue,
            (request.PreparedBallotCommitments ?? Array.Empty<ElectionPreparedBallotCommitmentRecord>()).Count,
            (request.SpoiledPreparedBallots ?? Array.Empty<ElectionSpoiledPreparedBallotRecord>()).Count,
            receiptCommitments.Count,
            ComputeReceiptCommitmentSetHash(receiptCommitments),
            PublicPrivacyBoundary:
            [
                "no_named_voter",
                "no_spoiled_plaintext",
                "no_final_randomness",
                "no_proof_material",
            ]);
    }

    private static IReadOnlyList<ElectionSp04ReceiptCommitmentRecord> BuildSp04ReceiptCommitments(
        ElectionVerificationPackageExportRequest request) =>
        request.AcceptedBallots
            .Where(x =>
                x.PreparedBallotId.HasValue &&
                !string.IsNullOrWhiteSpace(x.PreparedBallotHash) &&
                !string.IsNullOrWhiteSpace(x.ReceiptCommitment) &&
                !string.IsNullOrWhiteSpace(x.ReceiptCommitmentScheme))
            .OrderBy(x => x.AcceptedAt)
            .ThenBy(x => x.Id)
            .Select(x => new ElectionSp04ReceiptCommitmentRecord(
                x.Id,
                x.PreparedBallotId!.Value,
                x.PreparedBallotHash!,
                x.ReceiptCommitment!,
                x.ReceiptCommitmentScheme!,
                x.AcceptedAt))
            .ToArray();

    private static IReadOnlyList<ElectionSp04RestrictedCeremonyRecord> BuildRestrictedSp04CeremonyRecords(
        ElectionVerificationPackageExportRequest request) =>
        (request.VoterCeremonyRecords ?? Array.Empty<ElectionVoterCeremonyRecord>())
            .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .Select(x => new ElectionSp04RestrictedCeremonyRecord(
                x.Id,
                x.ElectionId,
                x.OrganizationVoterId,
                x.LinkedActorPublicAddress,
                x.CeremonyProfileId,
                x.PreparedPackageCount,
                x.SpoiledPackageCount,
                x.FinalState,
                x.FinalAcceptedBallotId))
            .ToArray();

    private static IReadOnlyList<ElectionSp04RestrictedPreparedBallotRecord> BuildRestrictedSp04PreparedBallots(
        ElectionVerificationPackageExportRequest request) =>
        (request.PreparedBallotCommitments ?? Array.Empty<ElectionPreparedBallotCommitmentRecord>())
            .OrderBy(x => x.PrecommittedAt)
            .ThenBy(x => x.PreparedBallotId)
            .Select(x => new ElectionSp04RestrictedPreparedBallotRecord(
                x.PreparedBallotId,
                x.ElectionId,
                x.OrganizationVoterId,
                x.LinkedActorPublicAddress,
                x.PreparedBallotHash,
                x.BallotDefinitionVersion,
                x.BallotDefinitionHash,
                x.CeremonyProfileId,
                x.ProofStatementId,
                x.State,
                x.PrecommittedAt,
                x.ExpiresAt,
                x.SpoilMarkerId,
                x.AcceptedBallotId))
            .ToArray();

    private static IReadOnlyList<ElectionSp04RestrictedSpoilMarkerRecord> BuildRestrictedSp04SpoilMarkers(
        ElectionVerificationPackageExportRequest request) =>
        (request.SpoiledPreparedBallots ?? Array.Empty<ElectionSpoiledPreparedBallotRecord>())
            .OrderBy(x => x.SpoiledAt)
            .ThenBy(x => x.Id)
            .Select(x => new ElectionSp04RestrictedSpoilMarkerRecord(
                x.Id,
                x.ElectionId,
                x.PreparedBallotId,
                x.PreparedBallotHash,
                x.SpoiledTranscriptHash,
                x.SpoilRecordHash,
                x.LocalVerifierVersion,
                x.SpoiledAt))
            .ToArray();

    private static string ComputeReceiptCommitmentSetHash(
        IReadOnlyList<ElectionSp04ReceiptCommitmentRecord> receiptCommitments)
    {
        var payload = string.Join(
            '\n',
            receiptCommitments
                .OrderBy(x => x.AcceptedBallotId)
                .Select(x =>
                    $"{x.AcceptedBallotId:N}|{x.PreparedBallotId:N}|{x.PreparedBallotHash}|{x.ReceiptCommitment}|{x.ReceiptCommitmentScheme}|{x.AcceptedAt:O}"));

        return VerificationCanonicalHash.ComputeSha256UpperHex(payload);
    }

    private static ElectionSp05PolicyArtifactRecord BuildSp05EligibilityPolicy(
        ElectionVerificationPackageExportRequest request)
    {
        var policyEvidence = (request.EligibilityPolicyEvidences ?? Array.Empty<ElectionEligibilityPolicyEvidenceRecord>())
            .OrderByDescending(x => x.DeclaredAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var schemeEvidence = (request.CommitmentSchemeEvidences ?? Array.Empty<ElectionCommitmentSchemeEvidenceRecord>())
            .OrderByDescending(x => x.DeclaredAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        return new ElectionSp05PolicyArtifactRecord(
            request.Election.ElectionId.ToString(),
            policyEvidence?.EligibilityPolicyId ?? ElectionSp05ProfileIds.OrganizationalEligibilityCheckoffV1,
            policyEvidence?.EligibilityPolicyVersion ?? "1.0.0",
            policyEvidence?.EligibilityMutationPolicy ?? request.Election.EligibilityMutationPolicy,
            policyEvidence?.IdentityLinkPolicy ?? request.Election.IdentityLinkPolicy,
            policyEvidence?.CheckoffVisibilityPolicy ?? request.Election.CheckoffVisibilityPolicy,
            policyEvidence?.ActorLinkMultiplicityPolicy ?? request.Election.ActorLinkMultiplicityPolicy,
            policyEvidence?.ContactCodeProviderReadiness ?? request.Election.ContactCodeProviderReadiness,
            schemeEvidence?.RosterCanonicalizationVersion ?? ElectionSp05ProfileIds.RosterCanonicalizationV1,
            schemeEvidence?.RosterCanonicalizationVersionHash ?? ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
            policyEvidence?.EligibilityPolicyCanonicalizationVersion ?? ElectionSp05ProfileIds.EligibilityPolicyCanonicalizationV1,
            policyEvidence?.EligibilityPolicyCanonicalizationVersionHash ?? ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
            schemeEvidence?.CommitmentSchemeVersion ?? ElectionSp05ProfileIds.VoteCommitmentPreimageV1,
            schemeEvidence?.CommitmentSchemeVersionHash ?? ElectionEligibilityContracts.CommitmentSchemeVersionHash,
            schemeEvidence?.NullifierSchemeVersion ?? ElectionSp05ProfileIds.VoteNullifierPreimageV1,
            schemeEvidence?.NullifierSchemeVersionHash ?? ElectionEligibilityContracts.NullifierSchemeVersionHash);
    }

    private static ElectionSp05SummaryArtifactRecord BuildSp05EligibilitySummary(
        ElectionVerificationPackageExportRequest request)
    {
        var latestImportEvidence = (request.RosterImportEvidences ?? Array.Empty<ElectionRosterImportEvidenceRecord>())
            .OrderByDescending(x => x.RosterImportVersion)
            .ThenByDescending(x => x.ImportedAt)
            .FirstOrDefault();
        var rosteredEntries = request.RosterEntries
            .Where(x => x.WasPresentAtOpen)
            .ToArray();
        var activeDenominatorEntries = request.Election.EligibilityMutationPolicy == EligibilityMutationPolicy.FrozenAtOpen
            ? request.RosterEntries.Where(x => x.WasPresentAtOpen && x.WasActiveAtOpen).ToArray()
            : request.RosterEntries.Where(x => x.WasPresentAtOpen && x.IsActive).ToArray();
        var participationRecords = request.ParticipationRecords.ToDictionary(
            x => x.OrganizationVoterId,
            StringComparer.OrdinalIgnoreCase);
        var countedParticipationCount = participationRecords.Values.Count(x => x.ParticipationStatus == ElectionParticipationStatus.CountedAsVoted);
        var blankCount = participationRecords.Values.Count(x => x.ParticipationStatus == ElectionParticipationStatus.Blank);
        var activeDenominatorCount = activeDenominatorEntries.Length;
        var didNotVoteCount = Math.Max(0, activeDenominatorCount - countedParticipationCount - blankCount);
        var commitmentRegistrations = request.CommitmentRegistrations ?? Array.Empty<ElectionCommitmentRegistrationRecord>();
        var activationEvents = request.EligibilityActivationEvents ?? Array.Empty<ElectionEligibilityActivationEventRecord>();

        return new ElectionSp05SummaryArtifactRecord(
            request.Election.ElectionId.ToString(),
            latestImportEvidence?.RosterSourceFileHash ?? VerificationCanonicalHash.ComputeSha256LowerHex(string.Empty),
            latestImportEvidence?.RosterCanonicalHash ?? ElectionEligibilityContracts.ComputeRosterCanonicalHash(request.RosterEntries),
            ComputeOrganizationVoterIdSetHash(rosteredEntries.Select(x => x.OrganizationVoterId)),
            ComputeOrganizationVoterIdSetHash(request.RosterEntries.Where(x => x.WasPresentAtOpen && x.WasActiveAtOpen).Select(x => x.OrganizationVoterId)),
            ComputeOrganizationVoterIdSetHash(activeDenominatorEntries.Select(x => x.OrganizationVoterId)),
            ComputeCommitmentRoot(commitmentRegistrations),
            request.RosterEntries.Count,
            request.RosterEntries.Count(x => x.IsLinked),
            activeDenominatorCount,
            commitmentRegistrations.Count,
            countedParticipationCount,
            blankCount,
            didNotVoteCount,
            activationEvents.Count(x => x.Outcome == ElectionEligibilityActivationOutcome.Activated),
            activationEvents.Count(x => x.Outcome == ElectionEligibilityActivationOutcome.Blocked),
            PublicPrivacyBoundary:
            [
                "no_organization_voter_id",
                "no_contact_value",
                "no_linked_actor",
                "no_checkoff_id",
                "no_vote_secret",
                "no_plaintext_choice",
            ]);
    }

    private static ElectionSp05VerifierOutputArtifactRecord BuildSp05VerifierOutput(
        ElectionVerificationPackageExportRequest request,
        DateTime verifiedAt)
    {
        var summary = BuildSp05EligibilitySummary(request);
        var policy = BuildSp05EligibilityPolicy(request);
        var providerStatus = policy.ContactCodeProviderReadiness == ElectionContactCodeProviderReadiness.Ready
            ? VerificationCheckStatus.Pass
            : VerificationCheckStatus.Warn;
        var providerCode = policy.ContactCodeProviderReadiness == ElectionContactCodeProviderReadiness.Ready
            ? VerificationResultCodes.EligibilityEvidenceValid
            : VerificationResultCodes.EligibilityDevOnlyVerificationBlocked;

        return new ElectionSp05VerifierOutputArtifactRecord(
            request.Election.ElectionId.ToString(),
            request.VerifierProfileId,
            verifiedAt,
            [
                new VerifierCheckResultRecord(
                    "ELI-000",
                    VerificationCheckStatus.Pass,
                    VerificationResultCodes.EligibilityEvidenceValid,
                    "SP-05 public eligibility artifacts were generated.",
                    new Dictionary<string, string>
                    {
                        ["rostered_count"] = summary.RosteredCount.ToString(),
                    }),
                new VerifierCheckResultRecord(
                    "ELI-001",
                    VerificationCheckStatus.Pass,
                    VerificationResultCodes.EligibilityEvidenceValid,
                    "Eligibility policy is declared.",
                    new Dictionary<string, string>
                    {
                        ["eligibility_policy_id"] = policy.EligibilityPolicyId,
                    }),
                new VerifierCheckResultRecord(
                    "ELI-013",
                    providerStatus,
                    providerCode,
                    providerStatus == VerificationCheckStatus.Pass
                        ? "Contact-code provider readiness is production-ready."
                        : "Contact-code provider is not production-ready; high-assurance external review must treat this as blocked.",
                    new Dictionary<string, string>
                    {
                        ["contact_code_provider_readiness"] = policy.ContactCodeProviderReadiness.ToString(),
                    }),
            ]);
    }

    private static ElectionSp05RestrictedRosterImportEvidenceArtifactRecord BuildRestrictedSp05RosterImportEvidence(
        ElectionVerificationPackageExportRequest request)
    {
        var latestImportEvidence = (request.RosterImportEvidences ?? Array.Empty<ElectionRosterImportEvidenceRecord>())
            .OrderByDescending(x => x.RosterImportVersion)
            .ThenByDescending(x => x.ImportedAt)
            .FirstOrDefault() ??
            ElectionModelFactory.CreateRosterImportEvidence(
                request.Election.ElectionId,
                rosterImportVersion: 1,
                VerificationCanonicalHash.ComputeSha256LowerHex(string.Empty),
                ElectionEligibilityContracts.ComputeRosterCanonicalHash(request.RosterEntries),
                ElectionSp05ProfileIds.RosterCanonicalizationV1,
                ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
                request.RosterEntries.Count,
                rejectedRowCount: 0,
                invalidRowRejectionCount: 0,
                duplicateIdRejectionCount: 0,
                duplicateContactWarningCount: 0,
                importedByActor: request.Election.OwnerPublicAddress,
                importedAt: request.Election.CreatedAt);

        return new ElectionSp05RestrictedRosterImportEvidenceArtifactRecord(
            request.Election.ElectionId.ToString(),
            latestImportEvidence);
    }

    private static ElectionSp05RestrictedRosterArtifactRecord BuildRestrictedSp05Roster(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.RosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
                .Select(x => new ElectionSp05RestrictedRosterEntryArtifactRecord(
                    request.Election.ElectionId.ToString(),
                    x.OrganizationVoterId,
                    x.ContactType,
                    x.ContactValue,
                    DisplayLabel: null,
                    VoterGroup: null,
                    VotingWeightRef: null,
                    LegalBasisRef: null,
                    x.LinkStatus,
                    x.LinkedActorPublicAddress,
                    x.VotingRightStatus))
                .ToArray());

    private static ElectionSp05RestrictedLinkingEvidenceArtifactRecord BuildRestrictedSp05LinkingEvidence(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.RosterEntries
                .Where(x => x.IsLinked)
                .OrderBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
                .Select(x => new ElectionSp05RestrictedLinkEvidenceRecord(
                    x.OrganizationVoterId,
                    x.LinkedActorPublicAddress,
                    request.Election.IdentityLinkPolicy,
                    VerificationCanonicalHash.ComputeSha256UpperHex($"{request.Election.ElectionId}|{x.OrganizationVoterId}|{x.LinkedActorPublicAddress}|{x.LinkedAt:O}"),
                    x.LinkedAt,
                    x.LinkStatus.ToString()))
                .ToArray());

    private static ElectionSp05RestrictedActivationEventsArtifactRecord BuildRestrictedSp05ActivationEvents(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            (request.EligibilityActivationEvents ?? Array.Empty<ElectionEligibilityActivationEventRecord>())
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.Id)
                .ToArray());

    private static ElectionSp05RestrictedCheckoffLedgerArtifactRecord BuildRestrictedSp05CheckoffLedger(
        ElectionVerificationPackageExportRequest request)
    {
        var checkoffByVoter = (request.CheckoffConsumptions ?? Array.Empty<ElectionCheckoffConsumptionRecord>())
            .ToDictionary(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase);
        var participationByVoter = request.ParticipationRecords
            .ToDictionary(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase);

        return new ElectionSp05RestrictedCheckoffLedgerArtifactRecord(
            request.Election.ElectionId.ToString(),
            request.RosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
                .Select(x =>
                {
                    var participation = participationByVoter.GetValueOrDefault(x.OrganizationVoterId);
                    var checkoff = checkoffByVoter.GetValueOrDefault(x.OrganizationVoterId);
                    return new ElectionSp05RestrictedCheckoffLedgerEntryRecord(
                        request.Election.ElectionId.ToString(),
                        x.OrganizationVoterId,
                        participation?.ParticipationStatus ?? ElectionParticipationStatus.DidNotVote,
                        checkoff?.Id,
                        checkoff?.ConsumedAt,
                        checkoff is null ? null : ComputeAcceptedBallotReference(request, x.OrganizationVoterId));
                })
                .ToArray());
    }

    private static string? ComputeAcceptedBallotReference(
        ElectionVerificationPackageExportRequest request,
        string organizationVoterId)
    {
        var commitment = (request.CommitmentRegistrations ?? Array.Empty<ElectionCommitmentRegistrationRecord>())
            .FirstOrDefault(x => string.Equals(x.OrganizationVoterId, organizationVoterId, StringComparison.OrdinalIgnoreCase));
        if (commitment is null)
        {
            return null;
        }

        return VerificationCanonicalHash.ComputeSha256UpperHex(
            $"{request.Election.ElectionId}|{organizationVoterId}|{commitment.CommitmentHash}");
    }

    private static string ComputeOrganizationVoterIdSetHash(IEnumerable<string> organizationVoterIds)
    {
        var payload = string.Join(
            '\n',
            organizationVoterIds.OrderBy(x => x, StringComparer.Ordinal).Select(x => x.Trim()));

        return VerificationCanonicalHash.ComputeSha256UpperHex(payload);
    }

    private static string ComputeCommitmentRoot(IReadOnlyList<ElectionCommitmentRegistrationRecord> commitments)
    {
        var payload = string.Join(
            '\n',
            commitments
                .OrderBy(x => x.CommitmentHash, StringComparer.Ordinal)
                .Select(x => x.CommitmentHash));

        return VerificationCanonicalHash.ComputeSha256UpperHex(payload);
    }

    private static RestrictedRosterCheckoffArtifactRecord BuildRestrictedRosterCheckoff(
        ElectionVerificationPackageExportRequest request)
    {
        var participation = request.ParticipationRecords.ToDictionary(
            x => x.OrganizationVoterId,
            StringComparer.OrdinalIgnoreCase);
        return new RestrictedRosterCheckoffArtifactRecord(
            request.Election.ElectionId.ToString(),
            request.RosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                .Select(x => new RestrictedRosterCheckoffEntryRecord(
                    x.OrganizationVoterId,
                    x.LinkedActorPublicAddress,
                    x.VotingRightStatus.ToString(),
                    participation.GetValueOrDefault(x.OrganizationVoterId)?.ParticipationStatus.ToString()))
                .ToArray());
    }

    private static ElectionSp06ControlProfileArtifactRecord BuildSp06ControlProfile(
        ElectionVerificationPackageExportRequest request)
    {
        var expected = IsSp06EvidenceExpected(request);
        return new ElectionSp06ControlProfileArtifactRecord(
            request.Election.ElectionId.ToString(),
            expected
                ? ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1
                : "not_applicable",
            expected
                ? ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version
                : "not_applicable",
            request.Election.SelectedProfileId,
            TrusteeCount: expected ? 5 : ResolveSessionTrusteeCount(request),
            TrusteeThreshold: expected ? 3 : ResolveSessionThreshold(request),
            HighAssuranceClaimed: expected,
            AllowedCustodyModes: expected
                ? ElectionSp06ProfileIds.HighAssuranceV1AllowedCustodyModes.ToArray()
                : [],
            PublicPrivacyBoundary:
            [
                "no_trustee_account_id",
                "no_trustee_person_ref",
                "no_custody_domain_ref",
                "no_admin_domain_ref",
                "no_raw_trustee_share",
                "no_private_key",
            ]);
    }

    private static ElectionSp06TrusteeControlSummaryArtifactRecord BuildSp06ControlSummary(
        ElectionVerificationPackageExportRequest request)
    {
        var expected = IsSp06EvidenceExpected(request);
        var controlDomains = request.TrusteeControlDomainRecords ?? Array.Empty<ElectionTrusteeControlDomainRecord>();
        var releaseArtifacts = request.TrusteeReleaseArtifacts ?? BuildSp06ReleaseArtifactsFromFinalizationShares(request);
        var trusteeRows = BuildSp06TrusteeRows(request, controlDomains, releaseArtifacts);
        var finalizationSession = request.FinalizationSessions
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var acceptedReleaseCount = releaseArtifacts.Count(x => x.Status == ElectionTrusteeReleaseArtifactStatus.Accepted);
        var missingReleaseCount = Math.Max(0, trusteeRows.Count(x => x.ReleaseArtifactStatus == ElectionTrusteeReleaseArtifactStatus.Missing));
        var rejectedReleaseCount = releaseArtifacts.Count(x => x.Status == ElectionTrusteeReleaseArtifactStatus.Rejected);
        var blockers = BuildSp06ReadinessBlockers(request, controlDomains, releaseArtifacts);

        return new ElectionSp06TrusteeControlSummaryArtifactRecord(
            request.Election.ElectionId.ToString(),
            expected
                ? ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1
                : "not_applicable",
            expected
                ? ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version
                : "not_applicable",
            request.Election.SelectedProfileId,
            TrusteeCount: expected ? 5 : ResolveSessionTrusteeCount(request),
            TrusteeThreshold: expected ? 3 : ResolveSessionThreshold(request),
            AcceptedBeforeOpenCount: controlDomains.Count(x => x.AcceptedBeforeOpen),
            CompleteEvidenceCount: controlDomains.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Accepted),
            MissingEvidenceCount: expected ? Math.Max(0, 5 - controlDomains.Count) : 0,
            StaleEvidenceCount: controlDomains.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Stale),
            IncompatibleEvidenceCount: controlDomains.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Incompatible),
            acceptedReleaseCount,
            missingReleaseCount,
            rejectedReleaseCount,
            FinalEncryptedTallyHash: finalizationSession is null
                ? "not_available"
                : VerificationCanonicalHash.ToLowerHex(finalizationSession.FinalEncryptedTallyHash),
            TargetTallyId: finalizationSession?.TargetTallyId ?? "not_available",
            ExecutorSessionPublicKeyHash: ResolveExecutorSessionPublicKeyHash(releaseArtifacts),
            ExecutorKeyAlgorithm: request.FinalizationShares
                .Where(x => !string.IsNullOrWhiteSpace(x.ExecutorKeyAlgorithm))
                .OrderByDescending(x => x.SubmittedAt)
                .Select(x => x.ExecutorKeyAlgorithm)
                .FirstOrDefault(),
            trusteeRows,
            blockers,
            PublicPrivacyBoundary:
            [
                "no_trustee_account_id",
                "no_trustee_person_ref",
                "no_custody_domain_ref",
                "no_admin_domain_ref",
                "no_raw_trustee_share",
                "no_private_key",
            ]);
    }

    private static ElectionSp06VerifierOutputArtifactRecord BuildSp06VerifierOutput(
        ElectionVerificationPackageExportRequest request,
        DateTime verifiedAt)
    {
        var summary = BuildSp06ControlSummary(request);
        var expected = IsSp06EvidenceExpected(request);
        var status = expected && summary.CompleteEvidenceCount < 5
            ? VerificationCheckStatus.Fail
            : VerificationCheckStatus.Pass;
        var resultCode = status == VerificationCheckStatus.Pass
            ? VerificationResultCodes.TrusteeControlDomainEvidenceValid
            : VerificationResultCodes.TrusteeAcceptanceIncomplete;

        return new ElectionSp06VerifierOutputArtifactRecord(
            request.Election.ElectionId.ToString(),
            request.VerifierProfileId,
            verifiedAt,
            [
                new VerifierCheckResultRecord(
                    "CTRL-000",
                    expected ? VerificationCheckStatus.Pass : VerificationCheckStatus.NotApplicable,
                    expected
                        ? VerificationResultCodes.TrusteeControlDomainEvidenceValid
                        : VerificationResultCodes.PackageStructureValid,
                    expected
                        ? "SP-06 control-domain profile is declared for the package."
                        : "SP-06 control-domain evidence is not expected for this package.",
                    new Dictionary<string, string>
                    {
                        ["control_domain_profile_id"] = summary.ControlDomainProfileId,
                    }),
                new VerifierCheckResultRecord(
                    "CTRL-002",
                    status,
                    resultCode,
                    status == VerificationCheckStatus.Pass
                        ? "Required trustee control-domain evidence is complete."
                        : "Required trustee control-domain evidence is incomplete.",
                    new Dictionary<string, string>
                    {
                        ["complete_evidence_count"] = summary.CompleteEvidenceCount.ToString(),
                        ["trustee_count"] = summary.TrusteeCount.ToString(),
                    }),
            ]);
    }

    private static ElectionSp06RestrictedControlDomainEvidenceArtifactRecord BuildRestrictedSp06ControlDomains(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.TrusteeControlDomainRecords ?? Array.Empty<ElectionTrusteeControlDomainRecord>());

    private static ElectionSp06RestrictedReleaseArtifactEvidenceRecord BuildRestrictedSp06ReleaseArtifacts(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.TrusteeReleaseArtifacts ?? BuildSp06ReleaseArtifactsFromFinalizationShares(request));

    private static IReadOnlyList<ElectionSp06ReadinessBlockerArtifactRecord> BuildSp06ReadinessBlockers(
        ElectionVerificationPackageExportRequest request,
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains,
        IReadOnlyList<ElectionTrusteeReleaseArtifactRecord> releaseArtifacts)
    {
        if (!IsSp06EvidenceExpected(request))
        {
            return Array.Empty<ElectionSp06ReadinessBlockerArtifactRecord>();
        }

        var blockers = new List<ElectionSp06ReadinessBlockerArtifactRecord>();
        if (!string.Equals(request.Election.SelectedProfileId, ElectionSelectableProfileCatalog.TrusteeProductionProfileId, StringComparison.Ordinal))
        {
            blockers.Add(new ElectionSp06ReadinessBlockerArtifactRecord(
                "trustee_threshold_profile_mismatch",
                "High-assurance trustee control requires dkg-prod-3of5.",
                TrusteeId: null,
                BlocksOpen: true,
                BlocksFinalization: true));
        }

        if (controlDomains.Count < 5)
        {
            blockers.Add(new ElectionSp06ReadinessBlockerArtifactRecord(
                "control_domain_evidence_missing",
                "High-assurance trustee control requires five accepted control-domain records.",
                TrusteeId: null,
                BlocksOpen: true,
                BlocksFinalization: false));
        }

        if (controlDomains.Any(x => !x.AcceptedBeforeOpen))
        {
            blockers.Add(new ElectionSp06ReadinessBlockerArtifactRecord(
                "trustee_acceptance_incomplete",
                "Every high-assurance trustee must accept before election open.",
                TrusteeId: null,
                BlocksOpen: true,
                BlocksFinalization: false));
        }

        if (releaseArtifacts.Count(x => x.Status == ElectionTrusteeReleaseArtifactStatus.Accepted) < 3)
        {
            blockers.Add(new ElectionSp06ReadinessBlockerArtifactRecord(
                "trustee_release_threshold_not_met",
                "Fewer than three accepted trustee release artifacts are available.",
                TrusteeId: null,
                BlocksOpen: false,
                BlocksFinalization: true));
        }

        return blockers;
    }

    private static IReadOnlyList<ElectionSp06TrusteeControlSummaryRowArtifactRecord> BuildSp06TrusteeRows(
        ElectionVerificationPackageExportRequest request,
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains,
        IReadOnlyList<ElectionTrusteeReleaseArtifactRecord> releaseArtifacts)
    {
        var domainByTrusteeId = controlDomains.ToDictionary(x => x.TrusteeId, StringComparer.Ordinal);
        var releaseByTrusteeId = releaseArtifacts
            .GroupBy(x => x.TrusteeId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderByDescending(y => y.Status == ElectionTrusteeReleaseArtifactStatus.Accepted)
                    .ThenByDescending(y => y.RecordedAt)
                    .First(),
                StringComparer.Ordinal);
        var trusteeIds = controlDomains
            .Select(x => x.TrusteeId)
            .Concat(releaseArtifacts.Select(x => x.TrusteeId))
            .Concat(BuildSessionTrusteeIds(request))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (trusteeIds.Length == 0 && IsSp06EvidenceExpected(request))
        {
            trusteeIds = Enumerable.Range(1, 5).Select(x => $"trustee-{x:00}").ToArray();
        }

        return trusteeIds
            .Select(trusteeId =>
            {
                domainByTrusteeId.TryGetValue(trusteeId, out var domain);
                releaseByTrusteeId.TryGetValue(trusteeId, out var release);
                return new ElectionSp06TrusteeControlSummaryRowArtifactRecord(
                    trusteeId,
                    domain is null ? trusteeId : BuildTrusteePseudonym(domain.TrusteeAccountId),
                    domain?.EvidenceStatus ?? ElectionTrusteeControlDomainEvidenceStatus.Missing,
                    release?.Status ?? ElectionTrusteeReleaseArtifactStatus.Missing,
                    domain?.AcceptedBeforeOpen ?? false,
                    domain?.AcceptedAt,
                    domain?.PublicKeyCommitmentHash,
                    domain is null ? null : VerificationCanonicalHash.ComputeSha256UpperHex(domain.CustodyDomainRefHash),
                    domain is null ? null : VerificationCanonicalHash.ComputeSha256UpperHex(domain.AdminDomainRefHash),
                    release?.ArtifactHash,
                    release?.ShareMaterialHash,
                    domain?.EvidenceFailureCode ?? release?.FailureCode);
            })
            .ToArray();
    }

    private static IReadOnlyList<ElectionTrusteeReleaseArtifactRecord> BuildSp06ReleaseArtifactsFromFinalizationShares(
        ElectionVerificationPackageExportRequest request)
    {
        var latestSession = request.FinalizationSessions
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestSession is null)
        {
            return Array.Empty<ElectionTrusteeReleaseArtifactRecord>();
        }

        var records = request.FinalizationShares
            .Where(x => x.FinalizationSessionId == latestSession.Id)
            .Select(x => new ElectionTrusteeReleaseArtifactRecord(
                x.Id,
                x.ElectionId,
                x.FinalizationSessionId,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                latestSession.CeremonySnapshot?.ProfileId ?? request.Election.SelectedProfileId,
                BuildTrusteeId(x.TrusteeUserAddress),
                BuildTrusteePseudonym(x.TrusteeUserAddress),
                x.Status == ElectionFinalizationShareStatus.Accepted
                    ? ElectionTrusteeReleaseArtifactStatus.Accepted
                    : ElectionTrusteeReleaseArtifactStatus.Rejected,
                x.ShareMaterialHash,
                VerificationCanonicalHash.ComputeSha256UpperHex($"{x.Id:N}|{x.ShareMaterialHash}|{x.Status}"),
                x.FailureCode,
                x.FailureReason,
                x.ClaimedCloseArtifactId,
                x.ClaimedAcceptedBallotSetHash,
                x.ClaimedFinalEncryptedTallyHash,
                x.ClaimedTargetTallyId,
                x.ClaimedCeremonyVersionId,
                x.ClaimedTallyPublicKeyFingerprint,
                null,
                x.ExecutorKeyAlgorithm,
                x.SubmittedAt))
            .ToList();

        var acceptedTrusteeIds = records
            .Select(x => x.TrusteeId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missingTrustee in latestSession.EligibleTrustees
            .Where(x => !acceptedTrusteeIds.Contains(BuildTrusteeId(x.TrusteeUserAddress))))
        {
            records.Add(new ElectionTrusteeReleaseArtifactRecord(
                BuildDeterministicGuid(
                    "sp06-missing-release",
                    $"{latestSession.ElectionId}|{latestSession.Id:N}|{missingTrustee.TrusteeUserAddress}"),
                latestSession.ElectionId,
                latestSession.Id,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                latestSession.CeremonySnapshot?.ProfileId ?? request.Election.SelectedProfileId,
                BuildTrusteeId(missingTrustee.TrusteeUserAddress),
                BuildTrusteePseudonym(missingTrustee.TrusteeUserAddress),
                ElectionTrusteeReleaseArtifactStatus.Missing,
                ShareMaterialHash: null,
                ArtifactHash: null,
                FailureCode: "trustee_release_missing",
                FailureReason: "Trustee did not submit a release artifact for the finalization session.",
                latestSession.CloseArtifactId,
                latestSession.AcceptedBallotSetHash,
                latestSession.FinalEncryptedTallyHash,
                latestSession.TargetTallyId,
                latestSession.CeremonySnapshot?.CeremonyVersionId,
                latestSession.CeremonySnapshot?.TallyPublicKeyFingerprint,
                ExecutorSessionPublicKeyHash: null,
                ExecutorKeyAlgorithm: null,
                latestSession.CompletedAt ?? latestSession.CreatedAt));
        }

        return records;
    }

    private static IReadOnlyList<string> BuildSessionTrusteeIds(ElectionVerificationPackageExportRequest request) =>
        request.FinalizationSessions
            .SelectMany(x => x.EligibleTrustees)
            .Select(x => BuildTrusteeId(x.TrusteeUserAddress))
            .ToArray();

    private static bool IsSp06EvidenceExpected(ElectionVerificationPackageExportRequest request) =>
        string.Equals(
            request.Election.ControlDomainProfileId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            StringComparison.Ordinal) ||
        string.Equals(request.VerifierProfileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);

    private static int ResolveSessionTrusteeCount(ElectionVerificationPackageExportRequest request) =>
        request.FinalizationSessions
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .Select(x => x.EligibleTrustees.Count)
            .FirstOrDefault();

    private static int ResolveSessionThreshold(ElectionVerificationPackageExportRequest request) =>
        request.FinalizationSessions
            .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
            .Select(x => x.RequiredShareCount)
            .FirstOrDefault();

    private static string? ResolveExecutorSessionPublicKeyHash(
        IReadOnlyList<ElectionTrusteeReleaseArtifactRecord> releaseArtifacts) =>
        releaseArtifacts
            .Where(x => !string.IsNullOrWhiteSpace(x.ExecutorSessionPublicKeyHash))
            .OrderByDescending(x => x.RecordedAt)
            .Select(x => x.ExecutorSessionPublicKeyHash)
            .FirstOrDefault();

    private static string BuildTrusteeId(string trusteeUserAddress) =>
        $"trustee-{VerificationCanonicalHash.ComputeSha256UpperHex(trusteeUserAddress)[..12].ToLowerInvariant()}";

    private static string BuildTrusteePseudonym(string trusteeUserAddress) =>
        $"trustee-ref-{VerificationCanonicalHash.ComputeSha256UpperHex(trusteeUserAddress)[..12].ToLowerInvariant()}";

    private static Guid BuildDeterministicGuid(string scope, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{value}".Trim().ToLowerInvariant()));
        return new Guid(bytes[..16]);
    }
}

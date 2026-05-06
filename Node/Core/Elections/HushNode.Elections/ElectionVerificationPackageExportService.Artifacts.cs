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
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.FinalizationSessions.Count,
            request.FinalizationShares.Count(x => x.IsAccepted),
            request.FinalizationShares
                .Where(x => x.IsAccepted)
                .OrderBy(x => x.ShareIndex)
                .Select(x => new TrusteeReleaseShareEvidenceRecord(
                    x.TrusteeUserAddress,
                    x.ShareIndex,
                    x.ShareMaterialHash,
                    x.Status.ToString()))
                .ToArray());

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
}

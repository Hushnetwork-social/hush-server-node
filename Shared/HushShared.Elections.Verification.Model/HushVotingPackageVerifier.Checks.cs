using System.Text;
using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckManifestAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        var results = new List<VerifierCheckResultRecord>();

        foreach (var entry in manifest.Entries)
        {
            var fullPath = ResolvePackagePath(packagePath, entry.Path);
            if (!File.Exists(fullPath))
            {
                results.Add(CreateResult(
                    "VFY-MAN-001",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"Manifest entry '{entry.Path}' is missing."));
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            var actual = VerificationCanonicalHash.ComputeManifestFileSha256(bytes);
            if (!string.Equals(actual, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(CreateResult(
                    "VFY-MAN-002",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestArtifactHashMismatch,
                    $"Manifest entry '{entry.Path}' hash does not match exported bytes.",
                    new Dictionary<string, string>
                    {
                        ["expected"] = entry.Sha256Hash,
                        ["actual"] = actual,
                    }));
            }
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "VFY-MAN-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageManifestValid,
                "All manifest entries exist and match their SHA-256 hashes."));
        }

        return results;
    }

    private static VerifierCheckResultRecord CheckProfile(
        string requestedProfile,
        VerifierInputManifestRecord inputManifest,
        VerifierProfileRecord profile)
    {
        if (!VerificationProfileIds.All.Contains(requestedProfile) ||
            !string.Equals(requestedProfile, inputManifest.VerifierProfileId, StringComparison.Ordinal) ||
            !string.Equals(requestedProfile, profile.ProfileId, StringComparison.Ordinal))
        {
            return CreateResult(
                "VFY-PROFILE-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.VerifierProfilePackageMismatch,
                "Requested verifier profile does not match the package profile.");
        }

        return CreateResult(
            "VFY-PROFILE-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Verifier profile matches the package input manifest.");
    }

    private static async Task<VerifierCheckResultRecord> CheckElectionRecordAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        VerifierInputManifestRecord inputManifest,
        ElectionRecordReferenceRecord electionRecord,
        CancellationToken cancellationToken)
    {
        var electionIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["audit_manifest"] = manifest.ElectionId,
            ["verifier_input_manifest"] = inputManifest.ElectionId,
            ["election_record"] = electionRecord.ElectionId,
        };

        var accepted = await ReadJsonAsync<AcceptedBallotSetArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.AcceptedBallotSet,
            cancellationToken);
        electionIds["accepted_ballot_set"] = accepted.ElectionId;

        var published = await ReadJsonAsync<PublishedBallotStreamArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.PublishedBallotStream,
            cancellationToken);
        electionIds["published_ballot_stream"] = published.ElectionId;

        var tallyReplay = await ReadJsonAsync<TallyReplayArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.TallyReplay,
            cancellationToken);
        electionIds["tally_replay"] = tallyReplay.ElectionId;

        var trusteeRelease = await ReadJsonAsync<TrusteeReleaseEvidenceArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.TrusteeReleaseEvidence,
            cancellationToken);
        electionIds["trustee_release_evidence"] = trusteeRelease.ElectionId;

        var resultBinding = await ReadJsonAsync<ResultBindingArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.ResultBinding,
            cancellationToken);
        electionIds["result_binding"] = resultBinding.ElectionId;

        var sp04Evidence = await ReadJsonAsync<ElectionSp04EvidenceRecord>(
            packagePath,
            VerificationPackageFileNames.Sp04Evidence,
            cancellationToken);
        electionIds["sp04_evidence"] = sp04Evidence.ElectionId.ToString();

        var sp06Profile = await ReadJsonAsync<ElectionSp06ControlProfileArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp06TrusteeControlProfile,
            cancellationToken);
        electionIds["sp06_trustee_control_profile"] = sp06Profile.ElectionId;

        var sp06Summary = await ReadJsonAsync<ElectionSp06TrusteeControlSummaryArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp06TrusteeControlSummary,
            cancellationToken);
        electionIds["sp06_trustee_control_summary"] = sp06Summary.ElectionId;

        if (manifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            var restricted = await ReadJsonAsync<RestrictedRosterCheckoffArtifactRecord>(
                packagePath,
                VerificationPackageFileNames.RestrictedRosterCheckoff,
                cancellationToken);
            electionIds["restricted_roster_checkoff"] = restricted.ElectionId;

            var restrictedCeremonies = await ReadJsonAsync<ElectionSp04RestrictedCeremonyRecord[]>(
                packagePath,
                VerificationPackageFileNames.RestrictedSp04CeremonyRecords,
                cancellationToken);
            foreach (var group in restrictedCeremonies.GroupBy(x => x.ElectionId.ToString(), StringComparer.Ordinal))
            {
                electionIds[$"restricted_sp04_ceremony:{group.Key}"] = group.Key;
            }

            var restrictedSp06 = await ReadJsonAsync<ElectionSp06RestrictedControlDomainEvidenceArtifactRecord>(
                packagePath,
                VerificationPackageFileNames.RestrictedSp06TrusteeControlDomains,
                cancellationToken);
            electionIds["restricted_sp06_trustee_control_domains"] = restrictedSp06.ElectionId;
        }

        var distinctElectionIds = electionIds.Values
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctElectionIds.Length > 1)
        {
            return CreateResult(
                "VFY-ELECTION-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ElectionIdMismatch,
                "Election id differs across package root files or verifier artifacts.",
                electionIds);
        }

        if (!string.Equals(electionRecord.LifecycleState, "Finalized", StringComparison.Ordinal))
        {
            return CreateResult(
                "VFY-ELECTION-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ElectionNotFinalized,
                "Election record lifecycle state must be Finalized for verifier replay.",
                new Dictionary<string, string>
                {
                    ["lifecycle_state"] = electionRecord.LifecycleState,
                });
        }

        return CreateResult(
            "VFY-ELECTION-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Election id and finalized lifecycle are consistent across package files.");
    }

    private static async Task<VerifierCheckResultRecord> CheckAcceptedBallotsAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var accepted = await ReadJsonAsync<AcceptedBallotSetArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.AcceptedBallotSet,
            cancellationToken);
        var duplicate = accepted.AcceptedBallots
            .GroupBy(x => x.BallotNullifier, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
        {
            return CreateResult(
                "VFY-ACCEPTED-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AcceptedBallotDuplicateNullifier,
                $"Duplicate ballot nullifier '{duplicate.Key}' found.");
        }

        var records = accepted.AcceptedBallots
            .Select(x => new HushShared.Elections.Model.ElectionAcceptedBallotRecord(
                Guid.NewGuid(),
                new HushShared.Elections.Model.ElectionId(Guid.Parse(accepted.ElectionId)),
                x.EncryptedBallotPackage,
                x.ProofBundle,
                x.BallotNullifier,
                DateTime.UnixEpoch))
            .ToArray();
        var actualHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(records));

        if (!string.Equals(actualHash, accepted.AcceptedBallotInventoryHash, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                "VFY-ACCEPTED-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AcceptedBallotInventoryHashMismatch,
                "Accepted ballot inventory hash does not match the accepted ballot set artifact.");
        }

        return CreateResult(
            "VFY-ACCEPTED-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Accepted ballot inventory hash and nullifier uniqueness passed.");
    }

    private static async Task<VerifierCheckResultRecord> CheckPublishedBallotsAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var published = await ReadJsonAsync<PublishedBallotStreamArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.PublishedBallotStream,
            cancellationToken);
        var sequences = published.PublishedBallots
            .Select(x => x.PublicationSequence)
            .Order()
            .ToArray();
        var expectedSequences = Enumerable.Range(1, sequences.Length).Select(x => (long)x).ToArray();

        if (!sequences.SequenceEqual(expectedSequences))
        {
            return CreateResult(
                "VFY-PUBLISHED-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublishedBallotSequenceInvalid,
                "Published ballot sequence must be contiguous and start at 1.");
        }

        var records = published.PublishedBallots
            .Select(x => new HushShared.Elections.Model.ElectionPublishedBallotRecord(
                Guid.NewGuid(),
                new HushShared.Elections.Model.ElectionId(Guid.Parse(published.ElectionId)),
                x.PublicationSequence,
                x.EncryptedBallotPackage,
                x.ProofBundle,
                DateTime.UnixEpoch,
                SourceBlockHeight: null,
                SourceBlockId: null))
            .ToArray();
        var actualHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputePublishedBallotStreamHash(records));

        if (!string.Equals(actualHash, published.PublishedBallotStreamHash, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                "VFY-PUBLISHED-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublishedBallotStreamHashMismatch,
                "Published ballot stream hash does not match the published ballot stream artifact.");
        }

        return CreateResult(
            "VFY-PUBLISHED-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Published ballot stream hash and sequence checks passed.");
    }

    private static async Task<VerifierCheckResultRecord> CheckFuturePublicationProofAsync(
        string packagePath,
        string profileId,
        CancellationToken cancellationToken)
    {
        var tallyReplay = await ReadJsonAsync<TallyReplayArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.TallyReplay,
            cancellationToken);

        if (!string.Equals(tallyReplay.ResultCode, VerificationResultCodes.PublicationProofEvidencePending, StringComparison.Ordinal))
        {
            return CreateResult(
                "VFY-SP07-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageStructureValid,
                "Publication proof evidence is present for the selected profile.");
        }

        var isHighAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        return CreateResult(
            "VFY-SP07-001",
            isHighAssurance ? VerificationCheckStatus.Fail : VerificationCheckStatus.Warn,
            VerificationResultCodes.PublicationProofEvidencePending,
            isHighAssurance
                ? "High-assurance profile requires SP-07 publication proof evidence."
                : "SP-07 publication proof evidence is pending a later protocol package revision.");
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckSp04EvidenceAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        string profileId,
        CancellationToken cancellationToken)
    {
        var requiredFiles = new[]
        {
            VerificationPackageFileNames.Sp04Evidence,
            VerificationPackageFileNames.Sp04ReceiptCommitments,
        };
        var missingFiles = requiredFiles
            .Where(x => !File.Exists(ResolvePackagePath(packagePath, x)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    "VFY-SP04-070",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.ChallengeSpoilEvidencePending,
                    "SP-04 public evidence files are missing.",
                    missingFiles.ToDictionary(x => x, x => "missing", StringComparer.Ordinal)),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        var evidence = await ReadJsonAsync<ElectionSp04EvidenceRecord>(
            packagePath,
            VerificationPackageFileNames.Sp04Evidence,
            cancellationToken);
        var receipts = await ReadJsonAsync<ElectionSp04ReceiptCommitmentRecord[]>(
            packagePath,
            VerificationPackageFileNames.Sp04ReceiptCommitments,
            cancellationToken);
        var accepted = await ReadJsonAsync<AcceptedBallotSetArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.AcceptedBallotSet,
            cancellationToken);

        var expectedReceiptSetHash = ComputeReceiptCommitmentSetHash(receipts);
        if (!string.Equals(expectedReceiptSetHash, evidence.ReceiptCommitmentSetHash, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "VFY-SP04-071",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ChallengeSpoilReceiptMismatch,
                "SP-04 receipt commitment set hash does not match the receipt commitment artifact.",
                new Dictionary<string, string>
                {
                    ["expected"] = evidence.ReceiptCommitmentSetHash,
                    ["actual"] = expectedReceiptSetHash,
                }));
        }

        if (evidence.AcceptedBoundReceiptCount != receipts.Length ||
            evidence.AcceptedBoundReceiptCount != accepted.AcceptedBallotCount)
        {
            results.Add(CreateResult(
                "VFY-SP04-072",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ChallengeSpoilCountMismatch,
                "SP-04 accepted receipt count must match the accepted ballot count.",
                new Dictionary<string, string>
                {
                    ["accepted_ballots"] = accepted.AcceptedBallotCount.ToString(),
                    ["receipt_commitments"] = receipts.Length.ToString(),
                    ["evidence_count"] = evidence.AcceptedBoundReceiptCount.ToString(),
                }));
        }

        if (evidence.PreparedPackageCount < evidence.SpoiledPackageCount + evidence.AcceptedBoundReceiptCount)
        {
            results.Add(CreateResult(
                "VFY-SP04-073",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ChallengeSpoilCountMismatch,
                "SP-04 prepared package count must cover spoiled packages and accepted final casts."));
        }

        if (evidence.BallotDefinitionVersion <= 0 || evidence.BallotDefinitionHash.Length == 0)
        {
            results.Add(CreateResult(
                "VFY-SP04-074",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ChallengeSpoilBallotDefinitionMismatch,
                "SP-04 ballot definition seal is missing or invalid."));
        }

        foreach (var acceptedBallot in accepted.AcceptedBallots)
        {
            if (!acceptedBallot.PreparedBallotId.HasValue ||
                string.IsNullOrWhiteSpace(acceptedBallot.PreparedBallotHash) ||
                string.IsNullOrWhiteSpace(acceptedBallot.ReceiptCommitment) ||
                acceptedBallot.BallotDefinitionVersion != evidence.BallotDefinitionVersion ||
                !BytesEqual(acceptedBallot.BallotDefinitionHash, evidence.BallotDefinitionHash) ||
                !receipts.Any(x =>
                    x.PreparedBallotId == acceptedBallot.PreparedBallotId.Value &&
                    string.Equals(x.PreparedBallotHash, acceptedBallot.PreparedBallotHash, StringComparison.Ordinal) &&
                    string.Equals(x.ReceiptCommitment, acceptedBallot.ReceiptCommitment, StringComparison.Ordinal)))
            {
                results.Add(CreateResult(
                    "VFY-SP04-075",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.ChallengeSpoilReceiptMismatch,
                    "Accepted ballot SP-04 binding is missing or does not match the receipt commitments."));
                break;
            }
        }

        if (manifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            results.AddRange(await CheckRestrictedSp04EvidenceAsync(
                packagePath,
                evidence,
                receipts,
                cancellationToken));
        }
        else if (string.Equals(profileId, VerificationProfileIds.RestrictedOwnerAuditorV1, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "VFY-SP04-076",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.RestrictedEvidenceMissing,
                "Restricted owner/auditor profile requires restricted SP-04 evidence files."));
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "VFY-SP04-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.ChallengeSpoilEvidenceValid,
                "SP-04 ballot seal, prepared counts, spoil counts, and receipt commitments passed."));
        }

        return results;
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckRestrictedSp04EvidenceAsync(
        string packagePath,
        ElectionSp04EvidenceRecord evidence,
        IReadOnlyList<ElectionSp04ReceiptCommitmentRecord> receipts,
        CancellationToken cancellationToken)
    {
        var requiredFiles = new[]
        {
            VerificationPackageFileNames.RestrictedSp04CeremonyRecords,
            VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments,
            VerificationPackageFileNames.RestrictedSp04SpoilMarkers,
        };
        var missingFiles = requiredFiles
            .Where(x => !File.Exists(ResolvePackagePath(packagePath, x)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    "VFY-SP04-077",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.RestrictedEvidenceMissing,
                    "Restricted SP-04 evidence files are missing.",
                    missingFiles.ToDictionary(x => x, x => "missing", StringComparer.Ordinal)),
            ];
        }

        var ceremonies = await ReadJsonAsync<ElectionSp04RestrictedCeremonyRecord[]>(
            packagePath,
            VerificationPackageFileNames.RestrictedSp04CeremonyRecords,
            cancellationToken);
        var prepared = await ReadJsonAsync<ElectionSp04RestrictedPreparedBallotRecord[]>(
            packagePath,
            VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments,
            cancellationToken);
        var spoiled = await ReadJsonAsync<ElectionSp04RestrictedSpoilMarkerRecord[]>(
            packagePath,
            VerificationPackageFileNames.RestrictedSp04SpoilMarkers,
            cancellationToken);

        if (prepared.Length != evidence.PreparedPackageCount ||
            spoiled.Length != evidence.SpoiledPackageCount ||
            ceremonies.Sum(x => x.SpoiledPackageCount) != evidence.SpoiledPackageCount)
        {
            return
            [
                CreateResult(
                    "VFY-SP04-078",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.ChallengeSpoilRestrictedEvidenceMismatch,
                    "Restricted SP-04 counts do not match public SP-04 evidence."),
            ];
        }

        var preparedById = prepared.ToDictionary(x => x.PreparedBallotId);
        var missingReceiptPrepared = receipts
            .Where(x =>
                !preparedById.TryGetValue(x.PreparedBallotId, out var preparedRecord) ||
                preparedRecord.State != HushShared.Elections.Model.ElectionPreparedBallotState.Cast ||
                preparedRecord.AcceptedBallotId != x.AcceptedBallotId ||
                preparedRecord.BallotDefinitionVersion != evidence.BallotDefinitionVersion ||
                !BytesEqual(preparedRecord.BallotDefinitionHash, evidence.BallotDefinitionHash))
            .ToArray();
        if (missingReceiptPrepared.Length > 0)
        {
            return
            [
                CreateResult(
                    "VFY-SP04-079",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.ChallengeSpoilRestrictedEvidenceMismatch,
                    "Restricted SP-04 prepared records do not support every public receipt commitment."),
            ];
        }

        return Array.Empty<VerifierCheckResultRecord>();
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckSp05EvidenceAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        string profileId,
        CancellationToken cancellationToken)
    {
        var requiredFiles = new[]
        {
            VerificationPackageFileNames.Sp05EligibilityPolicy,
            VerificationPackageFileNames.Sp05EligibilitySummary,
            VerificationPackageFileNames.Sp05EligibilityVerifierOutput,
        };
        var missingFiles = requiredFiles
            .Where(x => !File.Exists(ResolvePackagePath(packagePath, x)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    "ELI-002",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.EligibilityPolicyMissing,
                    "SP-05 public eligibility files are missing.",
                    missingFiles.ToDictionary(x => x, x => "missing", StringComparer.Ordinal)),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        var policy = await ReadJsonAsync<ElectionSp05PolicyArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp05EligibilityPolicy,
            cancellationToken);
        var summary = await ReadJsonAsync<ElectionSp05SummaryArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp05EligibilitySummary,
            cancellationToken);
        var verifierOutput = await ReadJsonAsync<ElectionSp05VerifierOutputArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp05EligibilityVerifierOutput,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(policy.EligibilityPolicyId) ||
            string.IsNullOrWhiteSpace(policy.RosterCanonicalizationVersionHash) ||
            string.IsNullOrWhiteSpace(policy.CommitmentSchemeVersionHash) ||
            string.IsNullOrWhiteSpace(policy.NullifierSchemeVersionHash))
        {
            results.Add(CreateResult(
                "ELI-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.EligibilityPolicyMissing,
                "SP-05 eligibility policy is incomplete."));
        }

        if (summary.RosteredCount < 0 ||
            summary.LinkedCount < 0 ||
            summary.ActiveDenominatorCount < 0 ||
            summary.CommitmentCount < 0 ||
            summary.CountedParticipationCount < 0 ||
            summary.BlankCount < 0 ||
            summary.DidNotVoteCount < 0 ||
            summary.LinkedCount > summary.RosteredCount ||
            summary.ActiveDenominatorCount > summary.RosteredCount ||
            summary.CountedParticipationCount + summary.BlankCount + summary.DidNotVoteCount != summary.ActiveDenominatorCount)
        {
            results.Add(CreateResult(
                "ELI-012",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.EligibilityCountReconciliationMismatch,
                "SP-05 public eligibility counts are not internally consistent."));
        }

        if (!string.Equals(verifierOutput.ElectionId, manifest.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(verifierOutput.VerifierProfileId, profileId, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "ELI-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.EligibilitySchemaInvalid,
                "SP-05 verifier output does not match the package election id or verifier profile."));
        }

        var providerReady = policy.ContactCodeProviderReadiness == ElectionContactCodeProviderReadiness.Ready;
        if (!providerReady)
        {
            var highAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
            results.Add(CreateResult(
                "ELI-013",
                highAssurance ? VerificationCheckStatus.Fail : VerificationCheckStatus.Warn,
                VerificationResultCodes.EligibilityDevOnlyVerificationBlocked,
                highAssurance
                    ? "High-assurance profile cannot accept a dev-only or missing contact-code provider."
                    : "Contact-code provider is not marked production-ready; development profile records this as a warning.",
                new Dictionary<string, string>
                {
                    ["contact_code_provider_readiness"] = policy.ContactCodeProviderReadiness.ToString(),
                }));
        }

        if (manifest.PackageView == VerificationPackageView.PublicAnonymous)
        {
            var forbiddenFields = new List<string>();
            foreach (var file in requiredFiles)
            {
                var text = await File.ReadAllTextAsync(ResolvePackagePath(packagePath, file), cancellationToken);
                forbiddenFields.AddRange(VerificationPrivacyBoundary.FindForbiddenSp05PublicFields(CollectJsonPropertyNames(text)));
            }

            var distinctForbiddenFields = forbiddenFields
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctForbiddenFields.Length > 0)
            {
                results.Add(CreateResult(
                    "ELI-011",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.EligibilityPublicPrivacyBoundaryViolation,
                    "SP-05 public eligibility files contain named eligibility fields.",
                    distinctForbiddenFields.ToDictionary(x => x, x => "forbidden", StringComparer.OrdinalIgnoreCase)));
            }
        }
        else if (manifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            results.AddRange(await CheckRestrictedSp05EvidenceAsync(
                packagePath,
                policy,
                summary,
                cancellationToken));
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "ELI-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.EligibilityEvidenceValid,
                "SP-05 eligibility policy, public summary, provider readiness, and evidence boundaries passed."));
        }

        return results;
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckRestrictedSp05EvidenceAsync(
        string packagePath,
        ElectionSp05PolicyArtifactRecord policy,
        ElectionSp05SummaryArtifactRecord summary,
        CancellationToken cancellationToken)
    {
        var requiredFiles = new[]
        {
            VerificationPackageFileNames.RestrictedRosterImportEvidence,
            VerificationPackageFileNames.RestrictedRoster,
            VerificationPackageFileNames.RestrictedLinkingEvidence,
            VerificationPackageFileNames.RestrictedActivationEvents,
            VerificationPackageFileNames.RestrictedCheckoffLedger,
            VerificationPackageFileNames.RestrictedDisputes,
        };
        var missingFiles = requiredFiles
            .Where(x => !File.Exists(ResolvePackagePath(packagePath, x)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    "ELI-007",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.RestrictedEvidenceMissing,
                    "Restricted SP-05 evidence files are missing.",
                    missingFiles.ToDictionary(x => x, x => "missing", StringComparer.Ordinal)),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        var importEvidence = await ReadJsonAsync<ElectionSp05RestrictedRosterImportEvidenceArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.RestrictedRosterImportEvidence,
            cancellationToken);
        var roster = await ReadJsonAsync<ElectionSp05RestrictedRosterArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.RestrictedRoster,
            cancellationToken);
        var checkoff = await ReadJsonAsync<ElectionSp05RestrictedCheckoffLedgerArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.RestrictedCheckoffLedger,
            cancellationToken);

        var rosterHash = ComputeRestrictedRosterCanonicalHash(roster.Entries);
        if (!string.Equals(rosterHash, importEvidence.ImportEvidence.RosterCanonicalHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rosterHash, summary.RosterCanonicalHash, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "ELI-003",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.EligibilityRosterHashMismatch,
                "Restricted roster canonical hash does not reproduce the public SP-05 roster hash.",
                new Dictionary<string, string>
                {
                    ["public_summary_hash"] = summary.RosterCanonicalHash,
                    ["restricted_import_hash"] = importEvidence.ImportEvidence.RosterCanonicalHash,
                    ["recomputed_hash"] = rosterHash,
                }));
        }

        if (policy.ActorLinkMultiplicityPolicy == ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor)
        {
            var duplicateActor = roster.Entries
                .Where(x => !string.IsNullOrWhiteSpace(x.LinkedActorPublicAddress))
                .GroupBy(x => x.LinkedActorPublicAddress!, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicateActor is not null)
            {
                results.Add(CreateResult(
                    "ELI-007",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.EligibilityLinkEvidenceMissing,
                    "Strict actor multiplicity policy was declared, but one actor controls multiple roster entries.",
                    new Dictionary<string, string>
                    {
                        ["linked_actor_hash"] = VerificationCanonicalHash.ComputeSha256UpperHex(duplicateActor.Key),
                        ["linked_entry_count"] = duplicateActor.Count().ToString(),
                    }));
            }
        }

        var countedCheckoffCount = checkoff.Entries.Count(x =>
            x.ParticipationStatus == ElectionParticipationStatus.CountedAsVoted &&
            x.CheckoffConsumptionId.HasValue);
        if (countedCheckoffCount != summary.CountedParticipationCount)
        {
            results.Add(CreateResult(
                "ELI-012",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.EligibilityCountReconciliationMismatch,
                "Restricted checkoff count does not match public counted participation count.",
                new Dictionary<string, string>
                {
                    ["public_counted_participation_count"] = summary.CountedParticipationCount.ToString(),
                    ["restricted_counted_checkoff_count"] = countedCheckoffCount.ToString(),
                }));
        }

        var unsupportedConsumption = checkoff.Entries.FirstOrDefault(x =>
            x.CheckoffConsumptionId.HasValue &&
            string.IsNullOrWhiteSpace(x.AcceptedBallotReference));
        if (unsupportedConsumption is not null)
        {
            results.Add(CreateResult(
                "ELI-010",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.EligibilityConsumptionWithoutAcceptedCast,
                "Restricted checkoff consumption exists without accepted ballot reference evidence."));
        }

        return results;
    }

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

    private static string ComputeRestrictedRosterCanonicalHash(
        IReadOnlyList<ElectionSp05RestrictedRosterEntryArtifactRecord> rosterEntries)
    {
        var lines = rosterEntries
            .OrderBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
            .Select(x => string.Join(
                '\t',
                EncodeCanonicalField(x.OrganizationVoterId),
                x.ContactType.ToString().ToLowerInvariant(),
                EncodeCanonicalField(x.ContactValue),
                x.VotingRightStatus == ElectionVotingRightStatus.Active ? "active" : "inactive"));

        return VerificationCanonicalHash.ComputeSha256LowerHex($"HUSH_ROSTER_CANONICAL_V1\n{string.Join('\n', lines)}");
    }

    private static string EncodeCanonicalField(string? value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckSp06EvidenceAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        string profileId,
        CancellationToken cancellationToken)
    {
        var requiredFiles = new[]
        {
            VerificationPackageFileNames.Sp06TrusteeControlProfile,
            VerificationPackageFileNames.Sp06TrusteeControlSummary,
            VerificationPackageFileNames.Sp06TrusteeVerifierOutput,
        };
        var missingFiles = requiredFiles
            .Where(x => !File.Exists(ResolvePackagePath(packagePath, x)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    "CTRL-000",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.TrusteeControlProfileMissing,
                    "SP-06 public trustee control-domain evidence files are missing.",
                    missingFiles.ToDictionary(x => x, x => "missing", StringComparer.Ordinal)),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        var profile = await ReadJsonAsync<ElectionSp06ControlProfileArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp06TrusteeControlProfile,
            cancellationToken);
        var summary = await ReadJsonAsync<ElectionSp06TrusteeControlSummaryArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp06TrusteeControlSummary,
            cancellationToken);
        var verifierOutput = await ReadJsonAsync<ElectionSp06VerifierOutputArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp06TrusteeVerifierOutput,
            cancellationToken);
        var highAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal) ||
            profile.HighAssuranceClaimed;

        if (!profile.HighAssuranceClaimed && !highAssurance)
        {
            return
            [
                CreateResult(
                    "CTRL-000",
                    VerificationCheckStatus.NotApplicable,
                    VerificationResultCodes.PackageStructureValid,
                    "SP-06 trustee control-domain profile is not claimed by this package."),
            ];
        }

        if (string.IsNullOrWhiteSpace(profile.ControlDomainProfileId) ||
            !string.Equals(
                profile.ControlDomainProfileId,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "CTRL-000",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeControlProfileMissing,
                "High-assurance verification requires the SP-06 control-domain profile id."));
        }

        if (profile.TrusteeCount != 5 ||
            profile.TrusteeThreshold != 3 ||
            !string.Equals(profile.ThresholdProfileId, ElectionSelectableProfileCatalog.TrusteeProductionProfileId, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "CTRL-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeThresholdProfileMismatch,
                "SP-06 high-assurance v1 requires five trustees, threshold three, and dkg-prod-3of5.",
                new Dictionary<string, string>
                {
                    ["trustee_count"] = profile.TrusteeCount.ToString(),
                    ["trustee_threshold"] = profile.TrusteeThreshold.ToString(),
                    ["threshold_profile_id"] = profile.ThresholdProfileId,
                }));
        }

        if (summary.AcceptedBeforeOpenCount < 5 || summary.CompleteEvidenceCount < 5)
        {
            results.Add(CreateResult(
                "CTRL-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeAcceptanceIncomplete,
                "SP-06 requires all five trustee control-domain declarations to be accepted before open.",
                new Dictionary<string, string>
                {
                    ["accepted_before_open_count"] = summary.AcceptedBeforeOpenCount.ToString(),
                    ["complete_evidence_count"] = summary.CompleteEvidenceCount.ToString(),
                }));
        }

        var unsupportedCustodyModes = profile.AllowedCustodyModes
            .Where(x => !ElectionSp06ProfileIds.IsHighAssuranceV1AllowedCustodyMode(x))
            .ToArray();
        if (unsupportedCustodyModes.Length > 0)
        {
            results.Add(CreateResult(
                "CTRL-007",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeCustodyModeUnsupported,
                "SP-06 package declares unsupported custody modes for high assurance.",
                unsupportedCustodyModes.ToDictionary(x => x, x => "unsupported", StringComparer.Ordinal)));
        }

        if (summary.Trustees.Any(x =>
                x.ReleaseArtifactStatus == ElectionTrusteeReleaseArtifactStatus.Rejected &&
                string.Equals(x.FailureCode, "WRONG_TARGET_SHARE", StringComparison.OrdinalIgnoreCase)))
        {
            results.Add(CreateResult(
                "CTRL-008",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeReleaseWrongTarget,
                "At least one trustee release artifact targets a different tally or ceremony."));
        }

        if (summary.AcceptedReleaseArtifactCount < 3)
        {
            results.Add(CreateResult(
                "CTRL-009",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeReleaseThresholdNotMet,
                "SP-06 requires at least three accepted trustee release artifacts.",
                new Dictionary<string, string>
                {
                    ["accepted_release_artifact_count"] = summary.AcceptedReleaseArtifactCount.ToString(),
                }));
        }

        if (!string.Equals(verifierOutput.ElectionId, manifest.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(verifierOutput.VerifierProfileId, profileId, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "CTRL-011",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.TrusteeExceptionPolicyViolation,
                "SP-06 verifier output does not match the package election id or verifier profile."));
        }

        if (manifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            results.AddRange(await CheckRestrictedSp06EvidenceAsync(
                packagePath,
                summary,
                cancellationToken));
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "CTRL-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.TrusteeControlDomainEvidenceValid,
                "SP-06 trustee profile, control-domain counts, custody policy, release target, and threshold checks passed."));
        }

        return results;
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckRestrictedSp06EvidenceAsync(
        string packagePath,
        ElectionSp06TrusteeControlSummaryArtifactRecord summary,
        CancellationToken cancellationToken)
    {
        var requiredFiles = new[]
        {
            VerificationPackageFileNames.RestrictedSp06TrusteeControlDomains,
            VerificationPackageFileNames.RestrictedSp06TrusteeReleaseArtifacts,
        };
        var missingFiles = requiredFiles
            .Where(x => !File.Exists(ResolvePackagePath(packagePath, x)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    "CTRL-002",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.RestrictedEvidenceMissing,
                    "Restricted SP-06 evidence files are missing.",
                    missingFiles.ToDictionary(x => x, x => "missing", StringComparer.Ordinal)),
            ];
        }

        var controlDomains = await ReadJsonAsync<ElectionSp06RestrictedControlDomainEvidenceArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.RestrictedSp06TrusteeControlDomains,
            cancellationToken);

        if (controlDomains.ControlDomains.Count < summary.CompleteEvidenceCount)
        {
            return
            [
                CreateResult(
                    "CTRL-002",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.TrusteeAcceptanceIncomplete,
                    "Restricted SP-06 control-domain records do not support the public complete-evidence count."),
            ];
        }

        return Array.Empty<VerifierCheckResultRecord>();
    }

    private static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return left.SequenceEqual(right);
    }
}

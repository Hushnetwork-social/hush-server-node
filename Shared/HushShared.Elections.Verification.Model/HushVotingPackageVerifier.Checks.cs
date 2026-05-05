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

    private static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return left.SequenceEqual(right);
    }
}

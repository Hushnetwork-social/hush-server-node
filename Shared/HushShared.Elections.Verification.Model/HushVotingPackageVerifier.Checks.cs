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

        if (manifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            var restricted = await ReadJsonAsync<RestrictedRosterCheckoffArtifactRecord>(
                packagePath,
                VerificationPackageFileNames.RestrictedRosterCheckoff,
                cancellationToken);
            electionIds["restricted_roster_checkoff"] = restricted.ElectionId;
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
}

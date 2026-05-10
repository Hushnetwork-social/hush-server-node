using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckSp07PublicationProofEvidenceAsync(
        string packagePath,
        string profileId,
        ElectionRecordReferenceRecord electionRecord,
        ISp07PackagePublicProofVerifier publicProofVerifier,
        CancellationToken cancellationToken)
    {
        var tallyReplay = await ReadJsonAsync<TallyReplayArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.TallyReplay,
            cancellationToken);
        var isHighAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        var transcriptExists = File.Exists(ResolvePackagePath(
            packagePath,
            VerificationPackageFileNames.Sp07PublicationProofTranscript));
        var verifierOutputExists = File.Exists(ResolvePackagePath(
            packagePath,
            VerificationPackageFileNames.Sp07PublicationProofVerifierOutput));
        var deletionReceiptExists = File.Exists(ResolvePackagePath(
            packagePath,
            VerificationPackageFileNames.Sp07WitnessDeletionReceipt));
        var proofClaimed =
            transcriptExists ||
            deletionReceiptExists ||
            !string.Equals(tallyReplay.ResultCode, VerificationResultCodes.PublicationProofEvidencePending, StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(tallyReplay.PublicationProofTranscriptHash) ||
            !string.IsNullOrWhiteSpace(tallyReplay.PublicationProofHash);

        if (!proofClaimed)
        {
            return
            [
                CreateResult(
                    "VFY-SP07-001",
                    isHighAssurance ? VerificationCheckStatus.Fail : VerificationCheckStatus.Warn,
                    VerificationResultCodes.PublicationProofEvidencePending,
                    isHighAssurance
                        ? "High-assurance profile requires SP-07 publication proof evidence."
                        : "SP-07 publication proof evidence is pending a later protocol package revision."),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        if (!transcriptExists)
        {
            results.Add(CreateResult(
                "VFY-SP07-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptMissing,
                "SP-07 publication proof evidence is claimed, but the public transcript artifact is missing."));
        }

        if (!verifierOutputExists)
        {
            results.Add(CreateResult(
                "VFY-SP07-003",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofVerificationFailed,
                "SP-07 publication proof evidence is claimed, but the public verifier-output artifact is missing."));
        }

        if (!deletionReceiptExists)
        {
            results.Add(CreateResult(
                "VFY-SP07-004",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofWitnessDeletionMissing,
                "SP-07 publication proof evidence is claimed, but the public witness deletion receipt is missing."));
        }

        if (!transcriptExists)
        {
            return results;
        }

        var transcript = await ReadJsonAsync<ElectionSp07PublicationProofTranscriptArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp07PublicationProofTranscript,
            cancellationToken);
        var accepted = await ReadJsonAsync<AcceptedBallotSetArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.AcceptedBallotSet,
            cancellationToken);
        var published = await ReadJsonAsync<PublishedBallotStreamArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.PublishedBallotStream,
            cancellationToken);

        ValidateSp07TranscriptShape(results, transcript);
        ValidateSp07CanonicalProofFields(results, transcript);
        var proofManifest = TryReadSp07PublicationProofManifest(transcript, results);
        if (proofManifest is not null)
        {
            ValidateSp07PublicationProofManifest(results, transcript, proofManifest);
        }

        ValidateSp07AcceptedSetBinding(results, transcript, accepted);
        ValidateSp07PublishedStreamBinding(results, transcript, published);
        ValidateSp07TallyReplayBinding(results, transcript, tallyReplay, accepted, published);
        ValidateSp07PackageStatementBinding(results, electionRecord, transcript, proofManifest, accepted, published);

        if (verifierOutputExists)
        {
            var verifierOutput = await ReadJsonAsync<ElectionSp07VerifierOutputArtifactRecord>(
                packagePath,
                VerificationPackageFileNames.Sp07PublicationProofVerifierOutput,
                cancellationToken);
            ValidateSp07VerifierOutput(results, profileId, transcript, verifierOutput);
        }

        if (deletionReceiptExists)
        {
            var deletionReceipt = await ReadJsonAsync<ElectionSp07WitnessDeletionReceiptArtifactRecord>(
                packagePath,
                VerificationPackageFileNames.Sp07WitnessDeletionReceipt,
                cancellationToken);
            ValidateSp07WitnessDeletion(results, transcript, deletionReceipt);
        }

        if (results.All(x => x.Status != VerificationCheckStatus.Fail) &&
            proofManifest is not null &&
            HasManifestCanonicalProofVerifierInput(proofManifest))
        {
            results.AddRange(await VerifySp07ManifestCanonicalProofBytesAsync(
                transcript,
                proofManifest,
                publicProofVerifier,
                cancellationToken));
        }
        else if (results.All(x => x.Status != VerificationCheckStatus.Fail) &&
                 HasCanonicalProofVerifierInput(transcript))
        {
            results.Add(await VerifySp07CanonicalProofBytesAsync(
                transcript,
                publicProofVerifier,
                cancellationToken));
        }

        if (results.All(x => x.Status != VerificationCheckStatus.Fail) &&
            string.Equals(
                transcript.ExternalReviewStatus,
                ElectionSp07ProfileIds.ExternalReviewStatus,
                StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "VFY-SP07-090",
                VerificationCheckStatus.Warn,
                VerificationResultCodes.PublicationProofExternalReviewPending,
                "SP-07 proof artifacts are structurally consistent, but the protocol profile still records external crypto review as pending."));
        }

        if (results.All(x => x.Status != VerificationCheckStatus.Fail))
        {
            results.Add(CreateResult(
                "VFY-SP07-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PublicationProofEvidenceValid,
                "SP-07 publication proof transcript, tally replay binding, verifier output, and witness deletion receipt are structurally consistent."));
        }

        return results;
    }

    private static ElectionSp07PublicationProofManifestArtifactRecord? TryReadSp07PublicationProofManifest(
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        List<VerifierCheckResultRecord> results)
    {
        if (string.IsNullOrWhiteSpace(transcript.ProofBytes) ||
            !transcript.ProofBytes.Contains(
                ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion,
                StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ElectionSp07PublicationProofManifestArtifactRecord>(
                transcript.ProofBytes,
                VerificationJson.Options);
        }
        catch (JsonException exception)
        {
            results.Add(CreateResult(
                "VFY-SP07-017",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                $"SP-07 publication proof manifest could not be parsed: {exception.Message}"));
            return null;
        }
    }

    private static bool HasCanonicalProofVerifierInput(
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript) =>
        !string.IsNullOrWhiteSpace(transcript.StatementHashSha512) ||
        !string.IsNullOrWhiteSpace(transcript.FiatShamirTranscriptHashSha512) ||
        !string.IsNullOrWhiteSpace(transcript.CanonicalProofBytesHex) ||
        !string.IsNullOrWhiteSpace(transcript.CanonicalProofHashSha512) ||
        transcript.CanonicalProofByteLength is not null;

    private static void ValidateSp07TranscriptShape(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript)
    {
        if (!string.Equals(transcript.TranscriptVersion, ElectionSp07ProfileIds.TranscriptVersion, StringComparison.Ordinal) ||
            !string.Equals(transcript.PublicationProofMode, ElectionSp07ProfileIds.PublicationProofMode, StringComparison.Ordinal) ||
            !string.Equals(transcript.ProofConstruction, ElectionSp07ProfileIds.ProofConstruction, StringComparison.Ordinal) ||
            !string.Equals(transcript.StatementId, ElectionSp07ProfileIds.StatementId, StringComparison.Ordinal) ||
            !string.Equals(transcript.ProofSystemVersion, ElectionSp07ProfileIds.ProofSystemVersion, StringComparison.Ordinal) ||
            !IsSupportedSp07TranscriptProfile(transcript.ProfileId))
        {
            results.Add(CreateResult(
                "VFY-SP07-010",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 transcript profile, proof mode, construction, statement, or proof-system version is not the expected Protocol Omega v1 profile.",
                new Dictionary<string, string>
                {
                    ["transcript_version"] = transcript.TranscriptVersion,
                    ["publication_proof_mode"] = transcript.PublicationProofMode,
                    ["proof_construction"] = transcript.ProofConstruction,
                    ["statement_id"] = transcript.StatementId,
                    ["proof_system_version"] = transcript.ProofSystemVersion,
                    ["profile_id"] = transcript.ProfileId,
                }));
        }

        var computedProofHash = VerificationCanonicalHash.ComputeSha256LowerHex(transcript.ProofBytes);
        if (!string.Equals(computedProofHash, transcript.ProofHash, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "VFY-SP07-011",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptHashMismatch,
                "SP-07 proof hash does not match the transcript proof bytes.",
                new Dictionary<string, string>
                {
                    ["expected"] = transcript.ProofHash,
                    ["actual"] = computedProofHash,
                }));
        }
    }

    private static bool IsSupportedSp07TranscriptProfile(string profileId) =>
        string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);

    private static void ValidateSp07CanonicalProofFields(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript)
    {
        if (!HasCanonicalProofVerifierInput(transcript))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(transcript.StatementHashSha512) ||
            string.IsNullOrWhiteSpace(transcript.FiatShamirTranscriptHashSha512) ||
            string.IsNullOrWhiteSpace(transcript.CanonicalProofBytesHex) ||
            string.IsNullOrWhiteSpace(transcript.CanonicalProofHashSha512) ||
            transcript.CanonicalProofByteLength is null)
        {
            results.Add(CreateResult(
                "VFY-SP07-012",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 canonical proof verifier fields must be present together."));
            return;
        }

        if (transcript.StatementHashSha512.Length != 128 ||
            transcript.FiatShamirTranscriptHashSha512.Length != 128 ||
            transcript.CanonicalProofHashSha512.Length != 128)
        {
            results.Add(CreateResult(
                "VFY-SP07-013",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 canonical statement, transcript, and proof hashes must be SHA-512 hex values."));
        }

        byte[] canonicalProofBytes;
        try
        {
            canonicalProofBytes = Convert.FromHexString(transcript.CanonicalProofBytesHex);
        }
        catch (FormatException)
        {
            results.Add(CreateResult(
                "VFY-SP07-014",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 canonical proof bytes are not valid hex."));
            return;
        }

        if (canonicalProofBytes.Length != transcript.CanonicalProofByteLength)
        {
            results.Add(CreateResult(
                "VFY-SP07-015",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 canonical proof byte length does not match the encoded proof bytes.",
                new Dictionary<string, string>
                {
                    ["expected"] = transcript.CanonicalProofByteLength.Value.ToString(),
                    ["actual"] = canonicalProofBytes.Length.ToString(),
                }));
        }

        var computedProofHash = Convert.ToHexString(SHA512.HashData(canonicalProofBytes)).ToLowerInvariant();
        if (!string.Equals(computedProofHash, transcript.CanonicalProofHashSha512, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "VFY-SP07-016",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptHashMismatch,
                "SP-07 canonical proof SHA-512 hash does not match the encoded proof bytes.",
                new Dictionary<string, string>
                {
                    ["expected"] = transcript.CanonicalProofHashSha512,
                    ["actual"] = computedProofHash,
                }));
        }
    }

    private static void ValidateSp07PublicationProofManifest(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        ElectionSp07PublicationProofManifestArtifactRecord manifest)
    {
        if (!string.Equals(
                manifest.Schema,
                ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(manifest.ElectionId, transcript.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(manifest.PublicationProofMode, transcript.PublicationProofMode, StringComparison.Ordinal) ||
            !string.Equals(manifest.ProofConstruction, transcript.ProofConstruction, StringComparison.Ordinal) ||
            !string.Equals(manifest.StatementId, transcript.StatementId, StringComparison.Ordinal) ||
            !string.Equals(manifest.ProfileId, transcript.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(
                manifest.AcceptedBallotSetHash,
                transcript.AcceptedBallotSetHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                manifest.PublishedBallotStreamHash,
                transcript.PublishedBallotStreamHash,
                StringComparison.OrdinalIgnoreCase) ||
            manifest.AcceptedBallotCount != transcript.AcceptedBallotCount ||
            manifest.PublishedBallotCount != transcript.PublishedBallotCount ||
            manifest.CiphertextSlotCount != transcript.CiphertextSlotCount)
        {
            results.Add(CreateResult(
                "VFY-SP07-018",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 publication proof manifest does not bind the same election, profile, statement, accepted set, published stream, or counts as the transcript."));
        }

        if (manifest.ChunkCount < 1 ||
            manifest.ChunkCount != manifest.Chunks.Count ||
            manifest.CompletedChunkCount != manifest.ChunkCount ||
            manifest.FailedChunkCount != 0 ||
            manifest.Chunks.Any(chunk => !chunk.Passed))
        {
            results.Add(CreateResult(
                "VFY-SP07-019",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofVerificationFailed,
                "SP-07 publication proof manifest must contain only completed passing chunks."));
        }

        var orderedChunks = manifest.Chunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToArray();
        if (orderedChunks.Select(chunk => chunk.ChunkId).Distinct(StringComparer.Ordinal).Count() != orderedChunks.Length ||
            orderedChunks.Select(chunk => chunk.ChunkIndex).Distinct().Count() != orderedChunks.Length ||
            orderedChunks.Any(chunk => chunk.Count < 1 || chunk.Offset < 0) ||
            orderedChunks.Any(chunk =>
                chunk.StatementHashSha512.Length != 128 ||
                chunk.FiatShamirTranscriptHashSha512.Length != 128 ||
                chunk.CanonicalProofHashSha512.Length != 128 ||
                chunk.CanonicalProofByteLength < 1 ||
                string.IsNullOrWhiteSpace(chunk.CanonicalProofBytesHex)))
        {
            results.Add(CreateResult(
                "VFY-SP07-080",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 publication proof manifest chunk ids, indexes, hashes, and canonical proof fields must be complete."));
            return;
        }

        var expectedOffset = 0;
        foreach (var chunk in orderedChunks)
        {
            if (chunk.ChunkIndex != Array.IndexOf(orderedChunks, chunk) ||
                chunk.Offset != expectedOffset)
            {
                results.Add(CreateResult(
                    "VFY-SP07-081",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicationProofTranscriptInvalid,
                    "SP-07 publication proof manifest chunks must be ordered and cover the published stream without gaps."));
                return;
            }

            expectedOffset += chunk.Count;

            if (!string.Equals(chunk.ResultCode, "PUB-005", StringComparison.Ordinal) ||
                !string.Equals(chunk.ProofProfileId, "matrix_m_1_publication_proof_v1", StringComparison.Ordinal) ||
                !string.Equals(
                    chunk.PublishedBallotStreamHash,
                    transcript.PublishedBallotStreamHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                results.Add(CreateResult(
                    "VFY-SP07-082",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicationProofVerificationFailed,
                    "SP-07 publication proof manifest chunk profile, result code, or published stream binding is invalid."));
                return;
            }

            if (!TryValidateSp07ManifestChunkCanonicalBytes(chunk, results))
            {
                return;
            }
        }

        if (expectedOffset != manifest.PublishedBallotCount)
        {
            results.Add(CreateResult(
                "VFY-SP07-083",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofCountMismatch,
                "SP-07 publication proof manifest chunk counts do not add up to the published ballot count."));
        }
    }

    private static bool TryValidateSp07ManifestChunkCanonicalBytes(
        ElectionSp07PublicationProofManifestChunkArtifactRecord chunk,
        List<VerifierCheckResultRecord> results)
    {
        byte[] canonicalProofBytes;
        try
        {
            canonicalProofBytes = Convert.FromHexString(chunk.CanonicalProofBytesHex!);
        }
        catch (FormatException)
        {
            results.Add(CreateResult(
                "VFY-SP07-084",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 publication proof manifest chunk canonical proof bytes are not valid hex."));
            return false;
        }

        if (canonicalProofBytes.Length != chunk.CanonicalProofByteLength)
        {
            results.Add(CreateResult(
                "VFY-SP07-085",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 publication proof manifest chunk canonical proof byte length does not match the encoded bytes."));
            return false;
        }

        var computedProofHash = Convert.ToHexString(SHA512.HashData(canonicalProofBytes)).ToLowerInvariant();
        if (!string.Equals(computedProofHash, chunk.CanonicalProofHashSha512, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "VFY-SP07-086",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptHashMismatch,
                "SP-07 publication proof manifest chunk canonical proof SHA-512 hash does not match the encoded proof bytes."));
            return false;
        }

        return true;
    }

    private static bool HasManifestCanonicalProofVerifierInput(
        ElectionSp07PublicationProofManifestArtifactRecord manifest) =>
        manifest.Chunks.Count > 0 &&
        manifest.Chunks.All(chunk =>
            !string.IsNullOrWhiteSpace(chunk.StatementHashSha512) &&
            !string.IsNullOrWhiteSpace(chunk.FiatShamirTranscriptHashSha512) &&
            !string.IsNullOrWhiteSpace(chunk.CanonicalProofHashSha512) &&
            !string.IsNullOrWhiteSpace(chunk.CanonicalProofBytesHex) &&
            chunk.CanonicalProofByteLength > 0);

    private static void ValidateSp07PackageStatementBinding(
        List<VerifierCheckResultRecord> results,
        ElectionRecordReferenceRecord electionRecord,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        ElectionSp07PublicationProofManifestArtifactRecord? manifest,
        AcceptedBallotSetArtifactRecord accepted,
        PublishedBallotStreamArtifactRecord published)
    {
        if (manifest is null || !HasManifestCanonicalProofVerifierInput(manifest))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(electionRecord.ProtocolReleaseManifestHash))
        {
            results.Add(CreateResult(
                "VFY-SP07-072",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 statement reconstruction requires the sealed Protocol Omega release manifest hash from the election record."));
            return;
        }

        if (!TryBuildSp07PackageStatementContext(accepted, published, transcript, results, out var context))
        {
            return;
        }

        foreach (var chunk in manifest.Chunks.OrderBy(x => x.ChunkIndex))
        {
            var expectedStatementHash = Sp07PackagePublicStatementHasher.ComputeStatementHashSha512(
                new Sp07PackagePublicStatementHashInput(
                    transcript.ElectionId,
                    chunk.ChunkId,
                    electionRecord.ProtocolReleaseManifestHash,
                    transcript.BallotDefinitionHash,
                    context.PublicKey,
                    chunk.Count,
                    transcript.CiphertextSlotCount,
                    transcript.AcceptedBallotSetHash,
                    transcript.PublishedBallotStreamHash));

            if (!string.Equals(expectedStatementHash, chunk.StatementHashSha512, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(CreateResult(
                    "VFY-SP07-073",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicationProofTranscriptHashMismatch,
                    "SP-07 canonical chunk statement hash does not match the public statement reconstructed from the package artifacts.",
                    new Dictionary<string, string>
                    {
                        ["chunk_id"] = chunk.ChunkId,
                        ["expected_statement_hash_sha512"] = expectedStatementHash,
                        ["chunk_statement_hash_sha512"] = chunk.StatementHashSha512,
                    }));
            }
        }
    }

    private static bool TryBuildSp07PackageStatementContext(
        AcceptedBallotSetArtifactRecord accepted,
        PublishedBallotStreamArtifactRecord published,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        List<VerifierCheckResultRecord> results,
        out Sp07PackageStatementContext context)
    {
        context = default!;
        if (accepted.AcceptedBallots.Count == 0 || published.PublishedBallots.Count == 0)
        {
            results.Add(CreateResult(
                "VFY-SP07-074",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofCountMismatch,
                "SP-07 statement reconstruction requires non-empty accepted and published ballot artifacts."));
            return false;
        }

        var parsedAccepted = new List<Sp07PackageParsedBallot>();
        foreach (var ballot in accepted.AcceptedBallots)
        {
            if (!TryParseSp07PackageBallot(
                    ballot.EncryptedBallotPackage,
                    $"accepted:{ballot.BallotNullifier}",
                    results,
                    out var parsed))
            {
                return false;
            }

            if (!HashMatches(ballot.EncryptedBallotPackageHash, VerificationCanonicalHash.ComputeSha256UpperHex(ballot.EncryptedBallotPackage)) ||
                !HashMatches(ballot.ProofBundleHash, VerificationCanonicalHash.ComputeSha256UpperHex(ballot.ProofBundle)))
            {
                results.Add(CreateResult(
                    "VFY-SP07-075",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicationProofAcceptedSetMismatch,
                    "SP-07 accepted ballot artifact hash fields do not match their package bytes."));
                return false;
            }

            parsedAccepted.Add(parsed);
        }

        var parsedPublished = new List<Sp07PackageParsedBallot>();
        foreach (var ballot in published.PublishedBallots)
        {
            if (!TryParseSp07PackageBallot(
                    ballot.EncryptedBallotPackage,
                    $"published:{ballot.PublicationSequence}",
                    results,
                    out var parsed))
            {
                return false;
            }

            if (!HashMatches(ballot.EncryptedBallotPackageHash, VerificationCanonicalHash.ComputeSha256UpperHex(ballot.EncryptedBallotPackage)) ||
                !HashMatches(ballot.ProofBundleHash, VerificationCanonicalHash.ComputeSha256UpperHex(ballot.ProofBundle)))
            {
                results.Add(CreateResult(
                    "VFY-SP07-076",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicationProofPublishedStreamMismatch,
                    "SP-07 published ballot artifact hash fields do not match their package bytes."));
                return false;
            }

            parsedPublished.Add(parsed);
        }

        var publicKey = parsedAccepted[0].PublicKey;
        var slotCount = parsedAccepted[0].SelectionCount;
        var allBallots = parsedAccepted.Concat(parsedPublished).ToArray();
        if (allBallots.Any(x =>
                x.SelectionCount != slotCount ||
                !PointEquals(x.PublicKey, publicKey)) ||
            slotCount != transcript.CiphertextSlotCount)
        {
            results.Add(CreateResult(
                "VFY-SP07-077",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofPublicKeyMismatch,
                "SP-07 public statement reconstruction requires every accepted and published ballot to share the transcript public key and slot count.",
                new Dictionary<string, string>
                {
                    ["transcript_ciphertext_slot_count"] = transcript.CiphertextSlotCount.ToString(),
                    ["reconstructed_ciphertext_slot_count"] = slotCount.ToString(),
                }));
            return false;
        }

        context = new Sp07PackageStatementContext(publicKey);
        return true;
    }

    private static bool TryParseSp07PackageBallot(
        string encryptedBallotPackage,
        string label,
        List<VerifierCheckResultRecord> results,
        out Sp07PackageParsedBallot parsed)
    {
        parsed = default!;
        try
        {
            using var document = JsonDocument.Parse(encryptedBallotPackage);
            var root = document.RootElement;
            var publicKey = ReadSp07Point(root.GetProperty("publicKey"), $"{label}.publicKey");
            var selectionCount = root.GetProperty("selectionCount").GetInt32();
            var ciphertext = root.GetProperty("ciphertext");
            var c1 = ciphertext.GetProperty("c1");
            var c2 = ciphertext.GetProperty("c2");
            if (selectionCount < 1 || c1.ValueKind != JsonValueKind.Array || c2.ValueKind != JsonValueKind.Array ||
                c1.GetArrayLength() != selectionCount || c2.GetArrayLength() != selectionCount)
            {
                throw new JsonException("ciphertext component counts do not match selectionCount.");
            }

            parsed = new Sp07PackageParsedBallot(publicKey, selectionCount);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            results.Add(CreateResult(
                "VFY-SP07-078",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                $"SP-07 package ballot '{label}' cannot be parsed as a public BabyJubJub vector-ballot ciphertext: {exception.Message}"));
            return false;
        }
    }

    private static Sp07PackagePublicPoint ReadSp07Point(JsonElement element, string label)
    {
        var x = element.GetProperty("x").GetString()?.Trim();
        var y = element.GetProperty("y").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y))
        {
            throw new JsonException($"point {label} is missing x or y.");
        }

        return new Sp07PackagePublicPoint(x, y);
    }

    private static bool PointEquals(Sp07PackagePublicPoint left, Sp07PackagePublicPoint right) =>
        string.Equals(left.X, right.X, StringComparison.Ordinal) &&
        string.Equals(left.Y, right.Y, StringComparison.Ordinal);

    private static bool HashMatches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record Sp07PackageStatementContext(Sp07PackagePublicPoint PublicKey);

    private sealed record Sp07PackageParsedBallot(
        Sp07PackagePublicPoint PublicKey,
        int SelectionCount);

    private static async Task<VerifierCheckResultRecord> VerifySp07CanonicalProofBytesAsync(
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        ISp07PackagePublicProofVerifier publicProofVerifier,
        CancellationToken cancellationToken)
    {
        var result = await publicProofVerifier.VerifyAsync(
            new Sp07PackagePublicProofVerificationRequest(
                transcript.ElectionId,
                BuildPackageProofSessionId(transcript),
                "package-chunk-0",
                transcript.StatementHashSha512!,
                transcript.FiatShamirTranscriptHashSha512!,
                transcript.CanonicalProofHashSha512!,
                transcript.AcceptedBallotSetHash,
                transcript.PublishedBallotStreamHash,
                transcript.CanonicalProofByteLength!.Value,
                transcript.CanonicalProofBytesHex!),
            cancellationToken);

        if (result.Passed)
        {
            return CreateResult(
                "VFY-SP07-070",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PublicationProofEvidenceValid,
                "SP-07 Rust public verifier accepted the canonical proof bytes.",
                result.Evidence);
        }

        var evidence = new Dictionary<string, string>(result.Evidence, StringComparer.Ordinal)
        {
            ["rust_result_code"] = result.ResultCode,
        };
        return CreateResult(
            "VFY-SP07-071",
            VerificationCheckStatus.Fail,
            VerificationResultCodes.PublicationProofVerificationFailed,
            result.Message,
            evidence);
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> VerifySp07ManifestCanonicalProofBytesAsync(
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        ElectionSp07PublicationProofManifestArtifactRecord manifest,
        ISp07PackagePublicProofVerifier publicProofVerifier,
        CancellationToken cancellationToken)
    {
        var failures = new List<Sp07PackagePublicProofVerificationResult>();
        var verifiedChunks = 0;
        foreach (var chunk in manifest.Chunks.OrderBy(x => x.ChunkIndex))
        {
            var result = await publicProofVerifier.VerifyAsync(
                new Sp07PackagePublicProofVerificationRequest(
                    transcript.ElectionId,
                    manifest.ProofSessionId,
                    chunk.ChunkId,
                    chunk.StatementHashSha512,
                    chunk.FiatShamirTranscriptHashSha512,
                    chunk.CanonicalProofHashSha512,
                    transcript.AcceptedBallotSetHash,
                    transcript.PublishedBallotStreamHash,
                    chunk.CanonicalProofByteLength,
                    chunk.CanonicalProofBytesHex!),
                cancellationToken);

            if (result.Passed)
            {
                verifiedChunks++;
            }
            else
            {
                failures.Add(result);
            }
        }

        if (failures.Count == 0)
        {
            return
            [
                CreateResult(
                    "VFY-SP07-070",
                    VerificationCheckStatus.Pass,
                    VerificationResultCodes.PublicationProofEvidenceValid,
                    "SP-07 Rust public verifier accepted every canonical chunk proof in the publication manifest.",
                    new Dictionary<string, string>
                    {
                        ["verified_chunk_count"] = verifiedChunks.ToString(),
                    }),
            ];
        }

        return failures
            .Select((failure, index) =>
            {
                var evidence = new Dictionary<string, string>(failure.Evidence, StringComparer.Ordinal)
                {
                    ["rust_result_code"] = failure.ResultCode,
                    ["failure_index"] = index.ToString(),
                };
                return CreateResult(
                    "VFY-SP07-071",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicationProofVerificationFailed,
                    failure.Message,
                    evidence);
            })
            .ToArray();
    }

    private static string BuildPackageProofSessionId(
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript)
    {
        var source = string.IsNullOrWhiteSpace(transcript.TranscriptHash)
            ? transcript.ProofHash
            : transcript.TranscriptHash;
        var safe = new string(source
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(48)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "package-proof-session" : $"package-{safe}";
    }

    private static void ValidateSp07AcceptedSetBinding(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        AcceptedBallotSetArtifactRecord accepted)
    {
        if (!string.Equals(transcript.ElectionId, accepted.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(
                transcript.AcceptedBallotSetHash,
                accepted.AcceptedBallotInventoryHash,
                StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "VFY-SP07-020",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofAcceptedSetMismatch,
                "SP-07 transcript accepted-set hash does not match the accepted ballot set artifact.",
                new Dictionary<string, string>
                {
                    ["transcript_election_id"] = transcript.ElectionId,
                    ["accepted_election_id"] = accepted.ElectionId,
                    ["transcript_accepted_ballot_set_hash"] = transcript.AcceptedBallotSetHash,
                    ["accepted_ballot_inventory_hash"] = accepted.AcceptedBallotInventoryHash,
                }));
        }

        if (transcript.AcceptedBallotCount != accepted.AcceptedBallotCount)
        {
            results.Add(CreateResult(
                "VFY-SP07-021",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofCountMismatch,
                "SP-07 transcript accepted ballot count does not match the accepted ballot set artifact.",
                new Dictionary<string, string>
                {
                    ["transcript_accepted_ballot_count"] = transcript.AcceptedBallotCount.ToString(),
                    ["accepted_ballot_count"] = accepted.AcceptedBallotCount.ToString(),
                }));
        }
    }

    private static void ValidateSp07PublishedStreamBinding(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        PublishedBallotStreamArtifactRecord published)
    {
        if (!string.Equals(transcript.ElectionId, published.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(
                transcript.PublishedBallotStreamHash,
                published.PublishedBallotStreamHash,
                StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "VFY-SP07-030",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofPublishedStreamMismatch,
                "SP-07 transcript published-stream hash does not match the published ballot stream artifact.",
                new Dictionary<string, string>
                {
                    ["transcript_election_id"] = transcript.ElectionId,
                    ["published_election_id"] = published.ElectionId,
                    ["transcript_published_ballot_stream_hash"] = transcript.PublishedBallotStreamHash,
                    ["published_ballot_stream_hash"] = published.PublishedBallotStreamHash,
                }));
        }

        if (transcript.PublishedBallotCount != published.PublishedBallotCount)
        {
            results.Add(CreateResult(
                "VFY-SP07-031",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofCountMismatch,
                "SP-07 transcript published ballot count does not match the published ballot stream artifact.",
                new Dictionary<string, string>
                {
                    ["transcript_published_ballot_count"] = transcript.PublishedBallotCount.ToString(),
                    ["published_ballot_count"] = published.PublishedBallotCount.ToString(),
                }));
        }
    }

    private static void ValidateSp07TallyReplayBinding(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        TallyReplayArtifactRecord tallyReplay,
        AcceptedBallotSetArtifactRecord accepted,
        PublishedBallotStreamArtifactRecord published)
    {
        if (!string.Equals(tallyReplay.ElectionId, transcript.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(tallyReplay.PublicationProofMode, transcript.PublicationProofMode, StringComparison.Ordinal) ||
            tallyReplay.EvidenceStatus != VerificationCheckStatus.Pass ||
            !string.Equals(tallyReplay.ResultCode, VerificationResultCodes.PublicationProofEvidenceValid, StringComparison.Ordinal) ||
            !string.Equals(tallyReplay.AcceptedBallotSetHash, accepted.AcceptedBallotInventoryHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tallyReplay.PublishedBallotStreamHash, published.PublishedBallotStreamHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tallyReplay.PublicationProofTranscriptHash, transcript.TranscriptHash, StringComparison.Ordinal) ||
            !string.Equals(tallyReplay.PublicationProofHash, transcript.ProofHash, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "VFY-SP07-040",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofTallyReplayMismatch,
                "SP-07 tally replay does not bind the same accepted set, published stream, transcript hash, and proof hash as the transcript.",
                new Dictionary<string, string>
                {
                    ["tally_evidence_status"] = tallyReplay.EvidenceStatus.ToString(),
                    ["tally_result_code"] = tallyReplay.ResultCode,
                    ["tally_accepted_ballot_set_hash"] = tallyReplay.AcceptedBallotSetHash ?? string.Empty,
                    ["accepted_ballot_inventory_hash"] = accepted.AcceptedBallotInventoryHash,
                    ["tally_published_ballot_stream_hash"] = tallyReplay.PublishedBallotStreamHash ?? string.Empty,
                    ["published_ballot_stream_hash"] = published.PublishedBallotStreamHash,
                    ["tally_transcript_hash"] = tallyReplay.PublicationProofTranscriptHash ?? string.Empty,
                    ["transcript_hash"] = transcript.TranscriptHash,
                    ["tally_proof_hash"] = tallyReplay.PublicationProofHash ?? string.Empty,
                    ["proof_hash"] = transcript.ProofHash,
                }));
        }
    }

    private static void ValidateSp07VerifierOutput(
        List<VerifierCheckResultRecord> results,
        string profileId,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        ElectionSp07VerifierOutputArtifactRecord verifierOutput)
    {
        if (!string.Equals(verifierOutput.ElectionId, transcript.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(verifierOutput.VerifierProfileId, profileId, StringComparison.Ordinal) ||
            !string.Equals(verifierOutput.StatementId, transcript.StatementId, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                "VFY-SP07-050",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofVerificationFailed,
                "SP-07 verifier output election id, profile id, or statement id does not match the package request and transcript."));
        }

        if (verifierOutput.Results.Any(x => x.Status == VerificationCheckStatus.Fail) ||
            !verifierOutput.Results.Any(x =>
                x.CheckCode == "VFY-SP07-000" &&
                x.Status == VerificationCheckStatus.Pass &&
                string.Equals(x.ResultCode, VerificationResultCodes.PublicationProofEvidenceValid, StringComparison.Ordinal)))
        {
            results.Add(CreateResult(
                "VFY-SP07-051",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofVerificationFailed,
                "SP-07 verifier output does not contain a passing publication proof evidence result."));
        }
    }

    private static void ValidateSp07WitnessDeletion(
        List<VerifierCheckResultRecord> results,
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        ElectionSp07WitnessDeletionReceiptArtifactRecord deletionReceipt)
    {
        if (!string.Equals(deletionReceipt.ElectionId, transcript.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(
                deletionReceipt.DeletionStatus,
                ElectionPublicationWitnessDeletionStatus.Completed.ToString(),
                StringComparison.Ordinal) ||
            !string.Equals(deletionReceipt.TranscriptHash, transcript.TranscriptHash, StringComparison.Ordinal) ||
            !string.Equals(deletionReceipt.ProofHash, transcript.ProofHash, StringComparison.Ordinal) ||
            deletionReceipt.WitnessCount != transcript.AcceptedBallotCount)
        {
            results.Add(CreateResult(
                "VFY-SP07-060",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublicationProofWitnessDeletionInvalid,
                "SP-07 witness deletion receipt does not match the transcript or is not completed.",
                new Dictionary<string, string>
                {
                    ["deletion_status"] = deletionReceipt.DeletionStatus,
                    ["deletion_transcript_hash"] = deletionReceipt.TranscriptHash,
                    ["transcript_hash"] = transcript.TranscriptHash,
                    ["deletion_proof_hash"] = deletionReceipt.ProofHash,
                    ["proof_hash"] = transcript.ProofHash,
                    ["deletion_witness_count"] = deletionReceipt.WitnessCount.ToString(),
                    ["transcript_accepted_ballot_count"] = transcript.AcceptedBallotCount.ToString(),
                }));
        }
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

using System.Text.Json;
using FluentAssertions;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp07ContractTests
{
    [Fact]
    public void PublicationProofProfileIds_ShouldExposeCanonicalV1Selection()
    {
        ElectionSp07ProfileIds.PublicationProofMode.Should().Be("zk_rerandomization_shuffle_v1");
        ElectionSp07ProfileIds.ProofConstruction.Should().Be("bayer_groth_reencryption_shuffle_argument_v1");
        ElectionSp07ProfileIds.StatementId.Should().Be("sp07-bayer-groth-hush-vector-shuffle-v1");
        ElectionSp07ProfileIds.ExternalReviewStatus.Should().Be("external_crypto_review_pending");
        ElectionSp07ProfileIds.HighAssuranceV1MaxAcceptedBallots.Should().Be(500);
        ElectionSp07ProfileIds.HighAssuranceV1MaxEncryptedSlots.Should().Be(8);
        ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks.Should().Be(5);
    }

    [Fact]
    public void PublicSp07Artifacts_ShouldExcludeWitnessMappingAndVoteMaterial()
    {
        var electionId = ElectionId.NewElectionId.ToString();
        var transcript = new ElectionSp07PublicationProofTranscriptArtifactRecord(
            electionId,
            ElectionSp07ProfileIds.TranscriptVersion,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            VerificationProfileIds.HighAssuranceV1,
            BallotDefinitionHash: "ballot-definition-hash",
            BallotEncryptionSchemeVersion: "babyjubjub-elgamal-vector-ballot-v1",
            ElectionPublicKeyId: "election-public-key-id",
            AcceptedBallotSetHash: "accepted-set-hash",
            PublishedBallotStreamHash: "published-stream-hash",
            AcceptedBallotCount: 2,
            PublishedBallotCount: 2,
            CiphertextSlotCount: 3,
            ElectionSp07ProfileIds.ProofSystemVersion,
            ProofBytes: "proof-bytes",
            ProofHash: "proof-hash",
            TranscriptHash: "transcript-hash",
            ElectionSp07ProfileIds.ExternalReviewStatus,
            GeneratedAt: DateTime.UnixEpoch,
            GeneratorReleaseHash: "generator-release-hash",
            VerifierReleaseHash: "verifier-release-hash",
            PublicPrivacyBoundary:
            [
                "no_hidden_permutation",
                "no_shuffle_map",
                "no_rerandomization_randomness",
                "no_raw_witness",
            ]);
        var receipt = new ElectionSp07WitnessDeletionReceiptArtifactRecord(
            electionId,
            WitnessSetHash: "witness-set-hash",
            WitnessCount: 2,
            TranscriptHash: transcript.TranscriptHash,
            ProofHash: transcript.ProofHash,
            DeletionStatus: ElectionPublicationWitnessDeletionStatus.Completed.ToString(),
            DeletedAt: DateTime.UnixEpoch.AddMinutes(1),
            PublicPrivacyBoundary: transcript.PublicPrivacyBoundary);

        var json = JsonSerializer.Serialize(new { transcript, receipt }, VerificationJson.Options);

        json.Should().Contain("publicationProofMode");
        json.Should().Contain("proofConstruction");
        json.Should().Contain("acceptedBallotSetHash");
        json.Should().Contain("publishedBallotStreamHash");
        json.Should().Contain("proofHash");
        json.Should().NotContain("hiddenPermutation");
        json.Should().NotContain("shuffleMap");
        json.Should().NotContain("acceptedToPublishedMapping");
        json.Should().NotContain("rerandomizationRandomness");
        json.Should().NotContain("privateRandomness");
        json.Should().NotContain("sealedWitnessMaterial");
        json.Should().NotContain("rawWitness");
        json.Should().NotContain("voterId");
        json.Should().NotContain("plaintextVote");
    }

    [Fact]
    public void RestrictedSp07Artifacts_ShouldCarryOperationalMetadataButNotRawWitnessMaterial()
    {
        var electionId = ElectionId.NewElectionId;
        var session = new ElectionPublicationProofSessionRecord(
            Guid.NewGuid(),
            electionId,
            WitnessSetId: Guid.NewGuid(),
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionPublicationProofSessionStatus.Failed,
            StartedAt: DateTime.UnixEpoch,
            CompletedAt: DateTime.UnixEpoch.AddMinutes(1),
            AcceptedBallotCount: 2,
            PublishedBallotCount: 2,
            ChunkCount: 1,
            RetryCount: 1,
            FailureCode: VerificationResultCodes.PublicationProofVerificationFailed,
            FailureReason: "Synthetic failure",
            AcceptedBallotSetHash: "accepted-set-hash",
            PublishedBallotStreamHash: "published-stream-hash",
            TranscriptHash: null,
            ProofHash: null,
            ServerVerifierOutputHash: null,
            DeletionReceiptId: null);
        var artifact = new ElectionSp07RestrictedProofSessionArtifactRecord(
            electionId.ToString(),
            [session]);

        var json = JsonSerializer.Serialize(artifact, VerificationJson.Options);

        json.Should().Contain(VerificationResultCodes.PublicationProofVerificationFailed);
        json.Should().Contain("acceptedBallotSetHash");
        json.Should().Contain("publishedBallotStreamHash");
        json.Should().NotContain("hiddenPermutation");
        json.Should().NotContain("shuffleMap");
        json.Should().NotContain("rerandomizationRandomness");
        json.Should().NotContain("sealedWitnessMaterial");
        json.Should().NotContain("proofWitness");
    }

    [Fact]
    public void Sp07PackageFileNames_ShouldSeparatePublicAndRestrictedEvidence()
    {
        VerificationPackageFileNames.Sp07PublicationProofTranscript.Should()
            .Be("artifacts/election-record/publication-proof-transcript.json");
        VerificationPackageFileNames.Sp07PublicationProofVerifierOutput.Should()
            .Be("artifacts/election-record/publication-proof-verifier-output.json");
        VerificationPackageFileNames.Sp07WitnessDeletionReceipt.Should()
            .Be("artifacts/election-record/witness-deletion-receipt.json");
        VerificationPackageFileNames.RestrictedSp07PublicationProofSession.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp07WitnessDeletionLog.Should().StartWith("artifacts/restricted/");
    }

    [Theory]
    [InlineData("hiddenPermutation")]
    [InlineData("shuffleMap")]
    [InlineData("rerandomizationRandomness")]
    [InlineData("sealedWitnessMaterial")]
    [InlineData("proofWitness")]
    public void PublicPrivacyBoundary_ShouldRejectSp07RestrictedFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage(fieldName).Should().BeTrue();
    }

    [Fact]
    public void VerificationResultCodes_ShouldExposeStablePublicationProofCodes()
    {
        var codes = new[]
        {
            VerificationResultCodes.PublicationProofEvidenceValid,
            VerificationResultCodes.PublicationProofTranscriptMissing,
            VerificationResultCodes.PublicationProofTranscriptInvalid,
            VerificationResultCodes.PublicationProofTranscriptHashMismatch,
            VerificationResultCodes.PublicationProofAcceptedSetMismatch,
            VerificationResultCodes.PublicationProofPublishedStreamMismatch,
            VerificationResultCodes.PublicationProofCountMismatch,
            VerificationResultCodes.PublicationProofPublicKeyMismatch,
            VerificationResultCodes.PublicationProofVerificationFailed,
            VerificationResultCodes.PublicationProofTallyReplayMismatch,
            VerificationResultCodes.PublicationProofForbiddenFieldLeak,
            VerificationResultCodes.PublicationProofWitnessDeletionMissing,
            VerificationResultCodes.PublicationProofWitnessDeletionInvalid,
            VerificationResultCodes.PublicationProofExternalReviewPending,
            VerificationResultCodes.PublicationProofEnvelopeExceeded,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(x => x.Should().StartWith("publication_proof_"));
    }
}

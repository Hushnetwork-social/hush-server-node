using System.Text.Json;
using FluentAssertions;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp04ContractTests
{
    [Fact]
    public void BoundReceiptContract_ShouldExcludeVoteChoiceRandomnessAndWitnesses()
    {
        var receipt = ElectionModelFactory.CreateBoundReceiptRecord(
            ElectionId.NewElectionId,
            ballotDefinitionVersion: 1,
            ballotDefinitionHash: [1, 2, 3, 4],
            preparedBallotId: Guid.NewGuid(),
            preparedBallotHash: "prepared-hash",
            receiptSecret: "voter-held-secret",
            receiptCommitment: "receipt-commitment",
            acceptedBallotId: Guid.NewGuid(),
            acceptedAt: DateTime.UtcNow,
            serverAcceptanceProof: "server-acceptance-proof",
            verifierProfileId: VerificationProfileIds.HighAssuranceV1);

        var json = JsonSerializer.Serialize(receipt, VerificationJson.Options);

        json.Should().Contain("receiptSecret");
        json.Should().Contain("receiptCommitment");
        json.Should().NotContain("voteChoice");
        json.Should().NotContain("choice");
        json.Should().NotContain("randomness");
        json.Should().NotContain("witness");
    }

    [Fact]
    public void AcceptedBallotBinding_ShouldStoreReceiptCommitmentWithoutReceiptSecret()
    {
        var acceptedBallot = ElectionModelFactory.CreateAcceptedBallotRecord(
            ElectionId.NewElectionId,
            encryptedBallotPackage: "encrypted-ballot",
            proofBundle: "proof-bundle",
            ballotNullifier: "nullifier",
            preparedBallotId: Guid.NewGuid(),
            preparedBallotHash: "prepared-hash",
            receiptCommitment: "receipt-commitment",
            receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
            ballotDefinitionVersion: 1,
            ballotDefinitionHash: [5, 6, 7, 8]);

        var json = JsonSerializer.Serialize(acceptedBallot, VerificationJson.Options);

        json.Should().Contain("preparedBallotHash");
        json.Should().Contain("receiptCommitment");
        json.Should().NotContain("receiptSecret");
        json.Should().NotContain("voteChoice");
        json.Should().NotContain("finalRandomness");
        json.Should().NotContain("witness");
    }

    [Fact]
    public void PublicSp04Evidence_ShouldExcludeRestrictedVoterMaterial()
    {
        var evidence = new ElectionSp04EvidenceRecord(
            ElectionId.NewElectionId,
            new ElectionSp04PolicyRecord(
                ElectionSp04ProfileIds.ChallengeSpoilV1,
                RequiredChallengeCount: 1,
                PreparedPackageTtlSeconds: 900,
                ElectionBallotDefinitionMutationPolicy.ImmutableAfterOpen),
            BallotDefinitionVersion: 1,
            BallotDefinitionHash: [1, 1, 1, 1],
            BallotDefinitionSealedAt: DateTime.UtcNow,
            PreparedPackageCount: 3,
            SpoiledPackageCount: 1,
            AcceptedBoundReceiptCount: 2,
            ReceiptCommitmentSetHash: "receipt-set-hash",
            PublicPrivacyBoundary:
            [
                "no_named_voter",
                "no_spoiled_plaintext",
                "no_final_randomness",
                "no_proof_material",
            ]);

        var receiptCommitments = new[]
        {
            new ElectionSp04ReceiptCommitmentRecord(
                AcceptedBallotId: Guid.NewGuid(),
                PreparedBallotId: Guid.NewGuid(),
                PreparedBallotHash: "prepared-a",
                ReceiptCommitment: "receipt-a",
                ReceiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                AcceptedAt: DateTime.UtcNow),
        };

        var json = JsonSerializer.Serialize(new { evidence, receiptCommitments }, VerificationJson.Options);

        json.Should().Contain("receiptCommitmentSetHash");
        json.Should().NotContain("organizationVoterId");
        json.Should().NotContain("linkedActorPublicAddress");
        json.Should().NotContain("spoiledTranscriptHash");
        json.Should().NotContain("voteChoice");
        json.Should().NotContain("finalRandomness");
        json.Should().NotContain("witness");
    }

    [Fact]
    public void Sp04PackageFileNames_ShouldSeparatePublicAndRestrictedEvidence()
    {
        VerificationPackageFileNames.Sp04Evidence.Should().Be("artifacts/election-record/sp04-evidence.json");
        VerificationPackageFileNames.Sp04ReceiptCommitments.Should().Be("artifacts/election-record/sp04-receipt-commitments.json");
        VerificationPackageFileNames.RestrictedSp04CeremonyRecords.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp04SpoilMarkers.Should().StartWith("artifacts/restricted/");
    }

    [Fact]
    public void PreparedBallotCommitment_ShouldNormalizeValuesAndExposeExpiry()
    {
        var now = DateTime.UtcNow;

        var prepared = ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
            ElectionId.NewElectionId,
            organizationVoterId: " voter-001 ",
            linkedActorPublicAddress: " actor-address ",
            preparedBallotHash: " prepared-hash ",
            ballotDefinitionVersion: 1,
            ballotDefinitionHash: [9, 9, 9, 9],
            proofStatementId: " proof-statement ",
            precommittedAt: now,
            ttl: TimeSpan.FromMinutes(15));

        prepared.OrganizationVoterId.Should().Be("voter-001");
        prepared.LinkedActorPublicAddress.Should().Be("actor-address");
        prepared.PreparedBallotHash.Should().Be("prepared-hash");
        prepared.ProofStatementId.Should().Be("proof-statement");
        prepared.ExpiresAt.Should().Be(now.AddMinutes(15));
        prepared.IsExpired(now.AddMinutes(14)).Should().BeFalse();
        prepared.IsExpired(now.AddMinutes(15)).Should().BeTrue();
    }
}

using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Moq;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ElectionPublicationWitnessDeletionServiceTests
{
    [Fact]
    public async Task TryDeleteVerifiedWitnessesAsync_WhenVerifiedSessionHasSealedWitnesses_ShouldDeleteWitnessesAndSaveReceipt()
    {
        var electionId = ElectionId.NewElectionId;
        var proofSessionId = Guid.NewGuid();
        var witnessSetId = Guid.NewGuid();
        var deletedAt = new DateTime(2026, 05, 07, 10, 15, 00, DateTimeKind.Utc);
        var transcriptHash = "transcript-hash";
        var proofHash = "proof-hash";
        var session = CreateSession(electionId, proofSessionId, witnessSetId, transcriptHash, proofHash);
        var transcript = CreateTranscript(electionId, proofSessionId, witnessSetId, transcriptHash, proofHash);
        var witnesses = new[]
        {
            CreateWitness(electionId, witnessSetId, Guid.NewGuid(), 1, "sealed-a", "sealed-a-hash"),
            CreateWitness(electionId, witnessSetId, Guid.NewGuid(), 0, "sealed-b", "sealed-b-hash"),
        };
        var expectedWitnessSetHash = ElectionPublicationWitnessDeletionService.ComputeWitnessSetHash(witnesses);
        var repository = new Mock<IElectionsRepository>(MockBehavior.Strict);
        var updatedWitnesses = new List<ElectionPublicationWitnessRecord>();
        var savedReceipts = new List<ElectionPublicationWitnessDeletionReceiptRecord>();
        ElectionPublicationProofSessionRecord? updatedSession = null;

        repository
            .Setup(x => x.GetPublicationProofSessionsAsync(electionId))
            .ReturnsAsync(new[] { session });
        repository
            .Setup(x => x.GetPublicationProofTranscriptsAsync(electionId))
            .ReturnsAsync(new[] { transcript });
        repository
            .Setup(x => x.GetPublicationWitnessDeletionReceiptsAsync(electionId))
            .ReturnsAsync(Array.Empty<ElectionPublicationWitnessDeletionReceiptRecord>());
        repository
            .Setup(x => x.GetPublicationWitnessesAsync(electionId, witnessSetId))
            .ReturnsAsync(witnesses);
        repository
            .Setup(x => x.UpdatePublicationWitnessAsync(It.IsAny<ElectionPublicationWitnessRecord>()))
            .Callback<ElectionPublicationWitnessRecord>(updatedWitnesses.Add)
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.SavePublicationWitnessDeletionReceiptAsync(It.IsAny<ElectionPublicationWitnessDeletionReceiptRecord>()))
            .Callback<ElectionPublicationWitnessDeletionReceiptRecord>(savedReceipts.Add)
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.UpdatePublicationProofSessionAsync(It.IsAny<ElectionPublicationProofSessionRecord>()))
            .Callback<ElectionPublicationProofSessionRecord>(x => updatedSession = x)
            .Returns(Task.CompletedTask);

        var result = await new ElectionPublicationWitnessDeletionService()
            .TryDeleteVerifiedWitnessesAsync(repository.Object, electionId, deletedAt);

        result.IsCompleted.Should().BeTrue();
        result.Receipt.Should().NotBeNull();
        savedReceipts.Should().ContainSingle();
        savedReceipts[0].WitnessSetHash.Should().Be(expectedWitnessSetHash);
        savedReceipts[0].WitnessCount.Should().Be(witnesses.Length);
        savedReceipts[0].TranscriptHash.Should().Be(transcriptHash);
        savedReceipts[0].ProofHash.Should().Be(proofHash);
        savedReceipts[0].DeletionStatus.Should().Be(ElectionPublicationWitnessDeletionStatus.Completed);
        savedReceipts[0].DeletedAt.Should().Be(deletedAt);

        updatedWitnesses.Should().HaveCount(2);
        updatedWitnesses.Should().OnlyContain(x => x.CustodyStatus == ElectionPublicationWitnessCustodyStatus.Deleted);
        updatedWitnesses.Should().OnlyContain(x => x.DeletedAt == deletedAt);
        updatedWitnesses.Should().OnlyContain(x => x.SealedWitnessMaterial.StartsWith("deleted:sp07-publication-witness:", StringComparison.Ordinal));
        updatedWitnesses.Select(x => x.SealedWitnessMaterialHash).Should().BeEquivalentTo("sealed-a-hash", "sealed-b-hash");

        updatedSession.Should().NotBeNull();
        updatedSession!.Status.Should().Be(ElectionPublicationProofSessionStatus.WitnessDeleted);
        updatedSession.DeletionReceiptId.Should().Be(savedReceipts[0].Id);
        updatedSession.CompletedAt.Should().Be(deletedAt);
    }

    [Fact]
    public async Task TryDeleteVerifiedWitnessesAsync_WhenWitnessWasAlreadyDeletedWithoutReceipt_ShouldNotCreateReceipt()
    {
        var electionId = ElectionId.NewElectionId;
        var proofSessionId = Guid.NewGuid();
        var witnessSetId = Guid.NewGuid();
        var session = CreateSession(electionId, proofSessionId, witnessSetId, "transcript-hash", "proof-hash");
        var transcript = CreateTranscript(electionId, proofSessionId, witnessSetId, "transcript-hash", "proof-hash");
        var witness = CreateWitness(electionId, witnessSetId, Guid.NewGuid(), 0, "deleted:sp07-publication-witness", "sealed-hash")
            with
            {
                CustodyStatus = ElectionPublicationWitnessCustodyStatus.Deleted,
                DeletedAt = DateTime.UtcNow.AddMinutes(-2),
            };
        var repository = new Mock<IElectionsRepository>(MockBehavior.Strict);

        repository
            .Setup(x => x.GetPublicationProofSessionsAsync(electionId))
            .ReturnsAsync(new[] { session });
        repository
            .Setup(x => x.GetPublicationProofTranscriptsAsync(electionId))
            .ReturnsAsync(new[] { transcript });
        repository
            .Setup(x => x.GetPublicationWitnessDeletionReceiptsAsync(electionId))
            .ReturnsAsync(Array.Empty<ElectionPublicationWitnessDeletionReceiptRecord>());
        repository
            .Setup(x => x.GetPublicationWitnessesAsync(electionId, witnessSetId))
            .ReturnsAsync(new[] { witness });

        var result = await new ElectionPublicationWitnessDeletionService()
            .TryDeleteVerifiedWitnessesAsync(repository.Object, electionId, DateTime.UtcNow);

        result.IsCompleted.Should().BeFalse();
        result.FailureCode.Should().Be(VerificationResultCodes.PublicationProofWitnessDeletionInvalid);
        repository.Verify(
            x => x.UpdatePublicationWitnessAsync(It.IsAny<ElectionPublicationWitnessRecord>()),
            Times.Never);
        repository.Verify(
            x => x.SavePublicationWitnessDeletionReceiptAsync(It.IsAny<ElectionPublicationWitnessDeletionReceiptRecord>()),
            Times.Never);
        repository.Verify(
            x => x.UpdatePublicationProofSessionAsync(It.IsAny<ElectionPublicationProofSessionRecord>()),
            Times.Never);
    }

    private static ElectionPublicationProofSessionRecord CreateSession(
        ElectionId electionId,
        Guid proofSessionId,
        Guid witnessSetId,
        string transcriptHash,
        string proofHash) =>
        new(
            proofSessionId,
            electionId,
            witnessSetId,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionPublicationProofSessionStatus.Verified,
            StartedAt: DateTime.UtcNow.AddMinutes(-4),
            CompletedAt: DateTime.UtcNow.AddMinutes(-3),
            AcceptedBallotCount: 2,
            PublishedBallotCount: 2,
            ChunkCount: 1,
            RetryCount: 0,
            FailureCode: null,
            FailureReason: null,
            AcceptedBallotSetHash: "accepted-set-hash",
            PublishedBallotStreamHash: "published-stream-hash",
            TranscriptHash: transcriptHash,
            ProofHash: proofHash,
            ServerVerifierOutputHash: "server-verifier-output-hash",
            DeletionReceiptId: null);

    private static ElectionPublicationProofTranscriptRecord CreateTranscript(
        ElectionId electionId,
        Guid proofSessionId,
        Guid witnessSetId,
        string transcriptHash,
        string proofHash) =>
        new(
            Guid.NewGuid(),
            electionId,
            proofSessionId,
            witnessSetId,
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
            CiphertextSlotCount: 8,
            ProofSystemVersion: ElectionSp07ProfileIds.ProofSystemVersion,
            ProofBytes: "proof-bytes",
            ProofHash: proofHash,
            TranscriptHash: transcriptHash,
            ExternalReviewStatus: ElectionSp07ProfileIds.ExternalReviewStatus,
            GeneratedAt: DateTime.UtcNow.AddMinutes(-2),
            GeneratorReleaseHash: "generator-release-hash",
            VerifierReleaseHash: "verifier-release-hash",
            PublicPrivacyBoundary: ["no_hidden_permutation", "no_raw_witness"]);

    private static ElectionPublicationWitnessRecord CreateWitness(
        ElectionId electionId,
        Guid witnessSetId,
        Guid acceptedBallotId,
        long publishedSequence,
        string sealedWitnessMaterial,
        string sealedWitnessMaterialHash) =>
        new(
            Guid.NewGuid(),
            electionId,
            witnessSetId,
            acceptedBallotId,
            publishedSequence,
            AcceptedEncryptedBallotHash: $"accepted-hash-{publishedSequence}",
            PublishedEncryptedBallotHash: $"published-hash-{publishedSequence}",
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionSp07ProfileIds.ProofSystemVersion,
            sealedWitnessMaterial,
            sealedWitnessMaterialHash,
            SealAlgorithm: "transparent-test",
            ElectionPublicationWitnessCustodyStatus.Sealed,
            CreatedAt: DateTime.UtcNow.AddMinutes(-5),
            DeletedAt: null);
}

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionVerificationContractTests
{
    [Fact]
    public void RootFiles_ShouldCoverRequiredVerifierPackageFiles()
    {
        VerificationPackageFileNames.RootFiles.Should().Equal(
            VerificationPackageFileNames.ElectionRecord,
            VerificationPackageFileNames.AuditPackageManifest,
            VerificationPackageFileNames.VerifierInputManifest,
            VerificationPackageFileNames.VerifierProfile);

        VerificationPackageFileNames.VerifierOutput.Should().Be("verifier-output/VerifierOutput.json");
        VerificationPackageFileNames.VerifierSummary.Should().Be("verifier-output/VerifierSummary.md");
    }

    [Fact]
    public void Profiles_ShouldExposeAllV1ProfileIdentifiers()
    {
        VerificationProfileIds.All.Should().BeEquivalentTo(
        [
            "development_current_v1",
            "public_anonymous_v1",
            "restricted_owner_auditor_v1",
            "high_assurance_v1",
        ]);
    }

    [Fact]
    public void ManifestFileHash_ShouldUseLowercaseSha256OverExactBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("ElectionRecord\n");
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var actual = VerificationCanonicalHash.ComputeManifestFileSha256(bytes);

        actual.Should().Be(expected);
        actual.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void AcceptedBallotInventoryHash_ShouldMatchExistingBoundaryPayloadFormat()
    {
        var electionId = ElectionId.NewElectionId;
        var acceptedBallots = new[]
        {
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                encryptedBallotPackage: "ballot-z",
                proofBundle: "proof-z",
                ballotNullifier: "nullifier-z"),
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                encryptedBallotPackage: "ballot-a",
                proofBundle: "proof-a",
                ballotNullifier: "nullifier-a"),
        };
        var expectedPayload = string.Join(
            '\n',
            [
                $"nullifier-a|{UpperSha256("ballot-a")}|{UpperSha256("proof-a")}",
                $"nullifier-z|{UpperSha256("ballot-z")}|{UpperSha256("proof-z")}",
            ]);
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload));

        VerificationCanonicalHash.BuildAcceptedBallotInventoryPayload(acceptedBallots)
            .Should()
            .Be(expectedPayload);
        VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots)
            .Should()
            .Equal(expectedHash);
    }

    [Fact]
    public void PublishedBallotStreamHash_ShouldMatchExistingBoundaryPayloadFormat()
    {
        var electionId = ElectionId.NewElectionId;
        var publishedBallots = new[]
        {
            ElectionModelFactory.CreatePublishedBallotRecord(
                electionId,
                publicationSequence: 2,
                encryptedBallotPackage: "published-b",
                proofBundle: "proof-b"),
            ElectionModelFactory.CreatePublishedBallotRecord(
                electionId,
                publicationSequence: 1,
                encryptedBallotPackage: "published-a",
                proofBundle: "proof-a"),
        };
        var expectedPayload = string.Join(
            '\n',
            [
                $"1|{UpperSha256("published-a")}|{UpperSha256("proof-a")}",
                $"2|{UpperSha256("published-b")}|{UpperSha256("proof-b")}",
            ]);
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload));

        VerificationCanonicalHash.BuildPublishedBallotStreamPayload(publishedBallots)
            .Should()
            .Be(expectedPayload);
        VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots)
            .Should()
            .Equal(expectedHash);
    }

    [Fact]
    public void PublicPrivacyBoundary_ShouldDetectRestrictedFieldNames()
    {
        var fields = new[]
        {
            "election_id",
            "organizationVoterId",
            "rawTrusteeShare",
            "published_ballot_hash",
        };

        VerificationPrivacyBoundary.FindForbiddenPublicFields(fields)
            .Should()
            .Equal("organizationVoterId", "rawTrusteeShare");
    }

    [Fact]
    public void RestrictedArtifactEntry_ShouldBeDetectedFromVisibilityOrPath()
    {
        var publicEntry = CreateEntry("artifacts/election-record/accepted-ballot-set.json");
        var restrictedEntry = CreateEntry(
            "artifacts/restricted/roster-checkoff.json",
            VerificationArtifactVisibility.Restricted);
        var pathRestrictedEntry = CreateEntry("artifacts/restricted/custom.json");

        VerificationPrivacyBoundary.IsRestrictedArtifactEntry(publicEntry).Should().BeFalse();
        VerificationPrivacyBoundary.IsRestrictedArtifactEntry(restrictedEntry).Should().BeTrue();
        VerificationPrivacyBoundary.IsRestrictedArtifactEntry(pathRestrictedEntry).Should().BeTrue();
    }

    [Fact]
    public void ExitCodeMapping_ShouldKeepWarnAsSuccessfulProcessWithVerifierWarnings()
    {
        VerificationExitCodes.FromOverallStatus(VerificationOverallStatus.Pass)
            .Should()
            .Be(0);
        VerificationExitCodes.FromOverallStatus(VerificationOverallStatus.Warn)
            .Should()
            .Be(0);
        VerificationExitCodes.FromOverallStatus(VerificationOverallStatus.Fail)
            .Should()
            .Be(1);
        VerificationExitCodes.FromOverallStatus(VerificationOverallStatus.NotAvailable)
            .Should()
            .Be(2);
    }

    private static AuditPackageManifestEntryRecord CreateEntry(
        string path,
        VerificationArtifactVisibility visibility = VerificationArtifactVisibility.Public) =>
        new(
            path,
            Sha256Hash: new string('a', 64),
            SizeBytes: 12,
            MediaType: "application/json",
            visibility,
            VerificationEvidenceRequirement.Required,
            RequiredProfileIds:
            [
                VerificationProfileIds.DevelopmentCurrentV1,
            ]);

    private static string UpperSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

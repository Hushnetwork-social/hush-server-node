using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class HushVotingPackageVerifierTamperTests
{
    public static TheoryData<string, string, string, VerificationCheckStatus, VerificationOverallStatus, int> TamperFixtures =>
        new()
        {
            {
                "tamper-missing-artifact",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PackageManifestMissingArtifact,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-artifact-hash",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PackageManifestArtifactHashMismatch,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-wrong-election-id",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.ElectionIdMismatch,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-duplicate-nullifier",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.AcceptedBallotDuplicateNullifier,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-accepted-set-hash",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.AcceptedBallotInventoryHashMismatch,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-published-stream-sequence",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PublishedBallotSequenceInvalid,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-published-stream-hash",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PublishedBallotStreamHashMismatch,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-named-voter-in-public-artifact",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PublicRestrictedFieldLeak,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "tamper-raw-trustee-share",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PublicRestrictedFieldLeak,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
            {
                "missing-sp07-development-warning",
                VerificationProfileIds.DevelopmentCurrentV1,
                VerificationResultCodes.PublicationProofPendingFeat117,
                VerificationCheckStatus.Warn,
                VerificationOverallStatus.Warn,
                0
            },
            {
                "missing-sp07-high-assurance-fail-closed",
                VerificationProfileIds.HighAssuranceV1,
                VerificationResultCodes.PublicationProofPendingFeat117,
                VerificationCheckStatus.Fail,
                VerificationOverallStatus.Fail,
                1
            },
        };

    [Theory]
    [MemberData(nameof(TamperFixtures))]
    public async Task Verify_TamperFixtureCatalog_ShouldReturnExpectedResultCode(
        string fixtureName,
        string profileId,
        string expectedResultCode,
        VerificationCheckStatus expectedCheckStatus,
        VerificationOverallStatus expectedOverallStatus,
        int expectedExitCode)
    {
        using var package = CreatePackage(profileId);
        await ApplyTamperFixtureAsync(package.PackagePath, fixtureName);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            profileId));

        result.Output.OverallStatus.Should().Be(expectedOverallStatus, fixtureName);
        result.ExitCode.Should().Be(expectedExitCode, fixtureName);
        result.Output.Results.Should().Contain(
            x => x.ResultCode == expectedResultCode && x.Status == expectedCheckStatus,
            fixtureName);
    }

    private static TemporaryPackageDirectory CreatePackage(string profileId)
    {
        var directory = new TemporaryPackageDirectory();
        var export = new ElectionVerificationPackageExportService().Export(
            ElectionVerificationPackageExportServiceTests.CreateRequest(
                VerificationPackageView.PublicAnonymous,
                profileId: profileId));
        ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
        return directory;
    }

    private static async Task ApplyTamperFixtureAsync(string packagePath, string fixtureName)
    {
        switch (fixtureName)
        {
            case "tamper-missing-artifact":
                File.Delete(ResolvePackagePath(packagePath, VerificationPackageFileNames.AcceptedBallotSet));
                return;

            case "tamper-artifact-hash":
                await AddJsonPropertyAsync(
                    packagePath,
                    VerificationPackageFileNames.ResultBinding,
                    "tamperMarker",
                    "artifact hash changed without updating the manifest");
                return;

            case "tamper-wrong-election-id":
                var accepted = await ReadArtifactAsync<AcceptedBallotSetArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.AcceptedBallotSet);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.AcceptedBallotSet,
                    accepted with { ElectionId = Guid.NewGuid().ToString() });
                return;

            case "tamper-duplicate-nullifier":
                var duplicateAccepted = await ReadArtifactAsync<AcceptedBallotSetArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.AcceptedBallotSet);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.AcceptedBallotSet,
                    duplicateAccepted with
                    {
                        AcceptedBallots =
                        [
                            duplicateAccepted.AcceptedBallots[0],
                            duplicateAccepted.AcceptedBallots[1] with
                            {
                                BallotNullifier = duplicateAccepted.AcceptedBallots[0].BallotNullifier,
                            },
                        ],
                    });
                return;

            case "tamper-accepted-set-hash":
                var acceptedHash = await ReadArtifactAsync<AcceptedBallotSetArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.AcceptedBallotSet);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.AcceptedBallotSet,
                    acceptedHash with { AcceptedBallotInventoryHash = new string('0', 64) });
                return;

            case "tamper-published-stream-sequence":
                var publishedSequence = await ReadArtifactAsync<PublishedBallotStreamArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.PublishedBallotStream);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.PublishedBallotStream,
                    publishedSequence with
                    {
                        PublishedBallots =
                        [
                            publishedSequence.PublishedBallots[0] with
                            {
                                PublicationSequence = 2,
                            },
                            publishedSequence.PublishedBallots[1],
                        ],
                    });
                return;

            case "tamper-published-stream-hash":
                var publishedHash = await ReadArtifactAsync<PublishedBallotStreamArtifactRecord>(
                    packagePath,
                    VerificationPackageFileNames.PublishedBallotStream);
                await WriteArtifactAsync(
                    packagePath,
                    VerificationPackageFileNames.PublishedBallotStream,
                    publishedHash with { PublishedBallotStreamHash = new string('0', 64) });
                return;

            case "tamper-named-voter-in-public-artifact":
                await AddJsonPropertyAsync(
                    packagePath,
                    VerificationPackageFileNames.ElectionRecord,
                    "organizationVoterId",
                    "voter-1");
                return;

            case "tamper-raw-trustee-share":
                await AddJsonPropertyAsync(
                    packagePath,
                    VerificationPackageFileNames.TrusteeReleaseEvidence,
                    "rawTrusteeShare",
                    "raw-share-material");
                return;

            case "missing-sp07-development-warning":
            case "missing-sp07-high-assurance-fail-closed":
                return;

            default:
                throw new InvalidOperationException($"Unknown tamper fixture '{fixtureName}'.");
        }
    }

    private static async Task<T> ReadArtifactAsync<T>(string packagePath, string relativePath) =>
        JsonSerializer.Deserialize<T>(
            await File.ReadAllTextAsync(ResolvePackagePath(packagePath, relativePath)),
            VerificationJson.Options)!;

    private static async Task WriteArtifactAsync<T>(string packagePath, string relativePath, T value) =>
        await File.WriteAllTextAsync(
            ResolvePackagePath(packagePath, relativePath),
            JsonSerializer.Serialize(value, VerificationJson.Options));

    private static async Task AddJsonPropertyAsync(
        string packagePath,
        string relativePath,
        string propertyName,
        string propertyValue)
    {
        var path = ResolvePackagePath(packagePath, relativePath);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject() ??
            throw new InvalidOperationException($"Package artifact '{relativePath}' is not a JSON object.");
        node[propertyName] = propertyValue;
        await File.WriteAllTextAsync(path, node.ToJsonString(VerificationJson.Options));
    }

    private static string ResolvePackagePath(string packagePath, string relativePath) =>
        Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class TemporaryPackageDirectory : IDisposable
    {
        public string PackagePath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"hush-verifier-{Guid.NewGuid():N}");

        public TemporaryPackageDirectory()
        {
            Directory.CreateDirectory(PackagePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(PackagePath))
            {
                Directory.Delete(PackagePath, recursive: true);
            }
        }
    }
}

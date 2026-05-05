using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class HushVotingPackageVerifierTests
{
    [Fact]
    public async Task Verify_ValidDevelopmentPackage_ShouldWriteDeterministicOutputAndExitSuccessfully()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(0);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        File.Exists(Path.Combine(package.PackagePath, "verifier-output", "VerifierOutput.json"))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(package.PackagePath, "verifier-output", "VerifierSummary.md"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task Verify_HighAssurancePackageMissingPublicationProof_ShouldFailClosed()
    {
        using var package = CreatePackage(VerificationProfileIds.HighAssuranceV1);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofPendingFeat117 &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_MissingArtifact_ShouldFailWithManifestMissingArtifact()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        File.Delete(Path.Combine(package.PackagePath, VerificationPackageFileNames.AcceptedBallotSet.Replace('/', Path.DirectorySeparatorChar)));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PackageManifestMissingArtifact);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Verify_TamperedAcceptedSetHash_ShouldFailWithAcceptedHashCode()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var path = Path.Combine(package.PackagePath, VerificationPackageFileNames.AcceptedBallotSet.Replace('/', Path.DirectorySeparatorChar));
        var accepted = JsonSerializer.Deserialize<AcceptedBallotSetArtifactRecord>(
            await File.ReadAllTextAsync(path),
            VerificationJson.Options)!;
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                accepted with { AcceptedBallotInventoryHash = new string('0', 64) },
                VerificationJson.Options));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.AcceptedBallotInventoryHashMismatch);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Verify_NamedVoterInPublicArtifact_ShouldFailPrivacyBoundary()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var path = Path.Combine(package.PackagePath, VerificationPackageFileNames.ElectionRecord.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(path, "{\"electionId\":\"x\",\"organizationVoterId\":\"voter-1\"}");

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicRestrictedFieldLeak);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Verify_PublishedSequenceGap_ShouldFailWithPublishedSequenceCode()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var path = Path.Combine(package.PackagePath, VerificationPackageFileNames.PublishedBallotStream.Replace('/', Path.DirectorySeparatorChar));
        var published = JsonSerializer.Deserialize<PublishedBallotStreamArtifactRecord>(
            await File.ReadAllTextAsync(path),
            VerificationJson.Options)!;
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                published with
                {
                    PublishedBallots =
                    [
                        published.PublishedBallots[0] with
                        {
                            PublicationSequence = 2,
                        },
                    ],
                },
                VerificationJson.Options));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublishedBallotSequenceInvalid);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task CommandLine_BackendUrl_ShouldBeRejectedAsLiveDependency()
    {
        using var output = new TemporaryPackageDirectory();

        var exitCode = await HushVotingVerifierCommandLine.RunAsync(
            [
                "--package",
                "https://hush.example/api",
                "--profile",
                VerificationProfileIds.DevelopmentCurrentV1,
                "--output",
                output.PackagePath,
            ]);

        exitCode.Should().Be(1);
        var verifierOutput = JsonSerializer.Deserialize<VerifierOutputRecord>(
            await File.ReadAllTextAsync(Path.Combine(output.PackagePath, "VerifierOutput.json")),
            VerificationJson.Options)!;
        verifierOutput.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.UnsupportedLiveDependency);
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

using System.Text.Json.Nodes;
using FluentAssertions;
using RetentionLogPrivacyProofPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class RetentionLogPrivacyProofPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 5, 21, 17, 0, 0, TimeSpan.Zero);

    private static readonly RetentionLogPrivacyProofSourceRefs FixedSourceRefs = new(
        "hush-server-node:test-server",
        "hush-memory-bank:test-memory",
        "hush-documents:test-docs");

    [Fact]
    public void Generate_ReturnsRequiredArtifactsAndAcceptedStatus()
    {
        var generated = RetentionLogPrivacyProofGenerator.Generate(FixedGeneratedAt, FixedSourceRefs);

        generated.Status.Should().Be("accepted");
        generated.Artifacts.Select(x => x.RelativePath)
            .Should().BeEquivalentTo(RetentionLogPrivacyProofContracts.RequiredArtifactPaths);
        generated.CheckResult.Checks.Select(x => x.CheckId)
            .Should().Contain(RetentionLogPrivacyProofContracts.RequiredCheckIds);
        generated.ScanFindings.Should().BeEmpty();
        RetentionLogPrivacyProofContracts.ValidateGeneratedPackage(generated).Should().BeEmpty();
    }

    [Fact]
    public void Generate_IsStableForSameInputs()
    {
        var first = RetentionLogPrivacyProofGenerator.Generate(FixedGeneratedAt, FixedSourceRefs);
        var second = RetentionLogPrivacyProofGenerator.Generate(FixedGeneratedAt, FixedSourceRefs);

        first.Artifacts
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .Select(x => new { x.RelativePath, x.Sha256Hash, x.Content })
            .Should()
            .Equal(second.Artifacts
                .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                .Select(x => new { x.RelativePath, x.Sha256Hash, x.Content }));
    }

    [Fact]
    public void GeneratedExternalArtifacts_DoNotContainInternalCodesOrLocalPaths()
    {
        var generated = RetentionLogPrivacyProofGenerator.Generate(FixedGeneratedAt, FixedSourceRefs);

        var generatedText = string.Join('\n', generated.Artifacts.Select(x => x.Content));
        generatedText.Should().NotContain("FEAT-");
        generatedText.Should().NotContain("EPIC-");
        generatedText.Should().NotContain("C:\\");
        generatedText.Should().NotContain("C:/");
        generatedText.Should().NotContain("\\myWork\\");
    }

    [Fact]
    public void Scanner_DetectsDeliberateForbiddenIdentityToBallotFixture()
    {
        var findings = RetentionLogPrivacyProofContracts.ScanText(
            "fixture.txt",
            "fixture",
            "organizationVoterId=fixture-voter; receiptCommitment=fixture-receipt");

        findings.Should().ContainSingle(x => x.Category == "identity_to_ballot_join");
        RetentionLogPrivacyProofContracts.DeliberateForbiddenFixtureIsDetected().Should().BeTrue();
    }

    [Fact]
    public void Promotion_ValidateOnly_DoesNotWriteFiles()
    {
        using var workspace = TempRetentionLogPrivacyProofWorkspace.Create();

        var result = new RetentionLogPrivacyProofPromotionService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyProofPromotionService.ModeValidateOnly,
            FixedGeneratedAt,
            null,
            FixedSourceRefs.ServerNodeCommitRef,
            FixedSourceRefs.MemoryBankCommitRef,
            FixedSourceRefs.DocumentsCommitRef,
            ValidateOnly: true));

        result.Mode.Should().Be(RetentionLogPrivacyProofPromotionService.ModeValidateOnly);
        result.Status.Should().Be("accepted");
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(workspace.Paths.PackageOutputRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_Package_WritesArtifactsAndValidatesOutputFolder()
    {
        using var workspace = TempRetentionLogPrivacyProofWorkspace.Create();

        var result = new RetentionLogPrivacyProofPromotionService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyProofPromotionService.ModePackage,
            FixedGeneratedAt,
            null,
            FixedSourceRefs.ServerNodeCommitRef,
            FixedSourceRefs.MemoryBankCommitRef,
            FixedSourceRefs.DocumentsCommitRef,
            ValidateOnly: false));

        result.WrittenFiles.Should().HaveCount(RetentionLogPrivacyProofContracts.RequiredArtifactPaths.Length);
        File.Exists(Path.Combine(workspace.Paths.PackageOutputRoot, RetentionLogPrivacyProofContracts.PackagePath))
            .Should().BeTrue();
        RetentionLogPrivacyProofPromotionService.ValidateOutputFolder(workspace.Paths.PackageOutputRoot)
            .Should().BeEmpty();
    }

    [Fact]
    public void ValidateOutputFolder_WithMissingArtifact_ReturnsError()
    {
        using var workspace = TempRetentionLogPrivacyProofWorkspace.Create();
        new RetentionLogPrivacyProofPromotionService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyProofPromotionService.ModePackage,
            FixedGeneratedAt,
            null,
            FixedSourceRefs.ServerNodeCommitRef,
            FixedSourceRefs.MemoryBankCommitRef,
            FixedSourceRefs.DocumentsCommitRef,
            ValidateOnly: false));
        File.Delete(Path.Combine(workspace.Paths.PackageOutputRoot, RetentionLogPrivacyProofContracts.AtomicCastProofPath));

        RetentionLogPrivacyProofPromotionService.ValidateOutputFolder(workspace.Paths.PackageOutputRoot)
            .Should().Contain(x => x.Contains("Missing required artifact", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateOutputFolder_WithTamperedArtifactHash_ReturnsError()
    {
        using var workspace = TempRetentionLogPrivacyProofWorkspace.Create();
        new RetentionLogPrivacyProofPromotionService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyProofPromotionService.ModePackage,
            FixedGeneratedAt,
            null,
            FixedSourceRefs.ServerNodeCommitRef,
            FixedSourceRefs.MemoryBankCommitRef,
            FixedSourceRefs.DocumentsCommitRef,
            ValidateOnly: false));
        var packagePath = Path.Combine(workspace.Paths.PackageOutputRoot, RetentionLogPrivacyProofContracts.PackagePath);
        var package = JsonNode.Parse(File.ReadAllText(packagePath))!.AsObject();
        var artifactHashes = package["artifactHashes"]!.AsArray();
        artifactHashes[0]!.AsObject()["sha256Hash"] = new string('0', 64);
        File.WriteAllText(packagePath, RetentionLogPrivacyProofContracts.CanonicalJson(package));

        RetentionLogPrivacyProofPromotionService.ValidateOutputFolder(workspace.Paths.PackageOutputRoot)
            .Should().Contain(x => x.Contains("Artifact hash mismatch", StringComparison.Ordinal));
    }

    private sealed class TempRetentionLogPrivacyProofWorkspace : IDisposable
    {
        private TempRetentionLogPrivacyProofWorkspace(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "hush-server-node"));
            Directory.CreateDirectory(Path.Combine(root, "hush-memory-bank"));
            Directory.CreateDirectory(Path.Combine(root, "hush-documents"));
            Paths = RetentionLogPrivacyProofPromotionPaths.FromWorkspaceRoot(root);
        }

        public string Root { get; }

        public RetentionLogPrivacyProofPromotionPaths Paths { get; }

        public static TempRetentionLogPrivacyProofWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "hush-rlp-tests", Guid.NewGuid().ToString("N"));
            return new TempRetentionLogPrivacyProofWorkspace(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

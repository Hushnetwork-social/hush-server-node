using System.Text.Json.Nodes;
using FluentAssertions;
using PublicationCountingReplayPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class PublicationCountingReplayPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();

        var errors = PublicationCountingReplayContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in PublicationCountingReplayContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void ReleaseBaseline_SourceAndCurrentRefs_AreValid()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();

        var errors = PublicationCountingReplayContracts.ValidateSource(source)
            .Concat(PublicationCountingReplayContracts.ValidateCurrentRefs(workspace.Paths, source))
            .ToArray();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Promotion_ValidateOnly_ShouldNotWritePackageRoot()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(PublicationCountingReplayPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().BeEmpty();
        Directory.Exists(workspace.DefaultOutputPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldFailWhenPackageRootIsMissing()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();

        var act = () => CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingReplayPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("Package root does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_OutputRootOutsideWorkspace_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var outsideRoot = Path.Combine(Path.GetTempPath(), "feat160-outside-" + Guid.NewGuid().ToString("N"));

        var act = () => CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModePackage,
            null,
            outsideRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingReplayPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("FEAT-160 replay output root", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_PackageMode_WritesDeterministicReplayBindingArtifacts()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var service = CreateService();

        var first = service.Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        var second = service.Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        first.Status.Should().Be("candidate");
        first.WrittenFiles.Should().HaveCount(PublicationCountingReplayArtifactGenerator.RequiredArtifactPaths.Length);
        first.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
        foreach (var relativePath in PublicationCountingReplayArtifactGenerator.RequiredArtifactPaths)
        {
            File.Exists(Path.Combine(workspace.DefaultOutputPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(relativePath);
        }

        var manifest = PublicationCountingReplayContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, PublicationCountingReplayArtifactGenerator.ManifestPath),
            "generated manifest");
        manifest["readinessProposal"]!.AsObject()["status"]!.GetValue<string>().Should().Be("candidate");
        manifest["replaySummary"]!.AsObject()["evidenceMode"]!.GetValue<string>().Should().Be("verifier_replay");
        manifest["replaySummary"]!.AsObject()["status"]!.GetValue<string>().Should().Be("pass");
        manifest["tamperSummary"]!.AsObject()["evidenceMode"]!.GetValue<string>().Should().Be("verifier_tamper_replay");
        manifest["tamperSummary"]!.AsObject()["status"]!.GetValue<string>().Should().Be("pass");
        manifest["entries"]!.AsArray().Count.Should().Be(PublicationCountingReplayArtifactGenerator.RequiredArtifactPaths.Length - 1);

        var goodReplay = PublicationCountingReplayContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, PublicationCountingReplayArtifactGenerator.GoodProfileReplaySummaryPath),
            "good replay summary");
        goodReplay["status"]!.GetValue<string>().Should().Be("pass");
        goodReplay["evidenceMode"]!.GetValue<string>().Should().Be("verifier_replay");
        goodReplay["cases"]!.AsArray().Should().HaveCount(PublicationCountingReplayContracts.RequiredGoodProfileIds.Length);

        var tamperReplay = PublicationCountingReplayContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath),
            "tamper replay summary");
        tamperReplay["status"]!.GetValue<string>().Should().Be("pass");
        tamperReplay["evidenceMode"]!.GetValue<string>().Should().Be("verifier_tamper_replay");
        tamperReplay["cases"]!.AsArray().Should().HaveCount(workspace.LoadSource()["negativeMatrix"]!.AsArray().Count);

        var bindingSummary = PublicationCountingReplayContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, PublicationCountingReplayArtifactGenerator.GeneratedReportBindingSummaryPath),
            "generated binding summary");
        bindingSummary["requiredBindingTypes"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain(["package-hash", "tally-output", "package-verifier-output", "runtime-verifier-output", "generated-report"]);
        PublicationCountingReplayBindingValidator.ValidateGeneratedPackageBindings(first.GeneratedPackage)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsExistingPackageDrift()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        File.WriteAllText(
            Path.Combine(workspace.DefaultOutputPackageRoot, PublicationCountingReplayArtifactGenerator.ReadmePath),
            "drifted package");

        var act = () => service.Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingReplayPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(PublicationCountingReplayArtifactGenerator.ReadmePath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("package-hash")]
    [InlineData("tally-output")]
    [InlineData("package-verifier-output")]
    [InlineData("runtime-verifier-output")]
    public void BindingValidation_MissingCaseBinding_Fails(string bindingType)
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var generated = CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false)).GeneratedPackage;
        var replaySummary = GeneratedArtifactObject(generated, PublicationCountingReplayArtifactGenerator.GoodProfileReplaySummaryPath);
        var firstCase = replaySummary["cases"]!.AsArray()[0]!.AsObject();
        var bindings = firstCase["artifactBindings"]!.AsArray();
        for (var index = bindings.Count - 1; index >= 0; index--)
        {
            if (bindings[index] is JsonObject binding &&
                string.Equals(PublicationCountingReplayContracts.GetString(binding, "bindingType"), bindingType, StringComparison.Ordinal))
            {
                bindings.RemoveAt(index);
            }
        }

        var broken = ReplaceGeneratedArtifact(
            generated,
            PublicationCountingReplayArtifactGenerator.GoodProfileReplaySummaryPath,
            replaySummary);

        PublicationCountingReplayBindingValidator.ValidateGeneratedPackageBindings(broken)
            .Should()
            .Contain(error => error.Contains(bindingType, StringComparison.Ordinal));
    }

    [Fact]
    public void BindingValidation_MissingGeneratedReportBinding_Fails()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var generated = CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false)).GeneratedPackage;
        var bindingSummary = GeneratedArtifactObject(generated, PublicationCountingReplayArtifactGenerator.GeneratedReportBindingSummaryPath);
        bindingSummary["boundArtifacts"] = new JsonArray();

        var broken = ReplaceGeneratedArtifact(
            generated,
            PublicationCountingReplayArtifactGenerator.GeneratedReportBindingSummaryPath,
            bindingSummary);

        PublicationCountingReplayBindingValidator.ValidateGeneratedPackageBindings(broken)
            .Should()
            .Contain(error => error.Contains(PublicationCountingReplayArtifactGenerator.GoodProfileReplaySummaryPath, StringComparison.Ordinal));
    }

    [Fact]
    public void BindingValidation_MissingTamperChangedArtifactReference_Fails()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var generated = CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false)).GeneratedPackage;
        var tamperSummary = GeneratedArtifactObject(generated, PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath);
        tamperSummary["cases"]!.AsArray()[0]!.AsObject()["changedArtifactRefs"] = new JsonArray();

        var broken = ReplaceGeneratedArtifact(
            generated,
            PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath,
            tamperSummary);

        PublicationCountingReplayBindingValidator.ValidateGeneratedPackageBindings(broken)
            .Should()
            .Contain(error => error.Contains("changed-artifact", StringComparison.Ordinal));
    }

    [Fact]
    public void BindingValidation_MissingTamperNormalizedOutputHash_Fails()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var generated = CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false)).GeneratedPackage;
        var tamperSummary = GeneratedArtifactObject(generated, PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath);
        tamperSummary["cases"]!.AsArray()[0]!.AsObject()["normalizedOutputHash"] = "";

        var broken = ReplaceGeneratedArtifact(
            generated,
            PublicationCountingReplayArtifactGenerator.TamperReplaySummaryPath,
            tamperSummary);

        PublicationCountingReplayBindingValidator.ValidateGeneratedPackageBindings(broken)
            .Should()
            .Contain(error => error.Contains("normalizedOutputHash", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_TamperReplayMismatch_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var service = new PublicationCountingReplayPromotionService(
            new FakeGoodProfileReplayRunner(),
            new FailingNegativeReplayRunner());

        var act = () => service.Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingReplayPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("unexpectedly passed", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_StaleReadinessBaseline_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        source["baselineRegister"]!.AsObject()["registerVersionId"] = "RDY-REG-v0.1.6";

        var errors = PublicationCountingReplayContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT160_STALE_READINESS_BASELINE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StaleFeat153ManifestHash_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        source["upstreamBaselines"]!.AsObject()["feat153"]!.AsObject()["manifestHash"] =
            "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-feat153-source.json");

        var act = () => CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModePackage,
            sourceInput,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingReplayPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("upstreamBaselines.feat153.manifestHash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CurrentnessValidation_StaleFeat158FixtureIndexHash_BlocksPromotion()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        source["upstreamBaselines"]!.AsObject()["feat158"]!.AsObject()["fixtureIndexHash"] =
            "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var sourceInput = await workspace.WriteSourceAsync(source, "stale-feat158-source.json");

        var act = () => CreateService().Promote(new(
            workspace.Paths,
            PublicationCountingReplayPromotionService.ModePackage,
            sourceInput,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<PublicationCountingReplayPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("upstreamBaselines.feat158.fixtureIndexHash", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_LocalAbsolutePath_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        var goodProfiles = source["replayMatrix"]!.AsObject()["goodProfiles"]!.AsArray();
        goodProfiles[0]!.AsObject()["packagePath"] = @"C:\private\package";

        var errors = PublicationCountingReplayContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT160_LOCAL_ABSOLUTE_PATH_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivateMaterialField_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        source["publicSafety"]!.AsObject()["publicBoundaryStatement"] = "This leaks a private key seed phrase.";

        var errors = PublicationCountingReplayContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT160_PRIVATE_MATERIAL_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DirectRegisterMutation_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        source["scorePolicy"]!.AsObject()["directRegisterMutation"] = true;

        var errors = PublicationCountingReplayContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT160_DIRECT_REGISTER_MUTATION_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingNegativeExpectedResultCode_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        source["negativeMatrix"]!.AsArray()[0]!.AsObject()["expectedPrimaryResultCode"] = "";

        var errors = PublicationCountingReplayContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("expectedPrimaryResultCode", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingRequiredNegativeCase_IsRejected()
    {
        using var workspace = TempPublicationCountingReplayWorkspace.Create();
        var source = workspace.LoadSource();
        var negativeMatrix = source["negativeMatrix"]!.AsArray();
        var index = negativeMatrix
            .Select((node, itemIndex) => (node, itemIndex))
            .First(item => item.node!.AsObject()["fixtureId"]!.GetValue<string>() == "tamper-trustee-release-wrong-target")
            .itemIndex;
        negativeMatrix.RemoveAt(index);

        var errors = PublicationCountingReplayContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT160_NEGATIVE_CASE_MISSING", StringComparison.Ordinal));
    }

    private static PublicationCountingReplayPromotionService CreateService() =>
        new(new FakeGoodProfileReplayRunner(), new FakeNegativeReplayRunner());

    private static JsonObject GeneratedArtifactObject(
        PublicationCountingReplayGeneratedPackage generated,
        string relativePath)
    {
        var artifact = generated.Artifacts.Single(item => item.RelativePath == relativePath);
        return JsonNode.Parse(artifact.Content)!.AsObject();
    }

    private static PublicationCountingReplayGeneratedPackage ReplaceGeneratedArtifact(
        PublicationCountingReplayGeneratedPackage generated,
        string relativePath,
        JsonObject replacement)
    {
        var artifacts = generated.Artifacts
            .Select(item => item.RelativePath == relativePath
                ? new PublicationCountingReplayArtifact(relativePath, PublicationCountingReplayContracts.CanonicalJson(replacement))
                : item)
            .ToArray();
        return generated with { Artifacts = artifacts };
    }

    private sealed class FakeGoodProfileReplayRunner : IPublicationCountingReplayProfileRunner
    {
        public PublicationCountingGoodProfileReplaySet ReplayGoodProfiles(
            PublicationCountingReplayPromotionPaths paths,
            JsonObject source)
        {
            var cases = PublicationCountingReplayContracts.RequireArray(
                    PublicationCountingReplayContracts.RequireObject(source, "replayMatrix"),
                    "goodProfiles")
                .OfType<JsonObject>()
                .Select(BuildCase)
                .ToArray();
            return new PublicationCountingGoodProfileReplaySet("pass", cases, []);
        }

        private static PublicationCountingGoodProfileReplayCase BuildCase(JsonObject profile)
        {
            var fixtureId = PublicationCountingReplayContracts.GetString(profile, "fixtureId");
            var packageHash = PublicationCountingReplayContracts.GetString(profile, "packageHash");
            var normalizedOutputHash = PublicationCountingReplayContracts.GetString(profile, "normalizedOutputHash");
            return new PublicationCountingGoodProfileReplayCase(
                fixtureId,
                "matched",
                "public_anonymous_v1",
                PublicationCountingReplayContracts.GetString(profile, "packagePath"),
                packageHash,
                packageHash,
                packageHash,
                "matched",
                PublicationCountingReplayContracts.GetString(profile, "expectedResultRef"),
                PublicationCountingReplayContracts.GetString(profile, "expectedOverallStatus"),
                PublicationCountingReplayContracts.GetString(profile, "expectedOverallStatus"),
                PublicationCountingReplayContracts.GetInt(profile, "expectedExitCode"),
                PublicationCountingReplayContracts.GetInt(profile, "expectedExitCode"),
                PublicationCountingReplayContracts.GetString(profile, "expectedPrimaryResultCode"),
                PublicationCountingReplayContracts.GetString(profile, "expectedPrimaryResultCode"),
                normalizedOutputHash,
                normalizedOutputHash,
                "matched",
                [
                    new("package-hash", ".", packageHash),
                    new("tally-output", "artifacts/election-record/tally-replay.json", HashFor(fixtureId + "-tally")),
                    new("package-verifier-output", "artifacts/election-record/publication-proof-verifier-output.json", HashFor(fixtureId + "-publication-verifier-output")),
                    new("package-verifier-output", "artifacts/election-record/trustee-verifier-output.json", HashFor(fixtureId + "-trustee-verifier-output")),
                    new("runtime-verifier-output", "normalized-verifier-output", normalizedOutputHash),
                    new("result-binding", "artifacts/election-record/result-binding.json", HashFor(fixtureId + "-result-binding")),
                ],
                [],
                []);
        }

        private static string HashFor(string value) =>
            "sha256:" + PublicationCountingReplayContracts.Sha256Hex(value);
    }

    private sealed class FakeNegativeReplayRunner : IPublicationCountingReplayNegativeRunner
    {
        public PublicationCountingNegativeReplaySet ReplayNegativeCases(
            PublicationCountingReplayPromotionPaths paths,
            JsonObject source)
        {
            var cases = PublicationCountingReplayContracts.RequireArray(source, "negativeMatrix")
                .OfType<JsonObject>()
                .Select(BuildCase)
                .ToArray();
            return new PublicationCountingNegativeReplaySet("pass", cases, []);
        }

        private static PublicationCountingNegativeReplayCase BuildCase(JsonObject item)
        {
            var fixtureId = PublicationCountingReplayContracts.GetString(item, "fixtureId");
            var normalizedOutputHash = HashFor(fixtureId + "-negative-output");
            return new PublicationCountingNegativeReplayCase(
                PublicationCountingReplayContracts.GetString(item, "caseId"),
                fixtureId,
                PublicationCountingReplayContracts.GetString(item, "source"),
                PublicationCountingReplayContracts.GetString(item, "coverageArea"),
                "matched",
                "public_anonymous_v1",
                "fake:" + fixtureId,
                HashFor(fixtureId + "-package"),
                PublicationCountingReplayContracts.GetString(item, "changedArtifactOrCondition"),
                ["artifacts/election-record/" + fixtureId + ".json"],
                "expected-results/" + fixtureId + ".json",
                PublicationCountingReplayContracts.GetString(item, "expectedOverallStatus"),
                PublicationCountingReplayContracts.GetString(item, "expectedOverallStatus"),
                PublicationCountingReplayContracts.GetInt(item, "expectedExitCode"),
                PublicationCountingReplayContracts.GetInt(item, "expectedExitCode"),
                PublicationCountingReplayContracts.GetString(item, "expectedPrimaryResultCode"),
                PublicationCountingReplayContracts.GetString(item, "expectedPrimaryResultCode"),
                normalizedOutputHash,
                normalizedOutputHash,
                "matched",
                PublicationCountingReplayContracts.GetBool(item, "blocksScoreMovement"),
                []);
        }

        private static string HashFor(string value) =>
            "sha256:" + PublicationCountingReplayContracts.Sha256Hex(value);
    }

    private sealed class FailingNegativeReplayRunner : IPublicationCountingReplayNegativeRunner
    {
        public PublicationCountingNegativeReplaySet ReplayNegativeCases(
            PublicationCountingReplayPromotionPaths paths,
            JsonObject source)
        {
            var mismatch = new PublicationCountingNegativeReplayCase(
                "NEG-ACCEPTED-SET-HASH",
                "tamper-accepted-set-hash",
                "existing_v0.3.0",
                "accepted_set",
                "mismatch",
                "public_anonymous_v1",
                "fake:tamper-accepted-set-hash",
                HashFor("tamper-accepted-set-hash-package"),
                "test condition",
                ["artifacts/election-record/accepted-ballot-set.json"],
                "expected-results/tamper-accepted-set-hash.json",
                "fail",
                "pass",
                1,
                0,
                "accepted_ballot_inventory_hash_mismatch",
                "package_structure_valid",
                HashFor("tamper-accepted-set-hash-negative-output"),
                HashFor("tamper-accepted-set-hash-pass-output"),
                "mismatch",
                true,
                ["negative tamper case unexpectedly passed"]);

            return new PublicationCountingNegativeReplaySet(
                "blocked",
                [mismatch],
                ["tamper-accepted-set-hash: negative tamper case unexpectedly passed"]);
        }

        private static string HashFor(string value) =>
            "sha256:" + PublicationCountingReplayContracts.Sha256Hex(value);
    }

    private sealed class TempPublicationCountingReplayWorkspace : IDisposable
    {
        private const string VerifierSourceRef153 = "88e7d8f4f35e21a341d9ad1b92ecc73bdba0ab15";
        private const string VerifierSourceRef158 = "eab1795a0313bc284f28e2276200f982e4a883c2";
        private const string VerifierBinaryHash = "sha256:7048677ba0c66c69c123ab7d046eb03a0d3c7642103f3dd6a2645873c928d6a4";

        private TempPublicationCountingReplayWorkspace(string root)
        {
            Root = root;
            Paths = PublicationCountingReplayPromotionPaths.FromWorkspaceRoot(root);
            OutputRoot = Path.Combine(root, "package-output");
        }

        public string Root { get; }

        public string OutputRoot { get; }

        public PublicationCountingReplayPromotionPaths Paths { get; }

        public string DefaultOutputPackageRoot => Path.Combine(OutputRoot, PublicationCountingReplayPromotionPaths.PackageRelativeRoot);

        public static TempPublicationCountingReplayWorkspace Create()
        {
            var workspace = new TempPublicationCountingReplayWorkspace(Path.Combine(
                Path.GetTempPath(),
                "feat160-replay-promoter-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(workspace.Paths.SchemasRoot);
            Directory.CreateDirectory(Path.Combine(workspace.Paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(workspace.Paths.PublicCorpusRoot);
            workspace.WriteSchemas();
            workspace.WriteFeat153Package();
            workspace.WriteFeat158Corpus();
            workspace.WriteSourceAsync(workspace.BuildSource(), PublicationCountingReplayPromotionPaths.SourceFileName)
                .GetAwaiter()
                .GetResult();
            return workspace;
        }

        public JsonObject LoadSource() => PublicationCountingReplayContracts.LoadSource(Paths);

        public async Task<string> WriteSourceAsync(JsonObject source, string fileName)
        {
            var path = Path.Combine(Paths.ExamplesRoot, "release-baseline", fileName);
            await File.WriteAllTextAsync(path, PublicationCountingReplayContracts.CanonicalJson(source));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void WriteSchemas()
        {
            var schema = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["required"] = Strings("schemaVersion", "sourceId", "producerFeature"),
            };
            WriteJson(Path.Combine(Paths.SchemasRoot, PublicationCountingReplayPromotionPaths.SourceSchemaFileName), schema);
            WriteJson(Path.Combine(Paths.SchemasRoot, PublicationCountingReplayPromotionPaths.PackageManifestSchemaFileName), schema);
        }

        private void WriteFeat153Package()
        {
            var root = Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "publication-counting-hardening", "v0.1.0");
            Directory.CreateDirectory(Path.Combine(root, "readiness"));
            Directory.CreateDirectory(Path.Combine(root, "handoff"));
            WriteJson(Path.Combine(root, "publication-counting-hardening-manifest.json"), new JsonObject
            {
                ["schemaVersion"] = "publication-counting-hardening-manifest.v1",
                ["packageVersion"] = "v0.1.0",
                ["producerFeature"] = "FEAT-153",
                ["verifierRefs"] = new JsonObject
                {
                    ["sourceRef"] = VerifierSourceRef153,
                    ["binaryRelease"] = VerifierBinaryHash,
                },
            });
            WriteJson(Path.Combine(root, "readiness", "publication-counting-score-proposal.json"), new JsonObject
            {
                ["schemaVersion"] = "publication-counting-score-proposal.v1",
                ["status"] = "accepted",
            });
            WriteJson(Path.Combine(root, "readiness", "publication-counting-readiness-fragment.json"), new JsonObject
            {
                ["schemaVersion"] = "publication-counting-readiness-fragment.v1",
                ["status"] = "accepted",
            });
            WriteJson(Path.Combine(root, "handoff", "publication-counting-hardening-downstream-handoff.json"), new JsonObject
            {
                ["schemaVersion"] = "publication-counting-hardening-downstream-handoff.v1",
                ["status"] = "accepted",
            });
        }

        private void WriteFeat158Corpus()
        {
            var root = CorpusRoot;
            Directory.CreateDirectory(Path.Combine(root, "fixtures"));
            Directory.CreateDirectory(Path.Combine(root, "expected-results"));
            Directory.CreateDirectory(Path.Combine(root, "validation"));
            WriteJson(Path.Combine(root, "corpus-manifest.json"), new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-manifest.v1",
                ["corpusVersion"] = "v0.3.0",
                ["status"] = "accepted",
                ["visibility"] = "public",
                ["protocolPackage"] = new JsonObject
                {
                    ["packageVersion"] = "v1.2.0",
                },
                ["verifier"] = new JsonObject
                {
                    ["sourceRef"] = VerifierSourceRef158,
                    ["binaryRelease"] = VerifierBinaryHash,
                },
            });
            WriteJson(Path.Combine(root, "validation", "clean-machine-validation-summary.json"), new JsonObject { ["status"] = "pass" });
            WriteJson(Path.Combine(root, "validation", "result-code-stability-summary.json"), new JsonObject { ["status"] = "pass" });
            WriteJson(Path.Combine(root, "validation", "no-secret-scan-result.json"), new JsonObject { ["status"] = "pass" });

            var fixtures = new JsonArray();
            foreach (var profile in GoodProfileSeeds())
            {
                Directory.CreateDirectory(Path.Combine(root, "packages", profile.FixtureId));
                fixtures.Add(FixtureIndexEntry(profile.FixtureId, "good_sample", "packages/" + profile.FixtureId, profile.PackageHash, "expected-results/" + profile.FixtureId + ".json", profile.ExpectedCode, "pass", 0));
                WriteJson(Path.Combine(root, "expected-results", profile.FixtureId + ".json"), new JsonObject
                {
                    ["expectedOverallStatus"] = "pass",
                    ["expectedExitCode"] = 0,
                    ["normalizedOutputHash"] = profile.NormalizedOutputHash,
                });
            }

            foreach (var negative in ExistingNegativeCaseSeeds())
            {
                fixtures.Add(FixtureIndexEntry(negative.FixtureId, "tamper", "packages/" + negative.FixtureId, HashFor(negative.FixtureId + "-package"), "expected-results/" + negative.FixtureId + ".json", negative.ExpectedCode, negative.ExpectedOverallStatus, negative.ExpectedExitCode));
                WriteJson(Path.Combine(root, "expected-results", negative.FixtureId + ".json"), new JsonObject
                {
                    ["expectedOverallStatus"] = negative.ExpectedOverallStatus,
                    ["expectedExitCode"] = negative.ExpectedExitCode,
                });
            }

            WriteJson(Path.Combine(root, "fixtures", "fixture-index.json"), new JsonObject
            {
                ["schemaVersion"] = "verifier-corpus-fixture-index.v1",
                ["fixtures"] = fixtures,
            });
        }

        private JsonObject BuildSource()
        {
            var feat153Root = Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "publication-counting-hardening", "v0.1.0");
            var feat158Root = CorpusRoot;
            return new JsonObject
            {
                ["schemaVersion"] = PublicationCountingReplayContracts.SourceSchemaVersion,
                ["sourceId"] = "FEAT160-TEST-SOURCE",
                ["producerFeature"] = PublicationCountingReplayContracts.FeatureId,
                ["status"] = "candidate",
                ["generatedAt"] = "2026-06-02T00:00:00Z",
                ["baselineRegister"] = new JsonObject
                {
                    ["registerVersionId"] = PublicationCountingReplayContracts.CurrentRegisterId,
                    ["registerVersion"] = PublicationCountingReplayContracts.CurrentRegisterVersion,
                    ["status"] = "AcceptedInternal",
                    ["totalScore"] = 80,
                    ["internalAuditTargetScore"] = 95,
                    ["dimensionId"] = PublicationCountingReplayContracts.TargetDimensionId,
                    ["dimensionName"] = "Publication/counting evidence",
                    ["currentScore"] = 8,
                    ["proposedScore"] = 10,
                    ["targetBlockerId"] = PublicationCountingReplayContracts.TargetBlockerId,
                    ["blockerOwnerFeatureId"] = PublicationCountingReplayContracts.FeatureId,
                },
                ["upstreamBaselines"] = new JsonObject
                {
                    ["feat153"] = new JsonObject
                    {
                        ["packagePath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/publication-counting-hardening/v0.1.0/",
                        ["packageVersion"] = "v0.1.0",
                        ["producerFeature"] = "FEAT-153",
                        ["manifestHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat153Root, "publication-counting-hardening-manifest.json")),
                        ["scoreProposalHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat153Root, "readiness", "publication-counting-score-proposal.json")),
                        ["readinessFragmentHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat153Root, "readiness", "publication-counting-readiness-fragment.json")),
                        ["handoffHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat153Root, "handoff", "publication-counting-hardening-downstream-handoff.json")),
                        ["verifierSourceRef"] = VerifierSourceRef153,
                        ["verifierBinaryRelease"] = VerifierBinaryHash,
                    },
                    ["feat158"] = new JsonObject
                    {
                        ["corpusPath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/",
                        ["corpusVersion"] = "v0.3.0",
                        ["status"] = "accepted",
                        ["visibility"] = "public",
                        ["manifestHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat158Root, "corpus-manifest.json")),
                        ["fixtureIndexHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat158Root, "fixtures", "fixture-index.json")),
                        ["cleanMachineValidationHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat158Root, "validation", "clean-machine-validation-summary.json")),
                        ["resultCodeStabilityHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat158Root, "validation", "result-code-stability-summary.json")),
                        ["noSecretScanHash"] = PublicationCountingReplayContracts.Sha256File(Path.Combine(feat158Root, "validation", "no-secret-scan-result.json")),
                        ["protocolPackageVersion"] = "v1.2.0",
                        ["verifierSourceRef"] = VerifierSourceRef158,
                        ["verifierBinaryRelease"] = VerifierBinaryHash,
                    },
                },
                ["scorePolicy"] = new JsonObject
                {
                    ["dimensionId"] = PublicationCountingReplayContracts.TargetDimensionId,
                    ["proposedScoreFrom"] = 8,
                    ["proposedScoreTo"] = 10,
                    ["directRegisterMutation"] = false,
                    ["doesNotMutateRegister"] = true,
                    ["canonicalRegisterMutationOwner"] = "later_internal_audit_95_promotion_pass",
                    ["scoreMovementBlockedUnlessAllCasesPass"] = true,
                },
                ["replayMatrix"] = new JsonObject
                {
                    ["goodProfiles"] = new JsonArray(GoodProfileSeeds().Select(GoodProfileSource).ToArray<JsonNode?>()),
                    ["excludedGoodSamples"] = new JsonArray(),
                    ["requiredArtifactPaths"] = Strings("AuditPackageManifest.json", "VerifierInputManifest.json", "VerifierProfile.json", "ElectionRecord.json", "artifacts/election-record/accepted-ballot-set.json", "artifacts/election-record/published-ballot-stream.json", "artifacts/election-record/publication-proof-transcript.json", "artifacts/election-record/tally-replay.json", "artifacts/election-record/result-binding.json", "artifacts/election-record/trustee-release-evidence.json"),
                },
                ["negativeMatrix"] = new JsonArray(
                    ExistingNegativeCaseSeeds().Select(NegativeCaseSource)
                        .Concat(Feat160NegativeCaseSeeds().Select(NegativeCaseSource))
                        .ToArray<JsonNode?>()),
                ["packageLayout"] = new JsonObject
                {
                    ["targetPackagePath"] = PublicationCountingReplayContracts.ExpectedTargetPackagePath,
                    ["immutableVersion"] = PublicationCountingReplayContracts.TargetPackageVersion,
                    ["files"] = new JsonArray(PublicationCountingReplayArtifactGenerator.RequiredArtifactPaths
                        .Select(path => new JsonObject
                        {
                            ["path"] = path,
                            ["purpose"] = "test artifact",
                            ["publicSafe"] = true,
                            ["requiredForManifest"] = true,
                        })
                        .ToArray<JsonNode?>()),
                },
                ["publicSafety"] = new JsonObject
                {
                    ["visibility"] = "public_safe",
                    ["expectedUnexpectedFindingCount"] = 0,
                    ["forbiddenMaterialCategories"] = Strings("shuffle_maps", "rerandomization_randomness", "plaintext_choices", "voter_identity_joins", "kms_secrets", "support_case_data", "local_absolute_paths", "private_backend_logs", "cloud_account_identifiers", "database_connection_strings", "legal_sufficiency_claims", "public_state_election_claims", "production_rollout_claims"),
                    ["forbiddenClaimCategories"] = Strings("production_ready", "public_state_ready", "legally_sufficient", "certified", "external_crypto_review_complete"),
                    ["publicBoundaryStatement"] = "Public-safe metadata only.",
                },
                ["readinessOutput"] = new JsonObject
                {
                    ["readinessFragmentPath"] = PublicationCountingReplayArtifactGenerator.ReadinessFragmentPath,
                    ["scoreProposalPath"] = PublicationCountingReplayArtifactGenerator.ScoreProposalPath,
                    ["dimensionId"] = PublicationCountingReplayContracts.TargetDimensionId,
                    ["proposedScoreFrom"] = 8,
                    ["proposedScoreTo"] = 10,
                    ["doesNotMutateRegister"] = true,
                    ["targetBlockerId"] = PublicationCountingReplayContracts.TargetBlockerId,
                },
                ["downstreamConsumers"] = new JsonArray(new JsonObject
                {
                    ["featureId"] = "FEAT-166",
                    ["allowedUse"] = "Consume candidate replay metadata.",
                    ["forbiddenClaim"] = "No production, public-state, legal, or certification claim.",
                }),
                ["residualRisks"] = Strings("Later FEAT-160 phases must replace skeleton evidence with replay-run evidence."),
            };
        }

        private string CorpusRoot => Path.Combine(Paths.PublicCorpusRoot, "hushvoting-v1", "v0.3.0");

        private static JsonObject FixtureIndexEntry(
            string fixtureId,
            string family,
            string packagePath,
            string packageHash,
            string expectedResultRef,
            string expectedCode,
            string expectedStatus,
            int exitCode) =>
            new()
            {
                ["fixtureId"] = fixtureId,
                ["fixtureFamily"] = family,
                ["packagePath"] = packagePath,
                ["packageHash"] = packageHash,
                ["expectedResultRef"] = expectedResultRef,
                ["expectedPrimaryResultCode"] = expectedCode,
                ["expectedOverallStatus"] = expectedStatus,
                ["expectedExitCode"] = exitCode,
            };

        private static JsonObject GoodProfileSource(GoodProfileSeed seed) =>
            new()
            {
                ["fixtureId"] = seed.FixtureId,
                ["profileIntent"] = "test profile",
                ["packagePath"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/packages/" + seed.FixtureId,
                ["packageHash"] = seed.PackageHash,
                ["expectedResultRef"] = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/expected-results/" + seed.FixtureId + ".json",
                ["expectedOverallStatus"] = "pass",
                ["expectedExitCode"] = 0,
                ["expectedPrimaryResultCode"] = seed.ExpectedCode,
                ["normalizedOutputHash"] = seed.NormalizedOutputHash,
                ["requiredForScore"] = true,
            };

        private static JsonObject NegativeCaseSource(NegativeCaseSeed seed) =>
            new()
            {
                ["caseId"] = seed.CaseId,
                ["fixtureId"] = seed.FixtureId,
                ["source"] = seed.Source,
                ["coverageArea"] = seed.CoverageArea,
                ["changedArtifactOrCondition"] = "test condition",
                ["expectedPrimaryResultCode"] = seed.ExpectedCode,
                ["expectedOverallStatus"] = seed.ExpectedOverallStatus,
                ["expectedExitCode"] = seed.ExpectedExitCode,
                ["blocksScoreMovement"] = true,
            };

        private static IReadOnlyList<GoodProfileSeed> GoodProfileSeeds() =>
        [
            new("sample-good-finalized-election", HashFor("sample-good-finalized-election-package"), HashFor("sample-good-finalized-election-output"), "package_structure_valid"),
            new("sample-good-larger-electorate", HashFor("sample-good-larger-electorate-package"), HashFor("sample-good-larger-electorate-output"), "package_structure_valid"),
            new("sample-good-low-turnout", HashFor("sample-good-low-turnout-package"), HashFor("sample-good-low-turnout-output"), "package_structure_valid"),
            new("sample-good-multi-option-single-winner", HashFor("sample-good-multi-option-single-winner-package"), HashFor("sample-good-multi-option-single-winner-output"), "package_structure_valid"),
            new("sample-good-trustee-threshold", HashFor("sample-good-trustee-threshold-package"), HashFor("sample-good-trustee-threshold-output"), "package_structure_valid"),
        ];

        private static IReadOnlyList<NegativeCaseSeed> ExistingNegativeCaseSeeds() =>
        [
            new("NEG-ACCEPTED-SET-HASH", "tamper-accepted-set-hash", "existing_v0.3.0", "accepted_set", "accepted_ballot_inventory_hash_mismatch"),
            new("NEG-PACKAGE-MISSING-ARTIFACT", "tamper-missing-artifact", "existing_v0.3.0", "package_structure", "package_manifest_missing_artifact"),
            new("NEG-PACKAGE-MALFORMED-JSON", "tamper-malformed-package-json", "existing_v0.3.0", "package_structure", "package_unparseable", "notAvailable", 2),
        ];

        private static IReadOnlyList<NegativeCaseSeed> Feat160NegativeCaseSeeds() =>
        [
            new("NEG-TRUSTEE-RELEASE-WRONG-TARGET", "tamper-trustee-release-wrong-target", "feat160_required", "trustee_release", "trustee_release_wrong_target"),
            new("NEG-TRUSTEE-RELEASE-THRESHOLD-NOT-MET", "tamper-trustee-release-threshold-not-met", "feat160_required", "trustee_release", "trustee_release_threshold_not_met"),
        ];

        private static void WriteJson(string path, JsonObject json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, PublicationCountingReplayContracts.CanonicalJson(json));
        }

        private static string HashFor(string value) =>
            "sha256:" + PublicationCountingReplayContracts.Sha256Hex(value);

        private static JsonArray Strings(params string[] values) =>
            new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

        private sealed record GoodProfileSeed(
            string FixtureId,
            string PackageHash,
            string NormalizedOutputHash,
            string ExpectedCode);

        private sealed record NegativeCaseSeed(
            string CaseId,
            string FixtureId,
            string Source,
            string CoverageArea,
            string ExpectedCode,
            string ExpectedOverallStatus = "fail",
            int ExpectedExitCode = 1);
    }
}

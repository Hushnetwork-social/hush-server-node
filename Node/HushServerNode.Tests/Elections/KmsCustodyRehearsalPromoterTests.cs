using System.Text.Json.Nodes;
using FluentAssertions;
using KmsCustodyRehearsalPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class KmsCustodyRehearsalPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();

        var errors = KmsCustodyRehearsalContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in KmsCustodyRehearsalContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void ReleaseBaseline_SourceAndCurrentRefs_AreValid()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source)
            .Concat(KmsCustodyRehearsalContracts.ValidateCurrentRefs(workspace.Paths, source))
            .ToArray();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Promotion_ValidateOnly_ShouldNotWritePackageRoot()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(KmsCustodyRehearsalPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().BeEmpty();
        result.GeneratedPackage.Artifacts.Should().HaveCount(KmsCustodyRehearsalArtifactGenerator.RequiredArtifactPaths.Length);
        Directory.Exists(workspace.DefaultOutputPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldFailWhenPackageRootIsMissing()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();

        var act = () => CreateService().Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<KmsCustodyRehearsalPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("Package root does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_OutputRootOutsideWorkspace_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var outsideRoot = Path.Combine(Path.GetTempPath(), "feat161-outside-" + Guid.NewGuid().ToString("N"));

        var act = () => CreateService().Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModePackage,
            null,
            outsideRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<KmsCustodyRehearsalPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains("FEAT-161 rehearsal output root", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_PackageMode_WritesDeterministicCustodyRehearsalArtifacts()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var service = CreateService();

        var first = service.Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        var second = service.Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        first.Status.Should().Be("candidate");
        first.WrittenFiles.Should().HaveCount(KmsCustodyRehearsalArtifactGenerator.RequiredArtifactPaths.Length);
        first.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.GeneratedPackage.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));

        foreach (var relativePath in KmsCustodyRehearsalArtifactGenerator.RequiredArtifactPaths)
        {
            File.Exists(Path.Combine(workspace.DefaultOutputPackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(relativePath);
        }

        var manifest = KmsCustodyRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, KmsCustodyRehearsalArtifactGenerator.ManifestPath),
            "generated manifest");
        manifest["baselineRegister"]!.AsObject()["proposedScoreTo"]!.GetValue<int>().Should().Be(9);
        manifest["publicSafety"]!.AsObject()["unexpectedFindingCount"]!.GetValue<int>().Should().Be(0);
        manifest["reviewerHandoff"]!.AsObject()["restrictedIndexRef"]!.GetValue<string>()
            .Should()
            .Be(KmsCustodyRehearsalArtifactGenerator.RestrictedEvidenceIndexPath);
        manifest["readinessProposal"]!.AsObject()["doesNotMutateRegister"]!.GetValue<bool>().Should().BeTrue();
        manifest["entries"]!.AsArray().Should().HaveCount(KmsCustodyRehearsalArtifactGenerator.RequiredArtifactPaths.Length - 1);

        var noSecret = KmsCustodyRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, KmsCustodyRehearsalArtifactGenerator.NoSecretScanResultPath),
            "no secret scan");
        noSecret["status"]!.GetValue<string>().Should().Be("pass");
        noSecret["unexpectedFindingCount"]!.GetValue<int>().Should().Be(0);

        var iamSummary = KmsCustodyRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, KmsCustodyRehearsalArtifactGenerator.IamDriftSummaryPath),
            "iam drift summary");
        iamSummary["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Should()
            .OnlyContain(item => KmsCustodyRehearsalContracts.GetString(item, "expectedResult", "") == "blocked");

        var providerSummary = KmsCustodyRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, KmsCustodyRehearsalArtifactGenerator.ProviderRegionalFailureSummaryPath),
            "provider regional summary");
        providerSummary["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => KmsCustodyRehearsalContracts.GetString(item, "scenarioId"))
            .Should()
            .Contain([
                "KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN",
                "KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP",
                "KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP",
            ]);

        var deletionSummary = KmsCustodyRehearsalContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultOutputPackageRoot, KmsCustodyRehearsalArtifactGenerator.DeletionScheduleDriftSummaryPath),
            "deletion drift summary");
        deletionSummary["cases"]!.AsArray()
            .OfType<JsonObject>()
            .Should()
            .OnlyContain(item => KmsCustodyRehearsalContracts.GetString(item, "expectedResult", "") == "blocked");
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsExistingPackageDrift()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        File.WriteAllText(
            Path.Combine(workspace.DefaultOutputPackageRoot, KmsCustodyRehearsalArtifactGenerator.ReadmePath),
            "drifted package");

        var act = () => service.Promote(new(
            workspace.Paths,
            KmsCustodyRehearsalPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<KmsCustodyRehearsalPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(KmsCustodyRehearsalArtifactGenerator.ReadmePath, StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_StaleReadinessBaseline_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["baselineRegister"]!.AsObject()["registerVersionId"] = "RDY-REG-v0.1.6";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_STALE_READINESS_BASELINE", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentnessValidation_StaleFeat131Ref_BlocksPromotion()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["upstreamBaselines"]!.AsObject()["feat131"]!.AsObject()["publicSafeHandoffHash"] =
            HashFor("wrong-feat131");

        var errors = KmsCustodyRehearsalContracts.ValidateCurrentRefs(workspace.Paths, source);

        errors.Should().Contain(error => error.Contains("upstreamBaselines.feat131.publicSafeHandoffHash mismatch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("feat143", "FEAT161_STALE_FEAT143_REF")]
    [InlineData("feat154", "FEAT161_STALE_FEAT154_REF")]
    [InlineData("feat156", "FEAT161_STALE_FEAT156_REF")]
    public void SourceValidation_StaleUpstreamProducerRefs_AreRejected(string upstreamKey, string expectedCode)
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["upstreamBaselines"]!.AsObject()[upstreamKey]!.AsObject()["producerFeature"] = "FEAT-000";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DirectRegisterMutation_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["scorePolicy"]!.AsObject()["directRegisterMutation"] = true;

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_DIRECT_REGISTER_MUTATION_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ScoreOverclaimTo10_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["scorePolicy"]!.AsObject()["proposedScoreTo"] = 10;

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_SCORE_OVERCLAIM_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_LocalAbsolutePaths_AreRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["readinessOutput"]!.AsObject()["readinessFragment"] = @"C:\private\readiness.json";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_LOCAL_ABSOLUTE_PATH_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ForbiddenProviderMaterial_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["rehearsalMatrix"]!.AsObject()["scenarios"]!.AsArray()[0]!.AsObject()["description"] =
            "leaked arn:aws value";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_PRIVATE_MATERIAL_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_LiveAwsDefaultValidation_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        source["rehearsalMatrix"]!.AsObject()["liveProviderRequiredForDefaultValidation"] = true;

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_LIVE_PROVIDER_DEFAULT_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingIamDriftScenario_BlocksPromotion()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        var scenarios = source["rehearsalMatrix"]!.AsObject()["scenarios"]!.AsArray();
        RemoveScenario(scenarios, "KMS-CUSTODY-IAM-PERMISSION-DRIFT");

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_REQUIRED_SCENARIO_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_UnexpectedIamPermissionPass_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-IAM-PERMISSION-DRIFT")["expectedResult"] = "pass";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_IAM_DRIFT_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_RuntimeRotationRestrictedRefMismatch_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-RUNTIME-ROLE-ROTATION")["restrictedEvidenceRefs"]!
            .AsArray()[0]!
            .AsObject()["refId"] = "RESTRICTED-STALE-RUNTIME-ROTATION";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_RUNTIME_ROTATION_REF_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_AliasTagSafeResultCodeMismatch_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-ALIAS-TAG-DRIFT")["safeResultCodes"] = Strings("alias_tag_drift_ok");

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_ALIAS_TAG_DRIFT_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_RawPolicyDocumentMarker_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-IAM-POLICY-DRIFT")["description"] = "policyDocument leaked";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_PRIVATE_MATERIAL_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ProviderCleanupRetryPolicyMismatch_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP")["expectedResult"] = "pass";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_PROVIDER_FAILURE_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_RegionalDegradedScenarioMustRecordResidualRisk()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP")["readinessImpact"] = "supports_score_proposal";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_REGIONAL_FAILURE_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingDeletionScheduleDriftScenario_BlocksPromotion()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        RemoveScenario(source["rehearsalMatrix"]!.AsObject()["scenarios"]!.AsArray(), "KMS-CUSTODY-DELETION-SCHEDULE-DRIFT");

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_REQUIRED_SCENARIO_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DeletionScheduleRestrictedRefMismatch_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-DELETION-SCHEDULE-DRIFT")["restrictedEvidenceRefs"]!
            .AsArray()[0]!
            .AsObject()["refId"] = "RESTRICTED-STALE-DELETION-SCHEDULE";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_DELETION_DRIFT_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_OrphanedCustodyStateCannotPass()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE")["expectedResult"] = "pass";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_ORPHANED_CUSTODY_POLICY_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_RestrictedEvidencePayloadPublication_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE")["restrictedEvidenceRefs"]!
            .AsArray()[0]!
            .AsObject()["payloadPublished"] = true;

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_RESTRICTED_PAYLOAD_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ProviderErrorPayloadMarker_IsRejected()
    {
        using var workspace = TempKmsCustodyRehearsalWorkspace.Create();
        var source = workspace.LoadSource();
        ScenarioById(source, "KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN")["description"] = "provider_error_payload leaked";

        var errors = KmsCustodyRehearsalContracts.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("FEAT161_PRIVATE_MATERIAL_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicSafetyScanner_RejectsGeneratedProviderMarkers()
    {
        var artifacts = new[]
        {
            new KmsCustodyRehearsalArtifact("validation/example.json", "{\"value\":\"operator identity leaked\"}\n"),
        };

        var findings = KmsCustodyRehearsalArtifactGenerator.ScanGeneratedArtifacts(artifacts);

        findings.Should().Contain(finding => finding.Contains("operator identity", StringComparison.Ordinal));
    }

    private static KmsCustodyRehearsalPromotionService CreateService() => new();

    private static JsonObject ScenarioById(JsonObject source, string scenarioId) =>
        source["rehearsalMatrix"]!.AsObject()["scenarios"]!.AsArray()
            .OfType<JsonObject>()
            .Single(item => KmsCustodyRehearsalContracts.GetString(item, "scenarioId") == scenarioId);

    private static void RemoveScenario(JsonArray scenarios, string scenarioId)
    {
        for (var index = scenarios.Count - 1; index >= 0; index--)
        {
            if (scenarios[index] is JsonObject scenario &&
                KmsCustodyRehearsalContracts.GetString(scenario, "scenarioId") == scenarioId)
            {
                scenarios.RemoveAt(index);
            }
        }
    }

    private sealed class TempKmsCustodyRehearsalWorkspace : IDisposable
    {
        private TempKmsCustodyRehearsalWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "feat161-kms-custody-" + Guid.NewGuid().ToString("N"));
            Paths = KmsCustodyRehearsalPromotionPaths.FromWorkspaceRoot(Root);
            OutputRoot = Path.Combine(Root, "package-output");
            Directory.CreateDirectory(Paths.SchemasRoot);
            Directory.CreateDirectory(Path.Combine(Paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(Paths.PublicCorpusRoot);
            WriteSchemas();
            WriteCurrentnessFiles();
            WriteJson(Path.Combine(Paths.ExamplesRoot, "release-baseline", KmsCustodyRehearsalPromotionPaths.SourceFileName), BuildSource());
        }

        public string Root { get; }

        public KmsCustodyRehearsalPromotionPaths Paths { get; }

        public string OutputRoot { get; }

        public string DefaultOutputPackageRoot => Path.Combine(OutputRoot, KmsCustodyRehearsalPromotionPaths.PackageRelativeRoot);

        public static TempKmsCustodyRehearsalWorkspace Create() => new();

        public JsonObject LoadSource() => KmsCustodyRehearsalContracts.LoadSource(Paths);

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
            WriteJson(Path.Combine(Paths.SchemasRoot, KmsCustodyRehearsalPromotionPaths.SourceSchemaFileName), schema);
            WriteJson(Path.Combine(Paths.SchemasRoot, KmsCustodyRehearsalPromotionPaths.PackageManifestSchemaFileName), schema);
        }

        private void WriteCurrentnessFiles()
        {
            WriteText(Feat131PublicPath, "FEAT-131 public-safe custody handoff\n");
            WriteText(Feat131RestrictedPath, "FEAT-131 restricted custody evidence hash source\n");
            WriteText(Feat143Path, "FEAT-143 runtime deployment proof binding handoff\n");
            WriteText(Feat154Path, "FEAT-154 production-like operational run context\n");
            WriteText(Feat156Path, "FEAT-156 promotion register package manifest\n");
        }

        private JsonObject BuildSource() =>
            new()
            {
                ["schemaVersion"] = KmsCustodyRehearsalContracts.SourceSchemaVersion,
                ["sourceId"] = "KMS-CUSTODY-REHEARSAL-TEST-v0.1.0",
                ["producerFeature"] = KmsCustodyRehearsalContracts.FeatureId,
                ["status"] = "candidate",
                ["generatedAt"] = "2026-06-02T00:00:00Z",
                ["baselineRegister"] = new JsonObject
                {
                    ["registerVersionId"] = KmsCustodyRehearsalContracts.CurrentRegisterId,
                    ["registerVersion"] = KmsCustodyRehearsalContracts.CurrentRegisterVersion,
                    ["status"] = "AcceptedInternal",
                    ["totalScore"] = 80,
                    ["internalAuditTargetScore"] = 95,
                    ["dimensionId"] = KmsCustodyRehearsalContracts.TargetDimensionId,
                    ["dimensionName"] = KmsCustodyRehearsalContracts.TargetDimensionName,
                    ["currentScore"] = 8,
                    ["proposedScore"] = 9,
                    ["targetBlockerId"] = KmsCustodyRehearsalContracts.TargetBlockerId,
                    ["blockerOwnerFeatureId"] = KmsCustodyRehearsalContracts.FeatureId,
                },
                ["upstreamBaselines"] = new JsonObject
                {
                    ["feat131"] = new JsonObject
                    {
                        ["producerFeature"] = "FEAT-131",
                        ["evidenceId"] = "RDY-EVID-AT-RDY-002-FEAT-131-001",
                        ["dimensionId"] = KmsCustodyRehearsalContracts.TargetDimensionId,
                        ["acceptedGateIds"] = Strings("AT-RDY-002", "AT-RDY-003", "AT-RDY-004"),
                        ["productionCustodyMode"] = "aws_kms_per_election_envelope_v1",
                        ["publicSafeHandoffHash"] = KmsCustodyRehearsalContracts.Sha256File(Feat131PublicPath),
                        ["restrictedHandoffHash"] = KmsCustodyRehearsalContracts.Sha256File(Feat131RestrictedPath),
                    },
                    ["feat143"] = UpstreamRef("FEAT-143", "runtime deployment proof binding baseline", KmsCustodyRehearsalContracts.Sha256File(Feat143Path)),
                    ["feat154"] = UpstreamRef("FEAT-154", "production-like operational run context", KmsCustodyRehearsalContracts.Sha256File(Feat154Path)),
                    ["feat156"] = UpstreamRef("FEAT-156", "readiness promotion register blocker ownership", KmsCustodyRehearsalContracts.Sha256File(Feat156Path)),
                },
                ["scorePolicy"] = new JsonObject
                {
                    ["dimensionId"] = KmsCustodyRehearsalContracts.TargetDimensionId,
                    ["proposedScoreFrom"] = 8,
                    ["proposedScoreTo"] = 9,
                    ["directRegisterMutation"] = false,
                    ["doesNotMutateRegister"] = true,
                    ["canonicalRegisterMutationOwner"] = "later_internal_audit_95_promotion_pass",
                    ["scoreMovementBlockedUnlessAllCasesPass"] = true,
                },
                ["rehearsalMatrix"] = new JsonObject
                {
                    ["providerFamily"] = "aws-kms",
                    ["defaultValidationMode"] = "deterministic_fake_provider",
                    ["liveProviderRequiredForDefaultValidation"] = false,
                    ["scenarios"] = new JsonArray(
                        Scenario("KMS-CUSTODY-ACCEPTED-FEAT131-BASELINE", "accepted_baseline", "pass", "supports_score_proposal"),
                        Scenario("KMS-CUSTODY-IAM-PERMISSION-DRIFT", "iam_drift", "blocked", "blocks_score_proposal"),
                        Scenario("KMS-CUSTODY-IAM-POLICY-DRIFT", "iam_drift", "blocked", "blocks_score_proposal"),
                        Scenario("KMS-CUSTODY-RUNTIME-ROLE-ROTATION", "runtime_rotation", "pass", "supports_score_proposal"),
                        Scenario("KMS-CUSTODY-ALIAS-TAG-DRIFT", "alias_tag_drift", "blocked", "blocks_score_proposal"),
                        Scenario("KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN", "provider_failure", "blocked", "blocks_score_proposal"),
                        Scenario("KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP", "provider_failure", "degraded", "records_residual_risk"),
                        Scenario("KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP", "regional_failure", "degraded", "records_residual_risk"),
                        Scenario("KMS-CUSTODY-DELETION-SCHEDULE-DRIFT", "deletion_schedule_drift", "blocked", "blocks_score_proposal"),
                        Scenario("KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE", "stale_orphaned_custody_state", "blocked", "blocks_score_proposal"),
                        Scenario("KMS-CUSTODY-RESTRICTED-BOUNDARY", "restricted_boundary", "restricted_only", "preserves_private_boundary")),
                },
                ["negativeMatrix"] = new JsonArray(KmsCustodyRehearsalContracts.RequiredNegativeCaseIds
                    .Select(id => new JsonObject
                    {
                        ["caseId"] = id,
                        ["mutation"] = "test mutation",
                        ["expectedDiagnostic"] = id.ToLowerInvariant().Replace('-', '_'),
                        ["blocksScoreMovement"] = true,
                    })
                    .ToArray<JsonNode?>()),
                ["packageLayout"] = new JsonObject
                {
                    ["targetPackagePath"] = KmsCustodyRehearsalContracts.ExpectedTargetPackagePath,
                    ["packageVersion"] = KmsCustodyRehearsalContracts.TargetPackageVersion,
                    ["expectedArtifacts"] = Strings(KmsCustodyRehearsalArtifactGenerator.RequiredArtifactPaths),
                },
                ["publicSafety"] = new JsonObject
                {
                    ["noSecretScanRequired"] = true,
                    ["forbiddenMaterialClasses"] = Strings("raw IAM policy snapshots", "exact provider permissions", "KMS key identifiers", "KMS resource names", "KMS aliases", "raw provider tags", "cloud account identifiers", "operator identities", "provider error payloads", "custody row references", "decrypt authority details", "raw scalar material", "operational runbooks", "absolute private paths"),
                    ["publicProviderDetailLevel"] = "provider_family_only",
                    ["liveAwsKmsRequiredForDefaultCi"] = false,
                },
                ["restrictedEvidenceBoundary"] = new JsonObject
                {
                    ["payloadsRemainPrivate"] = true,
                    ["publicRefsAreHashOnly"] = true,
                    ["restrictedOwner"] = "restricted_reviewer",
                    ["restrictedIndexPath"] = KmsCustodyRehearsalArtifactGenerator.RestrictedEvidenceIndexPath,
                },
                ["readinessOutput"] = new JsonObject
                {
                    ["readinessFragment"] = KmsCustodyRehearsalArtifactGenerator.ReadinessFragmentPath,
                    ["scoreProposal"] = KmsCustodyRehearsalArtifactGenerator.ScoreProposalPath,
                    ["directRegisterMutation"] = false,
                    ["doesNotMutateRegister"] = true,
                },
                ["downstreamConsumers"] = new JsonArray(
                    Downstream("FEAT-162"),
                    Downstream("FEAT-163"),
                    Downstream("FEAT-166")),
                ["residualRisks"] = Strings("Provider-wide incidents remain residual risk."),
            };

        private JsonObject UpstreamRef(string producerFeature, string role, string referenceHash) =>
            new()
            {
                ["producerFeature"] = producerFeature,
                ["status"] = "completed",
                ["role"] = role,
                ["referenceHash"] = referenceHash,
            };

        private static JsonObject Scenario(string scenarioId, string category, string expectedResult, string readinessImpact) =>
            new()
            {
                ["scenarioId"] = scenarioId,
                ["category"] = category,
                ["description"] = "deterministic fake-provider test scenario",
                ["gateIds"] = Strings("AT-RDY-002", "AT-RDY-003", "AT-RDY-004"),
                ["expectedResult"] = expectedResult,
                ["safeResultCodes"] = Strings(SafeResultCodeFor(scenarioId)),
                ["readinessImpact"] = readinessImpact,
                ["restrictedEvidenceRefs"] = new JsonArray(new JsonObject
                {
                    ["refId"] = RestrictedRefFor(scenarioId),
                    ["visibility"] = "restricted_reviewer",
                    ["hash"] = HashFor(scenarioId),
                    ["payloadPublished"] = false,
                }),
                ["requiredForScore"] = true,
            };

        private static string SafeResultCodeFor(string scenarioId) =>
            scenarioId switch
            {
                "KMS-CUSTODY-ACCEPTED-FEAT131-BASELINE" => "accepted_baseline_verified",
                "KMS-CUSTODY-IAM-PERMISSION-DRIFT" => "iam_permission_drift_blocked",
                "KMS-CUSTODY-IAM-POLICY-DRIFT" => "iam_policy_drift_blocked",
                "KMS-CUSTODY-RUNTIME-ROLE-ROTATION" => "runtime_rotation_recovered",
                "KMS-CUSTODY-ALIAS-TAG-DRIFT" => "alias_tag_drift_blocked",
                "KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN" => "provider_unavailable_blocked",
                "KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP" => "provider_cleanup_retry_recorded",
                "KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP" => "regional_degraded_residual_recorded",
                "KMS-CUSTODY-DELETION-SCHEDULE-DRIFT" => "deletion_schedule_drift_blocked",
                "KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE" => "orphaned_custody_state_blocked",
                "KMS-CUSTODY-RESTRICTED-BOUNDARY" => "restricted_boundary_preserved",
                _ => throw new InvalidOperationException("Unknown FEAT-161 test scenario id: " + scenarioId),
            };

        private static string RestrictedRefFor(string scenarioId) =>
            scenarioId switch
            {
                "KMS-CUSTODY-ACCEPTED-FEAT131-BASELINE" => "RESTRICTED-FEAT131-CUSTODY-HANDOFF",
                "KMS-CUSTODY-IAM-PERMISSION-DRIFT" => "RESTRICTED-IAM-PERMISSION-DRIFT",
                "KMS-CUSTODY-IAM-POLICY-DRIFT" => "RESTRICTED-IAM-POLICY-DRIFT",
                "KMS-CUSTODY-RUNTIME-ROLE-ROTATION" => "RESTRICTED-RUNTIME-ROTATION",
                "KMS-CUSTODY-ALIAS-TAG-DRIFT" => "RESTRICTED-ALIAS-TAG-DRIFT",
                "KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN" => "RESTRICTED-PROVIDER-UNAVAILABLE",
                "KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP" => "RESTRICTED-PROVIDER-CLEANUP-RETRY",
                "KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP" => "RESTRICTED-REGIONAL-DEGRADED",
                "KMS-CUSTODY-DELETION-SCHEDULE-DRIFT" => "RESTRICTED-DELETION-SCHEDULE-DRIFT",
                "KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE" => "RESTRICTED-ORPHANED-CUSTODY-STATE",
                "KMS-CUSTODY-RESTRICTED-BOUNDARY" => "RESTRICTED-BOUNDARY-INDEX",
                _ => throw new InvalidOperationException("Unknown FEAT-161 test scenario id: " + scenarioId),
            };

        private static JsonObject Downstream(string featureId) =>
            new()
            {
                ["featureId"] = featureId,
                ["consumes"] = "test handoff",
            };

        private string Feat131PublicPath => Path.Combine(Root, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-131-per-election-kms-custody-lifecycle", "downstream-handoff.md");

        private string Feat131RestrictedPath => Path.Combine(Root, "hush-documents", "PrivateServer_ElectronicVoting", "Operational-Security", "FEAT-131-Custody-Evidence-Handoff.md");

        private string Feat143Path => Path.Combine(Root, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-143-runtime-deployment-proof-binding-ledger", "readiness-handoff-20260526.md");

        private string Feat154Path => Path.Combine(Root, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-154-production-like-operational-run-evidence", "FeatureDescription.md");

        private string Feat156Path => Path.Combine(Root, "hush-documents", "PrivateServer_ElectronicVoting", "Production-Rollout-Promotion-Register", "package", "feat156-package-manifest.json");

        private static void WriteText(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static void WriteJson(string path, JsonObject json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, KmsCustodyRehearsalContracts.CanonicalJson(json));
        }
    }

    private static string HashFor(string value) => KmsCustodyRehearsalContracts.Sha256Hex(value);

    private static JsonArray Strings(params string[] values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
}

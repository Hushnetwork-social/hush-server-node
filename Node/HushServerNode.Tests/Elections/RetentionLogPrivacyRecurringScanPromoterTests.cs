using System.Text.Json.Nodes;
using FluentAssertions;
using RetentionLogPrivacyRecurringScanPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class RetentionLogPrivacyRecurringScanPromoterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z");

    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();

        var errors = RetentionLogPrivacyRecurringScanContracts.ValidateSchemaSet(workspace.Paths.SchemasRoot);

        errors.Should().BeEmpty();
        foreach (var schemaFile in RetentionLogPrivacyRecurringScanContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(workspace.Paths.SchemasRoot, schemaFile)).Should().BeTrue(schemaFile);
        }
    }

    [Fact]
    public void SourceValidation_AcceptedReleaseBaseline_ShouldPass()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.Mode.Should().Be(RetentionLogPrivacyRecurringScanPromotionService.ModeValidateOnly);
        result.Status.Should().Be("accepted");
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(workspace.DefaultPackageRoot).Should().BeFalse();
    }

    [Fact]
    public void Promotion_PublicOnlyValidateOnly_ShouldNotRequirePrivateContextRepos()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModeValidateOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.Status.Should().Be("accepted");
        result.WrittenFiles.Should().BeEmpty();
    }

    [Fact]
    public void SourceValidation_StaleFeat137Proof_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["feat137Proof"]!.AsObject()["packageHash"] = "0000000000000000000000000000000000000000000000000000000000000000";

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_FEAT137_PROOF_CURRENTNESS_BLOCKED", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_MissingScannerBaseline_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source.Remove("scannerBaseline");

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_SCANNER_BASELINE_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_OutputFamilyNotInRegistry_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["outputFamilies"]!.AsArray().Add(new JsonObject
        {
            ["familyId"] = "new_unclassified_export",
            ["visibility"] = "public",
            ["scannerDecision"] = "not_applicable",
            ["publicPayloadAllowed"] = true,
            ["allowedPublicFields"] = new JsonArray("summary"),
            ["forbiddenFields"] = new JsonArray("rawPayload"),
        });

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_UNCLASSIFIED_OUTPUT_FAMILY", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ForbiddenRawLogMaterial_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["scanInputs"]!.AsArray()[0]!.AsObject()["rawLogPayload"] = "redacted raw log example";

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_RAW_LOG_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ForbiddenTraceLabel_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["scanInputs"]!.AsArray()[2]!.AsObject()["correlationLabel"] = "redacted-correlation-label";

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_TRACE_PAYLOAD_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_ForbiddenSupportPayload_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["scanInputs"]!.AsArray()[4]!.AsObject()["supportCaseContent"] = "redacted support case content";

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_SUPPORT_PAYLOAD_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PrivatePath_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["scanInputs"]!.AsArray()[0]!.AsObject()["sourceRef"]!.AsObject()["path"] =
            "PrivateServer_ElectronicVoting/Retention-Log-Privacy-Recurring-Scans/raw-log.json";

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_PRIVATE_PATH_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_DirectRegisterMutation_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["scorePolicy"]!.AsObject()["directRegisterMutation"] = true;

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_DIRECT_REGISTER_MUTATION_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_WrongScoreRange_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["scorePolicy"]!.AsObject()["proposedScoreTo"] = 10;

        var errors = workspace.ValidateSource(source);

        errors.Should().Contain(error => error.Contains("RLP164_WRONG_SCORE_RANGE", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceValidation_PublicOnlyPrivateDependency_IsRejected()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var source = workspace.LoadSource();
        source["publicSafetyPolicy"]!.AsObject()["publicOnlyValidation"] = false;

        var errors = workspace.ValidateSource(source, publicOnly: true);

        errors.Should().Contain(error => error.Contains("RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeFixtureCatalog_DeclaresExpectedBlockingCodes()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();

        var catalog = RetentionLogPrivacyRecurringScanContracts.ReadJsonObject(
            Path.Combine(workspace.Paths.ExamplesRoot, "negative", RetentionLogPrivacyRecurringScanPromotionPaths.NegativeFixtureCatalogFileName),
            "negative fixture catalog");

        catalog["fixtures"]!.AsArray().Should().HaveCountGreaterThanOrEqualTo(12);
        catalog["fixtures"]!.AsArray()
            .OfType<JsonObject>()
            .SelectMany(fixture => fixture["expectedResultCodes"]!.AsArray().Select(code => code!.GetValue<string>()))
            .Should()
            .Contain(new[]
            {
                "RLP164_FEAT137_PROOF_CURRENTNESS_BLOCKED",
                "RLP164_SCANNER_BASELINE_MISSING",
                "RLP164_UNCLASSIFIED_OUTPUT_FAMILY",
                "RLP164_DIRECT_REGISTER_MUTATION_FORBIDDEN",
                "RLP164_WRONG_SCORE_RANGE",
                "RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY",
            });
    }

    [Fact]
    public void Promotion_PackageMode_WritesPackageManifestReadinessAndHandoff()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();

        var result = CreateService().Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        result.WrittenFiles.Should().HaveCount(RetentionLogPrivacyRecurringScanArtifactGenerator.RequiredArtifactPaths.Length);
        File.Exists(Path.Combine(workspace.DefaultPackageRoot, RetentionLogPrivacyRecurringScanArtifactGenerator.ManifestPath)).Should().BeTrue();
        File.Exists(Path.Combine(workspace.DefaultPackageRoot, RetentionLogPrivacyRecurringScanArtifactGenerator.PackageIndexPath)).Should().BeTrue();

        var readiness = RetentionLogPrivacyRecurringScanContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, RetentionLogPrivacyRecurringScanArtifactGenerator.ReadinessFragmentPath.Replace('/', Path.DirectorySeparatorChar)),
            "readiness fragment");
        readiness["dimensionId"]!.GetValue<string>().Should().Be(RetentionLogPrivacyRecurringScanContracts.TargetDimensionId);
        readiness["scoreEffect"]!.AsObject()["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();

        var scoreProposal = RetentionLogPrivacyRecurringScanContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, RetentionLogPrivacyRecurringScanArtifactGenerator.ScoreProposalPath.Replace('/', Path.DirectorySeparatorChar)),
            "score proposal");
        scoreProposal["proposedScoreFrom"]!.GetValue<int>().Should().Be(8);
        scoreProposal["proposedScoreTo"]!.GetValue<int>().Should().Be(9);
        scoreProposal["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();

        var handoff = RetentionLogPrivacyRecurringScanContracts.ReadJsonObject(
            Path.Combine(workspace.DefaultPackageRoot, RetentionLogPrivacyRecurringScanArtifactGenerator.DownstreamHandoffPath.Replace('/', Path.DirectorySeparatorChar)),
            "downstream handoff");
        handoff["consumers"]!.AsArray()
            .OfType<JsonObject>()
            .Select(item => RetentionLogPrivacyRecurringScanContracts.GetString(item, "consumerId"))
            .Should()
            .Contain(new[] { "FEAT-166", "internal_audit_95_promotion_owner" });
    }

    [Fact]
    public void Promotion_CheckOnly_ShouldPassAfterPackageMode()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        var result = service.Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false,
            PublicOnly: true));

        result.CheckedFiles.Should().HaveCount(RetentionLogPrivacyRecurringScanArtifactGenerator.RequiredArtifactPaths.Length);
    }

    [Fact]
    public void Promotion_CheckOnly_DetectsManifestDrift()
    {
        using var workspace = TempRetentionLogPrivacyRecurringScanWorkspace.Create();
        var service = CreateService();
        service.Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModePackage,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));
        File.WriteAllText(
            Path.Combine(workspace.DefaultPackageRoot, RetentionLogPrivacyRecurringScanArtifactGenerator.ManifestPath),
            "drifted manifest");

        var act = () => service.Promote(new(
            workspace.Paths,
            RetentionLogPrivacyRecurringScanPromotionService.ModeCheckOnly,
            null,
            workspace.OutputRoot,
            FixedGeneratedAt,
            ValidateOnly: false));

        act.Should().Throw<RetentionLogPrivacyRecurringScanPromotionException>()
            .Which.Details.Should().Contain(error => error.Contains(RetentionLogPrivacyRecurringScanArtifactGenerator.ManifestPath, StringComparison.Ordinal));
    }

    private static RetentionLogPrivacyRecurringScanPromotionService CreateService() => new();

    private sealed class TempRetentionLogPrivacyRecurringScanWorkspace : IDisposable
    {
        private TempRetentionLogPrivacyRecurringScanWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "feat164-rlp-scan-" + Guid.NewGuid().ToString("N"));
            Paths = RetentionLogPrivacyRecurringScanPromotionPaths.FromWorkspaceRoot(Root);
            OutputRoot = Path.Combine(Root, "package-output");
            Directory.CreateDirectory(Paths.SchemasRoot);
            Directory.CreateDirectory(Paths.RulesRoot);
            Directory.CreateDirectory(Path.Combine(Paths.ExamplesRoot, "release-baseline"));
            Directory.CreateDirectory(Path.Combine(Paths.ExamplesRoot, "negative"));
            Directory.CreateDirectory(Paths.PackagesRoot);
            WriteSchemas();
            WriteRulesAndExamples();
        }

        public string Root { get; }

        public RetentionLogPrivacyRecurringScanPromotionPaths Paths { get; }

        public string OutputRoot { get; }

        public string DefaultPackageRoot => Path.Combine(
            OutputRoot,
            RetentionLogPrivacyRecurringScanPromotionPaths.PackageFamilyFolder,
            "FEAT164-RLP-SCAN-20260603-001");

        public static TempRetentionLogPrivacyRecurringScanWorkspace Create() => new();

        public JsonObject LoadSource() => RetentionLogPrivacyRecurringScanContracts.LoadSource(Paths);

        public IReadOnlyList<string> ValidateSource(JsonObject source, bool publicOnly = false) =>
            RetentionLogPrivacyRecurringScanContracts.ValidateSource(
                source,
                RetentionLogPrivacyRecurringScanContracts.FileSha256Hex(Path.Combine(Paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.ForbiddenMaterialCatalogFileName)),
                RetentionLogPrivacyRecurringScanContracts.FileSha256Hex(Path.Combine(Paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.OutputFamilyRegistryFileName)),
                KnownFamilies,
                publicOnly);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static readonly HashSet<string> KnownFamilies = new(StringComparer.Ordinal)
        {
            "logs",
            "diagnostics",
            "traces",
            "metrics",
            "support_exports",
            "public_packages",
            "reviewer_reports",
            "ci_outputs",
            "restricted_indexes",
        };

        private void WriteSchemas()
        {
            WriteJson(
                Path.Combine(Paths.SchemasRoot, RetentionLogPrivacyRecurringScanPromotionPaths.SourceSchemaFileName),
                new JsonObject
                {
                    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                    ["required"] = new JsonArray("schemaVersion"),
                });
            WriteJson(
                Path.Combine(Paths.SchemasRoot, RetentionLogPrivacyRecurringScanPromotionPaths.PackageManifestSchemaFileName),
                new JsonObject
                {
                    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                    ["required"] = new JsonArray("schemaVersion"),
                });
        }

        private void WriteRulesAndExamples()
        {
            var forbiddenCatalog = BuildForbiddenMaterialCatalog();
            var outputRegistry = BuildOutputFamilyRegistry();
            WriteJson(Path.Combine(Paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.ForbiddenMaterialCatalogFileName), forbiddenCatalog);
            WriteJson(Path.Combine(Paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.OutputFamilyRegistryFileName), outputRegistry);
            WriteJson(Path.Combine(Paths.ExamplesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.ResultCodesFileName), BuildResultCodes());
            WriteJson(Path.Combine(Paths.ExamplesRoot, "negative", RetentionLogPrivacyRecurringScanPromotionPaths.NegativeFixtureCatalogFileName), BuildNegativeFixtures());
            WriteJson(
                Path.Combine(Paths.ExamplesRoot, "release-baseline", RetentionLogPrivacyRecurringScanPromotionPaths.SourceFileName),
                BuildSource(
                    RetentionLogPrivacyRecurringScanContracts.FileSha256Hex(Path.Combine(Paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.ForbiddenMaterialCatalogFileName)),
                    RetentionLogPrivacyRecurringScanContracts.FileSha256Hex(Path.Combine(Paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.OutputFamilyRegistryFileName))));
        }

        private static JsonObject BuildForbiddenMaterialCatalog() =>
            new()
            {
                ["schemaVersion"] = "retention-log-privacy-forbidden-material-catalog.v1",
                ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
                ["categories"] = new JsonArray("voter_identity", "ballot_linkage", "raw_log_payload", "support_payload"),
            };

        private static JsonObject BuildOutputFamilyRegistry()
        {
            var families = new JsonArray();
            foreach (var familyId in KnownFamilies)
            {
                families.Add(new JsonObject { ["familyId"] = familyId });
            }

            return new JsonObject
            {
                ["schemaVersion"] = "retention-log-privacy-output-family-registry.v1",
                ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
                ["families"] = families,
            };
        }

        private static JsonObject BuildResultCodes() =>
            new()
            {
                ["schemaVersion"] = "retention-log-privacy-result-codes.v1",
                ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
                ["acceptedCode"] = "RLP164_ACCEPTED",
            };

        private static JsonObject BuildNegativeFixtures() =>
            new()
            {
                ["schemaVersion"] = "retention-log-privacy-negative-fixture-catalog.v1",
                ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
                ["fixtures"] = new JsonArray(
                    Fixture("stale-feat137-proof", "RLP164_FEAT137_PROOF_CURRENTNESS_BLOCKED"),
                    Fixture("missing-scanner-baseline", "RLP164_SCANNER_BASELINE_MISSING"),
                    Fixture("unclassified-output-family", "RLP164_UNCLASSIFIED_OUTPUT_FAMILY"),
                    Fixture("forbidden-voter-material", "RLP164_FORBIDDEN_VOTER_MATERIAL"),
                    Fixture("ballot-linkage", "RLP164_BALLOT_LINKAGE_FORBIDDEN"),
                    Fixture("raw-log", "RLP164_RAW_LOG_FORBIDDEN"),
                    Fixture("trace-label", "RLP164_TRACE_PAYLOAD_FORBIDDEN"),
                    Fixture("support-payload", "RLP164_SUPPORT_PAYLOAD_FORBIDDEN"),
                    Fixture("private-path", "RLP164_PRIVATE_PATH_FORBIDDEN"),
                    Fixture("direct-register-mutation", "RLP164_DIRECT_REGISTER_MUTATION_FORBIDDEN"),
                    Fixture("wrong-score-range", "RLP164_WRONG_SCORE_RANGE"),
                    Fixture("public-only-private-dependency", "RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY")),
            };

        private static JsonObject Fixture(string fixtureId, string expectedCode) =>
            new()
            {
                ["fixtureId"] = fixtureId,
                ["expectedResultCodes"] = new JsonArray(expectedCode),
            };

        private static JsonObject BuildSource(string catalogHash, string registryHash)
        {
            var families = new JsonArray();
            foreach (var familyId in KnownFamilies)
            {
                families.Add(new JsonObject
                {
                    ["familyId"] = familyId,
                    ["visibility"] = familyId is "support_exports" or "restricted_indexes" ? "restricted" : "public",
                    ["scannerDecision"] = familyId is "support_exports" or "restricted_indexes" ? "restricted_ref_only" : "scan_required",
                    ["publicPayloadAllowed"] = familyId is not ("support_exports" or "restricted_indexes"),
                    ["allowedPublicFields"] = new JsonArray("sourceRef", "resultCode"),
                    ["forbiddenFields"] = new JsonArray("rawPayload"),
                });
            }

            return new JsonObject
            {
                ["schemaVersion"] = RetentionLogPrivacyRecurringScanContracts.SourceSchemaVersion,
                ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
                ["scanRunId"] = "FEAT164-RLP-SCAN-20260603-001",
                ["generatedAt"] = "2026-06-03T00:00:00Z",
                ["readinessBaseline"] = new JsonObject
                {
                    ["registerVersionId"] = RetentionLogPrivacyRecurringScanContracts.CurrentRegisterVersionId,
                    ["dimensionId"] = RetentionLogPrivacyRecurringScanContracts.TargetDimensionId,
                    ["currentScore"] = 8,
                    ["proposedScore"] = 9,
                    ["targetBlockerId"] = RetentionLogPrivacyRecurringScanContracts.TargetBlockerId,
                },
                ["feat137Proof"] = new JsonObject
                {
                    ["packageId"] = RetentionLogPrivacyRecurringScanContracts.AcceptedFeat137PackageId,
                    ["packageHash"] = RetentionLogPrivacyRecurringScanContracts.AcceptedFeat137PackageHash,
                    ["privacyBoundaryVersion"] = RetentionLogPrivacyRecurringScanContracts.AcceptedFeat137PrivacyBoundaryVersion,
                    ["evidenceStatus"] = "accepted",
                    ["sourceRefs"] = new JsonArray(
                        new JsonObject
                        {
                            ["repository"] = "Hushnetwork-social/HushVoting-Readiness-Proof-Packages",
                            ["ref"] = "rlp-20260521-001",
                            ["path"] = "hushvoting-v1/retention-log-privacy/rlp-20260521-001/package/retention-log-privacy-proof-package.json",
                            ["sha256"] = RetentionLogPrivacyRecurringScanContracts.AcceptedFeat137PackageHash,
                        }),
                },
                ["runtimeProofFamily"] = new JsonObject
                {
                    ["requiredWhenLiveReportClaimed"] = true,
                    ["currentStatus"] = "accepted",
                    ["proofFamily"] = "retention_log_privacy",
                    ["allowedStatuses"] = new JsonArray("accepted"),
                    ["blockingStatuses"] = new JsonArray("missing", "stale", "mismatched", "superseded", "blocked", "unknown"),
                },
                ["scannerBaseline"] = new JsonObject
                {
                    ["scannerVersion"] = RetentionLogPrivacyRecurringScanContracts.RequiredScannerVersion,
                    ["rulesetVersion"] = RetentionLogPrivacyRecurringScanContracts.RequiredRulesetVersion,
                    ["forbiddenMaterialCatalogHash"] = catalogHash,
                    ["outputFamilyRegistryHash"] = registryHash,
                },
                ["outputFamilies"] = families,
                ["scanInputs"] = BuildScanInputs(),
                ["driftChecks"] = new JsonArray(
                    new JsonObject { ["checkId"] = "log-diagnostic-field-drift", ["target"] = "logs,diagnostics", ["failClosedWhenChanged"] = true },
                    new JsonObject { ["checkId"] = "trace-metrics-correlation-label-drift", ["target"] = "traces,metrics", ["failClosedWhenChanged"] = true }),
                ["resultCodes"] = new JsonArray("RLP164_ACCEPTED"),
                ["publicSafetyPolicy"] = new JsonObject
                {
                    ["publicOnlyValidation"] = true,
                    ["forbiddenPublicPathPatterns"] = new JsonArray("PrivateServer_ElectronicVoting"),
                    ["forbiddenClaimPhrases"] = new JsonArray("legal sufficiency"),
                },
                ["scorePolicy"] = new JsonObject
                {
                    ["dimensionId"] = RetentionLogPrivacyRecurringScanContracts.TargetDimensionId,
                    ["proposedScoreFrom"] = 8,
                    ["proposedScoreTo"] = 9,
                    ["directRegisterMutation"] = false,
                },
                ["restrictedEvidencePolicy"] = new JsonObject
                {
                    ["payloadPublished"] = false,
                    ["publicRefFieldsOnly"] = true,
                },
            };
        }

        private static JsonArray BuildScanInputs()
        {
            var array = new JsonArray();
            var index = 0;
            foreach (var familyId in KnownFamilies)
            {
                index++;
                array.Add(new JsonObject
                {
                    ["inputId"] = $"{familyId}-fixture",
                    ["outputFamilyId"] = familyId,
                    ["sourceRef"] = new JsonObject
                    {
                        ["repository"] = "Hushnetwork-social/Retention-Log-Privacy-Scans",
                        ["ref"] = "main",
                        ["path"] = $"examples/release-baseline/{familyId}.public.json",
                        ["sha256"] = new string(index.ToString(System.Globalization.CultureInfo.InvariantCulture)[0], 64),
                    },
                    ["expectedVisibility"] = familyId is "support_exports" or "restricted_indexes" ? "restricted" : "public",
                });
            }

            return array;
        }

        private static void WriteJson(string path, JsonObject json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, RetentionLogPrivacyRecurringScanContracts.CanonicalJson(json));
        }
    }
}

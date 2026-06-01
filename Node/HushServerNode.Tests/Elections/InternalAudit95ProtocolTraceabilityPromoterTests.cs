using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using InternalAudit95ProtocolTraceabilityPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class InternalAudit95ProtocolTraceabilityPromoterTests
{
    [Fact]
    public void SchemaSet_RequiredSchemas_ArePresentAndLoadable()
    {
        // Arrange
        var paths = CreateWorkspace();

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSchemaSet(paths.SchemasRoot);

        // Assert
        errors.Should().BeEmpty();
        foreach (var schemaFile in InternalAudit95ProtocolTraceabilityContracts.RequiredSchemaFiles)
        {
            File.Exists(Path.Combine(paths.SchemasRoot, schemaFile)).Should().BeTrue();
        }
    }

    [Fact]
    public void ReleaseBaselineSource_Validates()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidSource_GeneratesTraceMatrixAndInventory()
    {
        // Arrange
        var paths = CreateWorkspace();

        // Act
        var package = InternalAudit95ProtocolTraceabilityArtifactGenerator.Generate(
            paths,
            generatedAt: DateTimeOffset.Parse("2026-06-01T12:00:00Z"));
        var trace = ReadArtifactJson(package, InternalAudit95ProtocolTraceabilityArtifactGenerator.TraceMatrixPath);
        var inventory = ReadArtifactJson(package, InternalAudit95ProtocolTraceabilityArtifactGenerator.ArtifactInventoryPath);
        var stale = ReadArtifactJson(package, InternalAudit95ProtocolTraceabilityArtifactGenerator.StaleReferenceValidationPath);
        var orphan = ReadArtifactJson(package, InternalAudit95ProtocolTraceabilityArtifactGenerator.OrphanArtifactReportPath);

        // Assert
        package.Status.Should().Be("accepted_candidate");
        trace["rows"]!.AsArray().Count.Should().BeGreaterThan(0);
        inventory["entries"]!.AsArray()
            .OfType<JsonObject>()
            .Should().Contain(item => item["artifactId"]!.GetValue<string>() == "RDY-REG-v0.1.7-MANIFEST");
        stale["status"]!.GetValue<string>().Should().Be("passed");
        orphan["status"]!.GetValue<string>().Should().Be("passed");
    }

    [Fact]
    public void Source_WithDirectRegisterMutation_IsRejected()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        source["scorePolicy"]!.AsObject()["directRegisterMutation"] = true;

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);

        // Assert
        errors.Should().Contain(error => error.Contains("FEAT157_DIRECT_REGISTER_MUTATION_FORBIDDEN", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_WithMissingBlocker_IsRejected()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        source["scorePolicy"]!.AsObject()["targetBlockerId"] = "RDY-BLOCK-WRONG";

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);

        // Assert
        errors.Should().Contain(error => error.Contains("FEAT157_BLOCKER_OWNERSHIP_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_WithWrongScoreDimension_IsRejected()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        source["scorePolicy"]!.AsObject()["targetDimensionId"] = "RDY-DIM-999";

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);

        // Assert
        errors.Should().Contain(error => error.Contains("FEAT157_SCORE_DIMENSION_INVALID", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_WithoutTraceRequirements_IsRejected()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        source["traceRequirements"] = new JsonArray();

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);

        // Assert
        errors.Should().Contain(error => error.Contains("FEAT157_TRACE_ROW_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_WithEmptyTraceArtifactLists_IsRejected()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        var trace = source["traceRequirements"]!.AsArray()[0]!.AsObject();
        trace["sourceArtifactIds"] = new JsonArray();
        trace["requiredGeneratedArtifactIds"] = new JsonArray();

        // Act
        var errors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);

        // Assert
        errors.Should().Contain(error => error.Contains("FEAT157_TRACE_ROW_MISSING", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_WithWrongHash_BlocksStaleReferenceValidation()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        source["sourceArtifacts"]!.AsArray()[0]!.AsObject()["expectedSha256Hash"] =
            $"sha256:{new string('0', 64)}";
        var sourceInput = WriteSource(paths, source, "wrong-hash");

        // Act
        var package = InternalAudit95ProtocolTraceabilityArtifactGenerator.Generate(
            paths,
            sourceInput,
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"));
        var stale = ReadArtifactJson(package, InternalAudit95ProtocolTraceabilityArtifactGenerator.StaleReferenceValidationPath);

        // Assert
        package.Status.Should().Be("blocked");
        package.Blockers.Should().Contain("FEAT157_BASELINE_REGISTER_INVALID");
        stale["status"]!.GetValue<string>().Should().Be("failed");
    }

    [Fact]
    public void ScoreBearingGeneratedArtifact_WithoutTrace_IsReportedAsOrphan()
    {
        // Arrange
        var paths = CreateWorkspace();
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths);
        source["generatedArtifactContracts"]!.AsArray().Add(new JsonObject
        {
            ["artifactId"] = "FEAT157-UNTRACED-SCORE-ARTIFACT",
            ["fileName"] = "feat157-untraced-score-artifact.json",
            ["artifactType"] = "json",
            ["visibility"] = "internal",
            ["classification"] = "score-bearing",
            ["requiredForManifest"] = true,
            ["requiredFields"] = new JsonArray("artifactId"),
        });
        var sourceInput = WriteSource(paths, source, "orphan");

        // Act
        var package = InternalAudit95ProtocolTraceabilityArtifactGenerator.Generate(
            paths,
            sourceInput,
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"));
        var orphan = ReadArtifactJson(package, InternalAudit95ProtocolTraceabilityArtifactGenerator.OrphanArtifactReportPath);

        // Assert
        package.Status.Should().Be("blocked");
        package.Blockers.Should().Contain("FEAT157_ORPHAN_ARTIFACT");
        orphan["checks"]!.AsArray()
            .OfType<JsonObject>()
            .Should().Contain(item =>
                item["artifactId"]!.GetValue<string>() == "FEAT157-UNTRACED-SCORE-ARTIFACT" &&
                item["status"]!.GetValue<string>() == "failed");
    }

    private static InternalAudit95ProtocolTraceabilityPaths CreateWorkspace()
    {
        var root = HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("feat157-traceability-");
        var paths = InternalAudit95ProtocolTraceabilityPaths.FromWorkspaceRoot(root);
        Directory.CreateDirectory(paths.SchemasRoot);
        Directory.CreateDirectory(Path.Combine(paths.ExamplesRoot, "release-baseline"));
        WriteMinimalSchema(Path.Combine(paths.SchemasRoot, "internal-audit-95-protocol-traceability-source.schema.json"));
        WriteMinimalSchema(Path.Combine(paths.SchemasRoot, "internal-audit-95-protocol-traceability-package.schema.json"));

        var artifacts = new Dictionary<string, (string Path, string Content)>(StringComparer.Ordinal)
        {
            ["RDY-REG-v0.1.7-MANIFEST"] = ("hush-documents/PrivateServer_ElectronicVoting/HushVoting-Readiness-Register/v0.1.7/readiness-register-manifest.json", "readiness register v0.1.7"),
            ["PROTOCOL-OMEGA-v1.2.0-MANIFEST"] = ("hush-documents/PrivateServer_ElectronicVoting/Protocol-Omega-HushVoting-v1-Artifacts/v1.2.0/ProtocolOmegaPackageManifest.json", "protocol omega v1.2.0"),
            ["DEPLOYMENT-PROOF-CATALOG"] = ("Deployment-Proof-Packages/deployment-proof-catalog.json", "deployment proof catalog"),
            ["VERIFIER-CORPUS-v0.2.0-MANIFEST"] = ("HushVoting-Verifier-Corpus/hushvoting-v1/v0.2.0/corpus-manifest.json", "verifier corpus v0.2.0"),
            ["MEMORY-BANK-OVERVIEW-RDY-REG-v0.1.5"] = ("hush-memory-bank/Overview/HushVotingReadiness/Readiness-Register/readiness-register.json", "memory bank overview v0.1.5"),
        };

        foreach (var (_, value) in artifacts)
        {
            var path = Path.Combine(root, value.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value.Content);
        }

        var source = BuildSource(artifacts);
        WriteSource(paths, source, "release-baseline");
        return paths;
    }

    private static JsonObject BuildSource(IReadOnlyDictionary<string, (string Path, string Content)> artifacts) =>
        new()
        {
            ["schemaVersion"] = InternalAudit95ProtocolTraceabilityContracts.SourceSchemaVersion,
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.PackageAnchor,
            ["featureId"] = InternalAudit95ProtocolTraceabilityContracts.FeatureId,
            ["status"] = "candidate",
            ["generatedAt"] = "2026-06-01T12:00:00Z",
            ["packageAnchor"] = InternalAudit95ProtocolTraceabilityContracts.PackageAnchor,
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = InternalAudit95ProtocolTraceabilityContracts.BaselineRegisterVersionId,
                ["registerVersion"] = "v0.1.7",
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 80,
                ["internalAuditTargetScore"] = 95,
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
                ["authoritativeSourceArtifactId"] = "RDY-REG-v0.1.7-MANIFEST",
                ["overviewDriftCheck"] = new JsonObject
                {
                    ["sourceArtifactId"] = "MEMORY-BANK-OVERVIEW-RDY-REG-v0.1.5",
                    ["expectedRegisterVersionId"] = InternalAudit95ProtocolTraceabilityContracts.DriftCheckRegisterVersionId,
                    ["scoringBaseline"] = false,
                    ["expectedFinding"] = "drift_detected_not_scoring_baseline",
                },
            },
            ["scorePolicy"] = new JsonObject
            {
                ["targetDimensionId"] = InternalAudit95ProtocolTraceabilityContracts.TargetDimensionId,
                ["currentScore"] = 8,
                ["proposedScore"] = 10,
                ["targetBlockerId"] = InternalAudit95ProtocolTraceabilityContracts.TargetBlockerId,
                ["blockerOwnerFeatureId"] = InternalAudit95ProtocolTraceabilityContracts.FeatureId,
                ["scoreChangeAllowed"] = true,
                ["directRegisterMutation"] = false,
                ["canonicalRegisterMutationOwner"] = "later_internal_audit_95_promotion_pass",
                ["minimumPassingValidationStatus"] = "passed",
            },
            ["sourceArtifacts"] = new JsonArray(
                SourceArtifact("RDY-REG-v0.1.7-MANIFEST", "readiness-register", artifacts, "authoritative-baseline", "restricted", "score-bearing", true),
                SourceArtifact("PROTOCOL-OMEGA-v1.2.0-MANIFEST", "protocol-omega", artifacts, "score-evidence", "restricted", "score-bearing", true),
                SourceArtifact("DEPLOYMENT-PROOF-CATALOG", "deployment-proof", artifacts, "supporting-evidence", "restricted", "supporting", true),
                SourceArtifact("VERIFIER-CORPUS-v0.2.0-MANIFEST", "verifier-corpus", artifacts, "supporting-evidence", "internal", "supporting", true),
                SourceArtifact("MEMORY-BANK-OVERVIEW-RDY-REG-v0.1.5", "memory-bank-overview", artifacts, "drift-check-only", "internal", "drift-check-only", false)),
            ["traceRequirements"] = new JsonArray(
                TraceRequirement(
                    "TR-FEAT157-RDY-DIM-001-BASELINE",
                    ["RDY-REG-v0.1.7-MANIFEST", "MEMORY-BANK-OVERVIEW-RDY-REG-v0.1.5"],
                    ["FEAT157-STALE-REFERENCE-VALIDATION", "FEAT157-AUDITOR-TRACE-MATRIX"]),
                TraceRequirement(
                    "TR-FEAT157-PROTOCOL-PROOF-PACKAGE",
                    ["PROTOCOL-OMEGA-v1.2.0-MANIFEST"],
                    ["FEAT157-AUDITOR-TRACE-MATRIX", "FEAT157-ARTIFACT-INVENTORY"]),
                TraceRequirement(
                    "TR-FEAT157-SUPPORTING-EVIDENCE",
                    ["DEPLOYMENT-PROOF-CATALOG", "VERIFIER-CORPUS-v0.2.0-MANIFEST"],
                    ["FEAT157-AUDITOR-TRACE-MATRIX", "FEAT157-ORPHAN-ARTIFACT-REPORT"])),
            ["generatedArtifactContracts"] = new JsonArray(
                GeneratedContract("FEAT157-SOURCE-SNAPSHOT", "feat157-traceability-source-snapshot.json", "supporting"),
                GeneratedContract("FEAT157-AUDITOR-TRACE-MATRIX", "feat157-auditor-trace-matrix.json", "score-bearing"),
                GeneratedContract("FEAT157-ARTIFACT-INVENTORY", "feat157-artifact-inventory.json", "score-bearing"),
                GeneratedContract("FEAT157-STALE-REFERENCE-VALIDATION", "feat157-stale-reference-validation.json", "score-bearing"),
                GeneratedContract("FEAT157-ORPHAN-ARTIFACT-REPORT", "feat157-orphan-artifact-report.json", "score-bearing")),
            ["validationRules"] = new JsonArray(
                new JsonObject
                {
                    ["ruleId"] = "FEAT157-RULE-BASELINE-REGISTER",
                    ["severity"] = "error",
                    ["blocksScoreMovement"] = true,
                    ["description"] = "Authoritative register must be valid.",
                    ["expectedFailureCode"] = "FEAT157_BASELINE_REGISTER_INVALID",
                }),
            ["publicSafeOutputRules"] = new JsonObject
            {
                ["allowedPublicPhrases"] = new JsonArray("release-bound internal traceability evidence"),
                ["forbiddenMaterialNeedles"] = new JsonArray("C:\\myWork\\HushNetworkOrg", "credential"),
                ["forbiddenClaimNeedles"] = new JsonArray("certified", "public/state election ready"),
                ["numericScorePublicDisclosure"] = false,
            },
            ["restrictedReviewerRules"] = new JsonObject
            {
                ["payloadInliningAllowed"] = false,
                ["rawEvidenceCopied"] = false,
                ["allowedRefTypes"] = new JsonArray("path", "sha256", "feature_id"),
            },
            ["downstreamConsumers"] = new JsonArray(
                new JsonObject
                {
                    ["featureId"] = "FEAT-158",
                    ["dimensionId"] = "RDY-DIM-002",
                    ["allowedUse"] = "Reuse traceability package refs.",
                    ["forbiddenClaim"] = "Verifier corpus breadth is not complete.",
                }),
            ["signoff"] = new JsonObject
            {
                ["engineeringOwner"] = "Paulo Aboim Pinto / Forge",
                ["status"] = "candidate",
                ["samePersonTwoHatAllowed"] = true,
            },
            ["residualRisks"] = new JsonArray("External review is not complete."),
        };

    private static JsonObject SourceArtifact(
        string artifactId,
        string family,
        IReadOnlyDictionary<string, (string Path, string Content)> artifacts,
        string role,
        string visibility,
        string classification,
        bool requiredForScore)
    {
        var artifact = artifacts[artifactId];
        var hash = InternalAudit95ProtocolTraceabilityContracts.Sha256Hex(Encoding.UTF8.GetBytes(artifact.Content));
        return new JsonObject
        {
            ["artifactId"] = artifactId,
            ["family"] = family,
            ["logicalRef"] = artifact.Path,
            ["resolvedPath"] = artifact.Path,
            ["expectedSha256Hash"] = $"sha256:{hash}",
            ["hashBasis"] = "file_sha256",
            ["role"] = role,
            ["visibility"] = visibility,
            ["classification"] = classification,
            ["requiredForScore"] = requiredForScore,
            ["staleWhen"] = new JsonArray("hash changes"),
        };
    }

    private static JsonObject TraceRequirement(
        string traceRequirementId,
        string[] sourceArtifactIds,
        string[] generatedArtifactIds) =>
        new()
        {
            ["traceRequirementId"] = traceRequirementId,
            ["claimLevel"] = "production_organizational_rollout",
            ["dimensionId"] = InternalAudit95ProtocolTraceabilityContracts.TargetDimensionId,
            ["blockerId"] = InternalAudit95ProtocolTraceabilityContracts.TargetBlockerId,
            ["acceptanceGateIds"] = new JsonArray("AT-RDY-001"),
            ["sourceRequirement"] = "Trace requirement under test.",
            ["sourceFeatureIds"] = new JsonArray("FEAT-157"),
            ["sourceArtifactIds"] = new JsonArray(sourceArtifactIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["requiredGeneratedArtifactIds"] = new JsonArray(generatedArtifactIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["classification"] = "score-bearing",
            ["residualRiskRequired"] = true,
        };

    private static JsonObject GeneratedContract(string artifactId, string fileName, string classification) =>
        new()
        {
            ["artifactId"] = artifactId,
            ["fileName"] = fileName,
            ["artifactType"] = "json",
            ["visibility"] = "internal",
            ["classification"] = classification,
            ["requiredForManifest"] = true,
            ["requiredFields"] = new JsonArray("artifactId"),
        };

    private static string WriteSource(
        InternalAudit95ProtocolTraceabilityPaths paths,
        JsonObject source,
        string folderName)
    {
        var folder = Path.Combine(paths.ExamplesRoot, folderName);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, InternalAudit95ProtocolTraceabilityPaths.SourceFileName);
        File.WriteAllText(path, InternalAudit95ProtocolTraceabilityContracts.CanonicalJson(source));
        return folder;
    }

    private static JsonObject ReadArtifactJson(InternalAudit95ProtocolTraceabilityGeneratedPackage package, string relativePath)
    {
        var artifact = package.Artifacts.Single(item => item.RelativePath == relativePath);
        return JsonNode.Parse(artifact.Content)?.AsObject() ??
            throw new InvalidOperationException($"Artifact {relativePath} is not a JSON object.");
    }

    private static void WriteMinimalSchema(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "required": ["schemaVersion"]
            }
            """);
    }
}

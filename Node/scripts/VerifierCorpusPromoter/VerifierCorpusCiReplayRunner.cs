using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using HushNode.Elections;
using HushShared.Elections.Verification.Model;

namespace VerifierCorpusPromoter;

public sealed record VerifierCorpusCiReplayOptions(
    string CorpusRoot,
    DateTimeOffset GeneratedAt,
    string CorpusRepository = "https://github.com/Hushnetwork-social/HushVoting-Verifier-Corpus",
    string CorpusRepositoryRef = "0000000000000000000000000000000000000000",
    string CorpusVersion = "v0.3.0",
    string VerifierRepository = "https://github.com/Hushnetwork-social/hush-server-node",
    string VerifierSourceRef = "0000000000000000000000000000000000000000",
    string VerifierHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
    string WorkflowName = "local-verifier-corpus-ci",
    string WorkflowPath = ".github/workflows/verifier-corpus-ci.yml",
    string WorkflowRunId = "local-replay",
    int WorkflowRunAttempt = 1);

public sealed record VerifierCorpusCiFixtureReplayResult(
    string FixtureId,
    int ExpectedExitCode,
    int ObservedExitCode,
    string ExpectedPrimaryResultCode,
    string ObservedPrimaryResultCode,
    string ExpectedNormalizedOutputHash,
    string ObservedNormalizedOutputHash,
    string OutputRef,
    string Status,
    IReadOnlyList<string> MismatchReasons);

public sealed record VerifierCorpusCiReplayResult(
    string CorpusRoot,
    string RunStatus,
    string PublicSafetyStatus,
    int FixtureCount,
    int MatchedFixtureCount,
    int MismatchCount,
    int UnexpectedPublicFindingCount,
    IReadOnlyList<VerifierCorpusCiFixtureReplayResult> Fixtures,
    IReadOnlyList<string> ContractErrors)
{
    public bool Passed =>
        string.Equals(RunStatus, "accepted", StringComparison.Ordinal) &&
        ContractErrors.Count == 0;
}

public sealed class VerifierCorpusCiReplayRunner
{
    public const string ManifestRelativePath = "validation/ci-verifier-run-manifest.json";
    public const string SummaryJsonRelativePath = "validation/ci-verifier-output-summary.json";
    public const string SummaryMarkdownRelativePath = "validation/ci-verifier-output-summary.md";
    public const string PublicReviewerHandoffRelativePath = "validation/ci-public-reviewer-handoff.json";
    public const string Audit95ReadinessFragmentRelativePath = "readiness/verifier-corpus-audit95-readiness-fragment.json";
    public const string Audit95ScoreProposalRelativePath = "readiness/verifier-corpus-audit95-score-proposal.json";

    public async Task<VerifierCorpusCiReplayResult> ReplayAsync(
        VerifierCorpusCiReplayOptions options,
        CancellationToken cancellationToken = default)
    {
        var corpusRoot = Path.GetFullPath(options.CorpusRoot);
        if (!Directory.Exists(corpusRoot))
        {
            throw new DirectoryNotFoundException($"Verifier corpus root does not exist: {corpusRoot}");
        }

        var fixtureIndex = ReadJsonObject(Path.Combine(corpusRoot, "fixtures", "fixture-index.json"));
        var fixtureRows = fixtureIndex["fixtures"]?.AsArray()
            .OfType<JsonObject>()
            .OrderBy(x => RequiredString(x, "fixtureId", "fixture-index"))
            .ToArray() ?? throw new InvalidOperationException("fixtures/fixture-index.json must contain a fixtures array.");

        var publicScanFindings = VerifierCorpusGenerator.ScanPublicOutput(corpusRoot);
        var unexpectedPublicFindingCount = publicScanFindings.Count(x => !x.ExpectedTamperFixture);
        var publicSafetyStatus = unexpectedPublicFindingCount == 0 ? "pass" : "blocked";

        var results = new List<VerifierCorpusCiFixtureReplayResult>();
        foreach (var fixture in fixtureRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fixtureId = RequiredString(fixture, "fixtureId", "fixture-index");
            var packagePath = RequiredString(fixture, "packagePath", fixtureId);
            var expectedResultRef = RequiredString(fixture, "expectedResultRef", fixtureId);
            var expectedResult = ReadJsonObject(Path.Combine(corpusRoot, ToLocalPath(expectedResultRef)));
            var profileId = GetStringOrDefault(fixture, "profileId") ??
                RequiredString(expectedResult, "profileId", $"{fixtureId} expected-result");
            var expectedExitCode = RequiredInt(expectedResult, "expectedExitCode", $"{fixtureId} expected-result");
            var expectedPrimaryResultCode = RequiredString(
                expectedResult,
                "expectedPrimaryResultCode",
                $"{fixtureId} expected-result");
            var expectedNormalizedOutputHash = RequiredString(
                expectedResult,
                "normalizedOutputHash",
                $"{fixtureId} expected-result");

            var outputRef = $"validation/ci-verifier-output/{fixtureId}/VerifierOutput.json";
            var outputDirectory = Path.Combine(corpusRoot, "validation", "ci-verifier-output", fixtureId);
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            var verification = await new HushVotingPackageVerifier().VerifyAsync(
                new HushVotingPackageVerificationRequest(
                    ResolvePackagePath(corpusRoot, packagePath),
                    profileId,
                    outputDirectory),
                cancellationToken);

            var normalizedOutput = VerifierCorpusGenerator.NormalizeVerifierOutput(verification.Output);
            var observedNormalizedOutputHash = VerifierCorpusGenerator.Sha256Text(
                VerifierCorpusGenerator.CanonicalJson(normalizedOutput));
            var observedPrimaryResultCode = ResolveObservedPrimaryResultCode(
                verification.Output,
                expectedPrimaryResultCode);
            var mismatchReasons = ResolveMismatchReasons(
                expectedResult,
                verification,
                expectedExitCode,
                expectedPrimaryResultCode,
                observedPrimaryResultCode,
                expectedNormalizedOutputHash,
                observedNormalizedOutputHash);

            results.Add(new VerifierCorpusCiFixtureReplayResult(
                fixtureId,
                expectedExitCode,
                verification.ExitCode,
                expectedPrimaryResultCode,
                observedPrimaryResultCode,
                expectedNormalizedOutputHash,
                observedNormalizedOutputHash,
                outputRef,
                mismatchReasons.Count == 0 ? "matched" : "mismatch",
                mismatchReasons));
        }

        var mismatchCount = results.Count(x => x.MismatchReasons.Count > 0);
        var runStatus = unexpectedPublicFindingCount > 0
            ? "blocked"
            : mismatchCount == 0
                ? "accepted"
                : "failed";
        var manifest = BuildRunManifest(
            options,
            corpusRoot,
            results,
            publicScanFindings,
            publicSafetyStatus,
            runStatus);
        var contractErrors = VerifierCorpusContracts.ValidateCiRunManifest(manifest);

        await WriteJsonAsync(corpusRoot, ManifestRelativePath, manifest, cancellationToken);
        await WriteJsonAsync(
            corpusRoot,
            SummaryJsonRelativePath,
            BuildSummaryJson(runStatus, publicSafetyStatus, results, publicScanFindings),
            cancellationToken);
        await WriteTextLfAsync(
            Path.Combine(corpusRoot, ToLocalPath(SummaryMarkdownRelativePath)),
            BuildSummaryMarkdown(runStatus, publicSafetyStatus, results, publicScanFindings),
            cancellationToken);
        await WriteJsonAsync(
            corpusRoot,
            PublicReviewerHandoffRelativePath,
            BuildPublicReviewerHandoff(options, corpusRoot, manifest, results, publicScanFindings, publicSafetyStatus, runStatus),
            cancellationToken);
        if (IsAudit95Replay(options))
        {
            await WriteJsonAsync(
                corpusRoot,
                Audit95ReadinessFragmentRelativePath,
                BuildAudit95ReadinessFragment(options, corpusRoot, manifest, results, publicScanFindings, publicSafetyStatus, runStatus),
                cancellationToken);
            await WriteJsonAsync(
                corpusRoot,
                Audit95ScoreProposalRelativePath,
                BuildAudit95ScoreProposal(options, corpusRoot, manifest, results, publicScanFindings, publicSafetyStatus, runStatus),
                cancellationToken);
        }

        return new VerifierCorpusCiReplayResult(
            corpusRoot,
            contractErrors.Count == 0 ? runStatus : "failed",
            publicSafetyStatus,
            results.Count,
            results.Count(x => x.MismatchReasons.Count == 0),
            mismatchCount,
            unexpectedPublicFindingCount,
            results,
            contractErrors);
    }

    private static JsonObject BuildRunManifest(
        VerifierCorpusCiReplayOptions options,
        string corpusRoot,
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings,
        string publicSafetyStatus,
        string runStatus)
    {
        var fixtures = new JsonArray();
        foreach (var result in results)
        {
            fixtures.Add(BuildFixtureJson(result));
        }

        var findings = new JsonArray();
        foreach (var finding in publicScanFindings
                     .Where(x => !x.ExpectedTamperFixture)
                     .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                     .ThenBy(x => x.Category, StringComparer.Ordinal))
        {
            findings.Add(new JsonObject
            {
                ["relativePath"] = finding.RelativePath,
                ["category"] = finding.Category,
                ["evidence"] = finding.Evidence,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-ci-run-manifest.v1",
            ["corpusRepository"] = options.CorpusRepository,
            ["corpusRepositoryRef"] = options.CorpusRepositoryRef,
            ["corpusVersion"] = options.CorpusVersion,
            ["corpusManifestHash"] = Sha256File(Path.Combine(corpusRoot, "corpus-manifest.json")),
            ["verifierRepository"] = options.VerifierRepository,
            ["verifierSourceRef"] = options.VerifierSourceRef,
            ["verifierHash"] = options.VerifierHash,
            ["workflowName"] = options.WorkflowName,
            ["workflowPath"] = options.WorkflowPath,
            ["workflowRunId"] = options.WorkflowRunId,
            ["workflowRunAttempt"] = options.WorkflowRunAttempt,
            ["runStatus"] = runStatus,
            ["publicSafetyStatus"] = publicSafetyStatus,
            ["fixtureCount"] = results.Count,
            ["matchedFixtureCount"] = results.Count(x => x.MismatchReasons.Count == 0),
            ["mismatchCount"] = results.Count(x => x.MismatchReasons.Count > 0),
            ["unexpectedPublicFindingCount"] = publicScanFindings.Count(x => !x.ExpectedTamperFixture),
            ["expectedTamperFindingCount"] = publicScanFindings.Count(x => x.ExpectedTamperFixture),
            ["generatedAt"] = options.GeneratedAt.UtcDateTime.ToString("O"),
            ["fixtures"] = fixtures,
            ["unexpectedPublicFindings"] = findings,
        };
    }

    private static JsonObject BuildFixtureJson(VerifierCorpusCiFixtureReplayResult result)
    {
        var reasons = new JsonArray();
        foreach (var reason in result.MismatchReasons)
        {
            reasons.Add(reason);
        }

        return new JsonObject
        {
            ["fixtureId"] = result.FixtureId,
            ["expectedExitCode"] = result.ExpectedExitCode,
            ["observedExitCode"] = result.ObservedExitCode,
            ["expectedPrimaryResultCode"] = result.ExpectedPrimaryResultCode,
            ["observedPrimaryResultCode"] = result.ObservedPrimaryResultCode,
            ["expectedNormalizedOutputHash"] = result.ExpectedNormalizedOutputHash,
            ["normalizedOutputHash"] = result.ObservedNormalizedOutputHash,
            ["outputRef"] = result.OutputRef,
            ["status"] = result.Status,
            ["mismatchReasons"] = reasons,
        };
    }

    private static JsonObject BuildSummaryJson(
        string runStatus,
        string publicSafetyStatus,
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings)
    {
        var fixtures = new JsonArray();
        foreach (var result in results.OrderBy(x => x.FixtureId, StringComparer.Ordinal))
        {
            fixtures.Add(BuildFixtureJson(result));
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-ci-output-summary.v1",
            ["runStatus"] = runStatus,
            ["publicSafetyStatus"] = publicSafetyStatus,
            ["fixtureCount"] = results.Count,
            ["matchedFixtureCount"] = results.Count(x => x.MismatchReasons.Count == 0),
            ["mismatchCount"] = results.Count(x => x.MismatchReasons.Count > 0),
            ["unexpectedPublicFindingCount"] = publicScanFindings.Count(x => !x.ExpectedTamperFixture),
            ["summaryText"] = $"Verifier corpus replay {runStatus}: {results.Count(x => x.MismatchReasons.Count == 0)}/{results.Count} fixtures matched.",
            ["fixtures"] = fixtures,
        };
    }

    private static JsonObject BuildPublicReviewerHandoff(
        VerifierCorpusCiReplayOptions options,
        string corpusRoot,
        JsonObject manifest,
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings,
        string publicSafetyStatus,
        string runStatus) =>
        new()
        {
            ["schemaVersion"] = "verifier-corpus-ci-public-reviewer-handoff.v1",
            ["handoffId"] = $"FEAT-158-{options.CorpusVersion}-public-reviewer-handoff",
            ["producerFeature"] = "FEAT-158",
            ["corpusVersion"] = options.CorpusVersion,
            ["publicCorpus"] = new JsonObject
            {
                ["repository"] = options.CorpusRepository,
                ["repositoryRef"] = options.CorpusRepositoryRef,
                ["manifestRef"] = "corpus-manifest.json",
                ["manifestHash"] = manifest["corpusManifestHash"]?.GetValue<string>() ?? Sha256File(Path.Combine(corpusRoot, "corpus-manifest.json")),
                ["fixtureIndexRef"] = "fixtures/fixture-index.json",
                ["fixtureIndexHash"] = Sha256File(Path.Combine(corpusRoot, "fixtures", "fixture-index.json")),
            },
            ["verifier"] = new JsonObject
            {
                ["repository"] = options.VerifierRepository,
                ["sourceRef"] = options.VerifierSourceRef,
                ["hash"] = options.VerifierHash,
            },
            ["workflow"] = new JsonObject
            {
                ["name"] = options.WorkflowName,
                ["path"] = options.WorkflowPath,
                ["runId"] = options.WorkflowRunId,
                ["runAttempt"] = options.WorkflowRunAttempt,
                ["generatedAt"] = options.GeneratedAt.UtcDateTime.ToString("O"),
            },
            ["replayEvidence"] = new JsonObject
            {
                ["runStatus"] = runStatus,
                ["publicSafetyStatus"] = publicSafetyStatus,
                ["fixtureCount"] = results.Count,
                ["matchedFixtureCount"] = results.Count(x => x.MismatchReasons.Count == 0),
                ["mismatchCount"] = results.Count(x => x.MismatchReasons.Count > 0),
                ["unexpectedPublicFindingCount"] = publicScanFindings.Count(x => !x.ExpectedTamperFixture),
                ["runManifestRef"] = ManifestRelativePath,
                ["outputSummaryJsonRef"] = SummaryJsonRelativePath,
                ["outputSummaryMarkdownRef"] = SummaryMarkdownRelativePath,
            },
            ["cleanMachineReplay"] = new JsonObject
            {
                ["publicInstructionsRef"] = "README.md",
                ["publicWorkflowRef"] = options.WorkflowPath,
                ["privateRepositoriesRequired"] = false,
                ["backendRequired"] = false,
                ["databaseRequired"] = false,
                ["cloudAccountRequired"] = false,
                ["localChainRequired"] = false,
            },
            ["downstreamOwners"] = BuildDownstreamOwners(),
            ["outOfScope"] = new JsonArray
            {
                "external audit signoff",
                "legal sufficiency",
                "certification",
                "public or state election authority approval",
            },
            ["restrictedPackage"] = new JsonObject
            {
                ["requiredForPublicCi"] = false,
                ["copiedIntoPublicCorpus"] = false,
                ["status"] = "not_produced_for_public_replay",
            },
            ["publicBoundaryStatement"] = "Synthetic public verifier corpus only. The CI replay proves fixture replay and public-safe packaging; it does not claim authority approval, legal sufficiency, certification, or real customer operation.",
        };

    private static JsonObject BuildAudit95ReadinessFragment(
        VerifierCorpusCiReplayOptions options,
        string corpusRoot,
        JsonObject manifest,
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings,
        string publicSafetyStatus,
        string runStatus)
    {
        var accepted = IsAcceptedAudit95Replay(results, publicScanFindings, publicSafetyStatus, runStatus);

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-audit95-readiness-fragment.v1",
            ["fragmentId"] = $"AT-RDY-007-{options.CorpusVersion}-audit95",
            ["producerFeature"] = "FEAT-158",
            ["dimensionId"] = "RDY-DIM-002",
            ["targetBlocker"] = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM002-001",
            ["status"] = accepted ? "accepted_candidate" : "blocked",
            ["visibility"] = "public",
            ["doesNotMutateRegister"] = true,
            ["evidenceRefs"] = BuildAudit95EvidenceRefs(corpusRoot, manifest),
            ["checkResults"] = new JsonArray
            {
                new JsonObject
                {
                    ["checkId"] = "ci-replay-accepted",
                    ["status"] = runStatus == "accepted" ? "pass" : "fail",
                    ["runStatus"] = runStatus,
                },
                new JsonObject
                {
                    ["checkId"] = "fixture-output-match",
                    ["status"] = results.All(x => x.MismatchReasons.Count == 0) ? "pass" : "fail",
                    ["fixtureCount"] = results.Count,
                    ["matchedFixtureCount"] = results.Count(x => x.MismatchReasons.Count == 0),
                },
                new JsonObject
                {
                    ["checkId"] = "public-safe",
                    ["status"] = publicSafetyStatus == "pass" && publicScanFindings.All(x => x.ExpectedTamperFixture) ? "pass" : "fail",
                    ["unexpectedFindingCount"] = publicScanFindings.Count(x => !x.ExpectedTamperFixture),
                },
            },
            ["claimEffect"] = "Proposal-only candidate evidence for RDY-DIM-002 8 -> 10 and RDY-BLOCK-INTERNAL_AUDIT_95_DIM002-001 resolution. Canonical readiness register mutation remains disabled.",
            ["residualRisk"] = "Does not prove external audit signoff, legal sufficiency, certification, public/state election readiness, or real customer operation.",
            ["promotionInstructions"] = "FEAT-130 or the readiness promoter may consume this fragment only after maintainer review of the public corpus release, CI run manifest, and public reviewer handoff.",
        };
    }

    private static JsonObject BuildAudit95ScoreProposal(
        VerifierCorpusCiReplayOptions options,
        string corpusRoot,
        JsonObject manifest,
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings,
        string publicSafetyStatus,
        string runStatus)
    {
        var accepted = IsAcceptedAudit95Replay(results, publicScanFindings, publicSafetyStatus, runStatus);

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-audit95-score-proposal.v1",
            ["proposalId"] = $"RDY-DIM-002-{options.CorpusVersion}-audit95-score-proposal",
            ["producerFeature"] = "FEAT-158",
            ["dimensionId"] = "RDY-DIM-002",
            ["proposedScoreFrom"] = 8,
            ["proposedScoreTo"] = 10,
            ["status"] = accepted ? "accepted_candidate" : "blocked",
            ["doesNotMutateRegister"] = true,
            ["generatedAt"] = options.GeneratedAt.UtcDateTime.ToString("O"),
            ["targetBlocker"] = new JsonObject
            {
                ["blockerId"] = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM002-001",
                ["currentStatus"] = "amber/open",
                ["proposedStatus"] = accepted ? "green/resolved" : "amber/open",
                ["resolutionRationale"] = "Audit95 verifier corpus replay is public-ref-bound, deterministic, public-safe, and covers the expected sample/tamper matrix.",
            },
            ["evidenceRefs"] = BuildAudit95EvidenceRefs(corpusRoot, manifest),
            ["scoreMovementRationale"] = new JsonObject
            {
                ["baselineEvidence"] = "FEAT-151 v0.2.0 established RDY-DIM-002 score-to-8 verifier corpus breadth.",
                ["audit95Evidence"] = $"FEAT-158 v0.3.0 replays {results.Count} fixtures in CI with {results.Count(x => x.MismatchReasons.Count == 0)} matched outputs and {publicScanFindings.Count(x => !x.ExpectedTamperFixture)} unexpected public-safety findings.",
                ["remainingLimits"] = "This proposal does not claim external audit signoff, legal sufficiency, certification, public/state election readiness, or real customer operation.",
            },
            ["promotionOwner"] = "FEAT-130 readiness promotion or later explicit readiness-register promotion flow",
        };
    }

    private static JsonArray BuildAudit95EvidenceRefs(string corpusRoot, JsonObject manifest) =>
    [
        new JsonObject
        {
            ["path"] = "corpus-manifest.json",
            ["sha256Hash"] = manifest["corpusManifestHash"]?.GetValue<string>() ?? Sha256File(Path.Combine(corpusRoot, "corpus-manifest.json")),
        },
        new JsonObject
        {
            ["path"] = "fixtures/fixture-index.json",
            ["sha256Hash"] = Sha256File(Path.Combine(corpusRoot, "fixtures", "fixture-index.json")),
        },
        new JsonObject
        {
            ["path"] = ManifestRelativePath,
            ["status"] = manifest["runStatus"]?.GetValue<string>() ?? "unknown",
        },
        new JsonObject
        {
            ["path"] = SummaryJsonRelativePath,
            ["status"] = manifest["runStatus"]?.GetValue<string>() ?? "unknown",
        },
        new JsonObject
        {
            ["path"] = PublicReviewerHandoffRelativePath,
            ["status"] = manifest["runStatus"]?.GetValue<string>() ?? "unknown",
        },
        new JsonObject
        {
            ["path"] = "validation/no-secret-scan-result.json",
            ["status"] = manifest["publicSafetyStatus"]?.GetValue<string>() ?? "unknown",
        },
    ];

    private static bool IsAcceptedAudit95Replay(
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings,
        string publicSafetyStatus,
        string runStatus) =>
        string.Equals(runStatus, "accepted", StringComparison.Ordinal) &&
        string.Equals(publicSafetyStatus, "pass", StringComparison.Ordinal) &&
        results.Count > 0 &&
        results.All(x => x.MismatchReasons.Count == 0) &&
        publicScanFindings.All(x => x.ExpectedTamperFixture);

    private static bool IsAudit95Replay(VerifierCorpusCiReplayOptions options) =>
        string.Equals(options.CorpusVersion, "v0.3.0", StringComparison.OrdinalIgnoreCase);

    private static JsonArray BuildDownstreamOwners() =>
    [
        new JsonObject
        {
            ["featureId"] = "FEAT-159",
            ["responsibility"] = "Cross-device receipt and inclusion verification channels beyond package-bound file replay.",
            ["residualRisk"] = "Physical QR, camera, compact code, browser, and mobile channel evidence remains outside this corpus.",
        },
        new JsonObject
        {
            ["featureId"] = "FEAT-160",
            ["responsibility"] = "Repeated deterministic publication and counting replay across multiple election profiles.",
            ["residualRisk"] = "This corpus provides sample package expectations, not repeated runtime publication/counting ceremony evidence.",
        },
        new JsonObject
        {
            ["featureId"] = "FEAT-163",
            ["responsibility"] = "Second operational run and repeated trusted deployment confidence evidence.",
            ["residualRisk"] = "This corpus is synthetic replay evidence and does not prove operating history.",
        },
        new JsonObject
        {
            ["featureId"] = "FEAT-166",
            ["responsibility"] = "Governance/customer handoff packaging for external review readiness.",
            ["residualRisk"] = "This handoff is public verifier evidence only; customer governance package ownership remains downstream.",
        },
    ];

    private static string BuildSummaryMarkdown(
        string runStatus,
        string publicSafetyStatus,
        IReadOnlyList<VerifierCorpusCiFixtureReplayResult> results,
        IReadOnlyList<VerifierCorpusScanFinding> publicScanFindings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Verifier Corpus CI Replay Summary");
        builder.AppendLine();
        builder.AppendLine($"- Run status: `{runStatus}`");
        builder.AppendLine($"- Public safety status: `{publicSafetyStatus}`");
        builder.AppendLine($"- Fixtures matched: `{results.Count(x => x.MismatchReasons.Count == 0)} / {results.Count}`");
        builder.AppendLine($"- Output mismatches: `{results.Count(x => x.MismatchReasons.Count > 0)}`");
        builder.AppendLine($"- Unexpected public findings: `{publicScanFindings.Count(x => !x.ExpectedTamperFixture)}`");
        builder.AppendLine();
        builder.AppendLine("| Fixture | Status | Expected | Observed | Output |");
        builder.AppendLine("|---|---:|---|---|---|");
        foreach (var result in results.OrderBy(x => x.FixtureId, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"| `{result.FixtureId}` | `{result.Status}` | `{result.ExpectedPrimaryResultCode}` | `{result.ObservedPrimaryResultCode}` | `{result.OutputRef}` |");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ResolveMismatchReasons(
        JsonObject expectedResult,
        HushVotingPackageVerificationResult verification,
        int expectedExitCode,
        string expectedPrimaryResultCode,
        string observedPrimaryResultCode,
        string expectedNormalizedOutputHash,
        string observedNormalizedOutputHash)
    {
        var reasons = new List<string>();
        if (verification.ExitCode != expectedExitCode)
        {
            reasons.Add($"exit-code expected {expectedExitCode} observed {verification.ExitCode}");
        }

        if (!string.Equals(observedPrimaryResultCode, expectedPrimaryResultCode, StringComparison.Ordinal))
        {
            reasons.Add($"primary-result expected {expectedPrimaryResultCode} observed {observedPrimaryResultCode}");
        }

        if (!string.Equals(observedNormalizedOutputHash, expectedNormalizedOutputHash, StringComparison.Ordinal))
        {
            reasons.Add($"normalized-output-hash expected {expectedNormalizedOutputHash} observed {observedNormalizedOutputHash}");
        }

        if (expectedResult["requiredResultCodes"] is JsonArray requiredCodes)
        {
            foreach (var code in requiredCodes.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!verification.Output.Results.Any(x => string.Equals(x.ResultCode, code, StringComparison.Ordinal)))
                {
                    reasons.Add($"required-result-code missing {code}");
                }
            }
        }

        if (expectedResult["requiredCheckStatuses"] is JsonObject requiredStatuses)
        {
            foreach (var (checkCode, statusNode) in requiredStatuses)
            {
                var expectedStatus = statusNode?.GetValue<string>();
                var observed = verification.Output.Results.FirstOrDefault(x =>
                    string.Equals(x.CheckCode, checkCode, StringComparison.Ordinal) &&
                    string.Equals(x.ResultCode, expectedPrimaryResultCode, StringComparison.Ordinal));
                var observedStatus = observed is null
                    ? "missing"
                    : VerifierCorpusGenerator.ToJsonEnumString(observed.Status);
                if (!string.Equals(observedStatus, expectedStatus, StringComparison.Ordinal))
                {
                    reasons.Add($"check-status {checkCode} expected {expectedStatus} observed {observedStatus}");
                }
            }
        }

        return reasons;
    }

    private static string ResolveObservedPrimaryResultCode(
        VerifierOutputRecord output,
        string expectedPrimaryResultCode) =>
        output.Results.FirstOrDefault(x => string.Equals(x.ResultCode, expectedPrimaryResultCode, StringComparison.Ordinal))?.ResultCode ??
        output.Results.FirstOrDefault(x => x.Status == VerificationCheckStatus.Fail)?.ResultCode ??
        output.Results.FirstOrDefault(x => x.Status == VerificationCheckStatus.Warn)?.ResultCode ??
        output.Results.FirstOrDefault()?.ResultCode ??
        "none";

    private static string ResolvePackagePath(string corpusRoot, string packagePath)
    {
        if (Uri.TryCreate(packagePath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return packagePath;
        }

        return Path.IsPathRooted(packagePath)
            ? packagePath
            : Path.Combine(corpusRoot, ToLocalPath(packagePath));
    }

    private static JsonObject ReadJsonObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
        throw new InvalidOperationException($"Expected JSON object: {path}");

    private static string RequiredString(JsonObject obj, string propertyName, string label) =>
        GetStringOrDefault(obj, propertyName) ??
        throw new InvalidOperationException($"{label}.{propertyName} is required.");

    private static int RequiredInt(JsonObject obj, string propertyName, string label) =>
        obj[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : throw new InvalidOperationException($"{label}.{propertyName} is required.");

    private static string? GetStringOrDefault(JsonObject obj, string propertyName) =>
        obj[propertyName] is JsonValue value && value.TryGetValue<string>(out var result) && !string.IsNullOrWhiteSpace(result)
            ? result
            : null;

    private static string ToLocalPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static async Task WriteJsonAsync(
        string root,
        string relativePath,
        JsonObject value,
        CancellationToken cancellationToken)
    {
        await WriteTextLfAsync(
            Path.Combine(root, ToLocalPath(relativePath)),
            VerifierCorpusGenerator.CanonicalJson(value),
            cancellationToken);
    }

    private static async Task WriteTextLfAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static string Sha256File(string path) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}";
}

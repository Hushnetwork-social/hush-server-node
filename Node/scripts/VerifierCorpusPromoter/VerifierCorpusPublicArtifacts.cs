using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HushShared.Elections.Verification.Model;

namespace VerifierCorpusPromoter;

public sealed record VerifierCorpusPublicArtifactResult(
    string ManifestHash,
    string FixtureIndexHash,
    string NoSecretScanStatus,
    IReadOnlyList<VerifierCorpusScanFinding> ScanFindings);

public sealed partial class VerifierCorpusGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string Category, string Fragment)[] ForbiddenFragments =
    [
        ("private_key", "BEGIN PRIVATE KEY"),
        ("private_key", "raw trustee share"),
        ("private_key", "decrypt authority"),
        ("cloud_secret", "aws_secret_access_key"),
        ("cloud_secret", "aws_access_key_id"),
        ("cloud_secret", "client_secret"),
        ("cloud_secret", "password="),
        ("cloud_secret", "token="),
        ("provider_kms_identifier", "arn:aws:kms"),
        ("provider_kms_identifier", "kmsKeyId"),
        ("provider_kms_identifier", "kms_key_id"),
        ("provider_kms_identifier", "alias/"),
        ("voter_private_data", "voterEmail"),
        ("voter_private_data", "voteChoice"),
        ("voter_private_data", "receipt secret"),
        ("voter_private_data", "named voter identity joined to ballot"),
        ("restricted_operational_data", "raw log"),
        ("restricted_operational_data", "operator contact"),
        ("restricted_operational_data", "device id"),
        ("restricted_operational_data", "support case joined to voter"),
        ("unsupported_claim", "certified voting system"),
        ("unsupported_claim", "legal approval granted"),
        ("unsupported_claim", "public-election approved"),
        ("unsupported_claim", "real customer election proof"),
    ];

    private static readonly (string Category, Regex Pattern)[] ForbiddenPatterns =
    [
        ("provider_kms_identifier", new Regex(@"\barn:aws:kms:[a-z0-9-]+:[0-9]{12}:key/[A-Za-z0-9/_+=,.@-]+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)),
        ("ip_address", new Regex(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
    ];

    private static async Task<VerifierCorpusPublicArtifactResult> RenderPublicArtifactsAsync(
        VerifierCorpusGenerationOptions options,
        IReadOnlyList<VerifierCorpusFixtureGenerationResult> fixtures,
        CancellationToken cancellationToken)
    {
        var fixtureIndex = BuildFixtureIndex(fixtures);
        var fixtureIndexHash = await WriteJsonAsync(
            options.OutputRoot,
            "fixtures/fixture-index.json",
            fixtureIndex,
            cancellationToken);

        foreach (var fixture in fixtures)
        {
            await WriteJsonAsync(
                options.OutputRoot,
                $"fixtures/{fixture.FixtureId}/fixture-manifest.json",
                BuildFixtureManifest(fixture),
                cancellationToken);
        }

        await WriteJsonAsync(
            options.OutputRoot,
            "validation/clean-machine-validation-summary.json",
            BuildCleanMachineValidationSummary(options, fixtures),
            cancellationToken);

        await WriteJsonAsync(
            options.OutputRoot,
            "validation/no-secret-scan-result.json",
            BuildNoSecretScanResult(options, [], "pending"),
            cancellationToken);

        var provisionalManifest = BuildCorpusManifest(
            options,
            fixtures,
            fixtureIndexHash,
            noSecretScanStatus: "pending",
            unexpectedFindingCount: 0,
            expectedTamperFindingCount: 0);
        await WriteJsonAsync(options.OutputRoot, "corpus-manifest.json", provisionalManifest, cancellationToken);

        var provisionalManifestHash = Sha256Text(CanonicalJson(provisionalManifest));
        await WriteJsonAsync(
            options.OutputRoot,
            "readiness/verifier-corpus-readiness-fragment.json",
            BuildReadinessFragment(options, fixtures, provisionalManifestHash, fixtureIndexHash, "pending", []),
            cancellationToken);
        await WriteJsonAsync(
            options.OutputRoot,
            "handoff/verifier-corpus-downstream-handoff.json",
            BuildDownstreamHandoff(options, fixtures, provisionalManifestHash, fixtureIndexHash, "pending"),
            cancellationToken);
        await WriteTextAsync(options.OutputRoot, "README.md", BuildReadme(options, provisionalManifestHash), cancellationToken);

        var scanFindings = ScanPublicOutput(options.OutputRoot);
        var unexpectedFindingCount = scanFindings.Count(x => !x.ExpectedTamperFixture);
        var expectedTamperFindingCount = scanFindings.Count(x => x.ExpectedTamperFixture);
        var scanStatus = unexpectedFindingCount == 0 ? "pass" : "blocked";

        await WriteJsonAsync(
            options.OutputRoot,
            "validation/no-secret-scan-result.json",
            BuildNoSecretScanResult(options, scanFindings, scanStatus),
            cancellationToken);

        var finalManifest = BuildCorpusManifest(
            options,
            fixtures,
            fixtureIndexHash,
            scanStatus,
            unexpectedFindingCount,
            expectedTamperFindingCount);
        var manifestHash = await WriteJsonAsync(options.OutputRoot, "corpus-manifest.json", finalManifest, cancellationToken);

        await WriteJsonAsync(
            options.OutputRoot,
            "readiness/verifier-corpus-readiness-fragment.json",
            BuildReadinessFragment(options, fixtures, manifestHash, fixtureIndexHash, scanStatus, scanFindings),
            cancellationToken);
        await WriteJsonAsync(
            options.OutputRoot,
            "handoff/verifier-corpus-downstream-handoff.json",
            BuildDownstreamHandoff(options, fixtures, manifestHash, fixtureIndexHash, scanStatus),
            cancellationToken);
        await WriteTextAsync(options.OutputRoot, "README.md", BuildReadme(options, manifestHash), cancellationToken);

        scanFindings = ScanPublicOutput(options.OutputRoot);
        unexpectedFindingCount = scanFindings.Count(x => !x.ExpectedTamperFixture);
        expectedTamperFindingCount = scanFindings.Count(x => x.ExpectedTamperFixture);
        scanStatus = unexpectedFindingCount == 0 ? "pass" : "blocked";
        await WriteJsonAsync(
            options.OutputRoot,
            "validation/no-secret-scan-result.json",
            BuildNoSecretScanResult(options, scanFindings, scanStatus),
            cancellationToken);

        return new VerifierCorpusPublicArtifactResult(
            manifestHash,
            fixtureIndexHash,
            scanStatus,
            scanFindings);
    }

    public static IReadOnlyList<VerifierCorpusScanFinding> ScanPublicOutput(string outputRoot)
    {
        var fullRoot = Path.GetFullPath(outputRoot);
        if (!Directory.Exists(fullRoot))
        {
            return [];
        }

        var findings = new List<VerifierCorpusScanFinding>();
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(fullRoot, path), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
            if (relativePath.StartsWith(".git/", StringComparison.Ordinal) ||
                string.Equals(relativePath, "validation/no-secret-scan-result.json", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var (category, fragment) in ForbiddenFragments)
            {
                if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new VerifierCorpusScanFinding(
                        relativePath,
                        category,
                        fragment,
                        IsExpectedTamperFinding(relativePath)));
                }
            }

            foreach (var (category, pattern) in ForbiddenPatterns)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    findings.Add(new VerifierCorpusScanFinding(
                        relativePath,
                        category,
                        match.Value,
                        IsExpectedTamperFinding(relativePath)));
                }
            }
        }

        return findings;
    }

    public static IReadOnlyList<VerifierCorpusScanFinding> ScanTextForForbiddenPublicMaterial(
        string relativePath,
        string text)
    {
        var findings = new List<VerifierCorpusScanFinding>();
        foreach (var (category, fragment) in ForbiddenFragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new VerifierCorpusScanFinding(relativePath, category, fragment, false));
            }
        }

        foreach (var (category, pattern) in ForbiddenPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                findings.Add(new VerifierCorpusScanFinding(relativePath, category, match.Value, false));
            }
        }

        return findings;
    }

    private static JsonObject BuildFixtureIndex(IReadOnlyList<VerifierCorpusFixtureGenerationResult> fixtures)
    {
        var fixtureArray = new JsonArray();
        foreach (var fixture in fixtures.OrderBy(x => x.FixtureId, StringComparer.Ordinal))
        {
            fixtureArray.Add(new JsonObject
            {
                ["fixtureId"] = fixture.FixtureId,
                ["fixtureFamily"] = ResolveFixtureFamily(fixture.FixtureId),
                ["profileId"] = fixture.VerifierProfileId,
                ["packagePath"] = PublicPackagePath(fixture),
                ["packageHash"] = fixture.PackageHash,
                ["fixtureManifestRef"] = $"fixtures/{fixture.FixtureId}/fixture-manifest.json",
                ["expectedResultRef"] = $"expected-results/{fixture.FixtureId}.json",
                ["expectedPrimaryResultCode"] = fixture.ExpectedPrimaryResultCode,
                ["expectedOverallStatus"] = ToJsonEnumString(fixture.ExpectedOverallStatus),
                ["expectedExitCode"] = fixture.ExpectedExitCode,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-fixture-index.v1",
            ["fixtures"] = fixtureArray,
        };
    }

    private static JsonObject BuildFixtureManifest(VerifierCorpusFixtureGenerationResult fixture) =>
        new()
        {
            ["schemaVersion"] = "verifier-corpus-fixture-manifest.v1",
            ["fixtureId"] = fixture.FixtureId,
            ["fixtureFamily"] = ResolveFixtureFamily(fixture.FixtureId),
            ["status"] = "accepted",
            ["visibility"] = "public",
            ["profileId"] = fixture.VerifierProfileId,
            ["packagePath"] = PublicPackagePath(fixture),
            ["packageHash"] = fixture.PackageHash,
            ["mutation"] = new JsonObject
            {
                ["mutationType"] = fixture.FixtureId == GoodSampleFixtureId ? "none" : "synthetic_tamper",
                ["description"] = MutationDescription(fixture.FixtureId),
            },
            ["changedArtifact"] = ChangedArtifact(fixture.FixtureId),
            ["expectedPrimaryResultCode"] = fixture.ExpectedPrimaryResultCode,
            ["expectedCheckStatus"] = ToJsonEnumString(fixture.ExpectedCheckStatus),
            ["expectedOverallStatus"] = ToJsonEnumString(fixture.ExpectedOverallStatus),
            ["expectedExitCode"] = fixture.ExpectedExitCode,
            ["expectedOutputRef"] = $"expected-results/{fixture.FixtureId}.json",
            ["proofStatement"] = fixture.FixtureId == GoodSampleFixtureId
                ? "Synthetic finalized public package passes the public anonymous verifier."
                : "Synthetic tamper package must expose the documented primary verifier failure.",
            ["secondaryFailuresAllowed"] = fixture.SecondaryFailuresAllowed,
            ["verifierInvocation"] = new JsonObject
            {
                ["profileId"] = fixture.VerifierProfileId,
                ["package"] = PublicPackagePath(fixture),
                ["output"] = $"validation/verifier-output/{fixture.FixtureId}",
            },
            ["forbiddenMaterialScan"] = new JsonObject
            {
                ["expectedTamperFinding"] = IsExpectedTamperFinding($"packages/{fixture.FixtureId}/"),
                ["unexpectedFindingPolicy"] = "block",
            },
        };

    private static JsonObject BuildCleanMachineValidationSummary(
        VerifierCorpusGenerationOptions options,
        IReadOnlyList<VerifierCorpusFixtureGenerationResult> fixtures)
    {
        var outputs = new JsonArray();
        foreach (var fixture in fixtures.OrderBy(x => x.FixtureId, StringComparer.Ordinal))
        {
            outputs.Add(new JsonObject
            {
                ["fixtureId"] = fixture.FixtureId,
                ["status"] = ToJsonEnumString(fixture.ExpectedOverallStatus),
                ["exitCode"] = fixture.ExpectedExitCode,
                ["normalizedOutputHash"] = fixture.NormalizedOutputHash,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-clean-machine-validation-summary.v1",
            ["generatedAt"] = options.GeneratedAt.UtcDateTime.ToString("O"),
            ["status"] = fixtures.All(x => x.ExpectedExitCode == VerificationExitCodes.Pass || x.ExpectedExitCode == VerificationExitCodes.Fail || x.ExpectedExitCode == VerificationExitCodes.UnreadableOrUnparseable)
                ? "accepted"
                : "blocked",
            ["windows"] = new JsonObject
            {
                ["status"] = options.WindowsReviewerReplayValidated
                    ? "pass"
                    : "command_documented_not_run_in_current_environment",
                ["runtime"] = ".NET 9",
                ["commandShape"] = "PowerShell dotnet run command documented in README.md",
                ["validated"] = options.WindowsReviewerReplayValidated,
                ["claimImpact"] = options.WindowsReviewerReplayValidated
                    ? "Windows PowerShell replay has been validated."
                    : "Windows replay must be run before claiming Windows reviewer validation.",
            },
            ["linux"] = new JsonObject
            {
                ["status"] = options.LinuxReviewerReplayValidated
                    ? "pass"
                    : "command_documented_not_run_in_current_environment",
                ["runtime"] = ".NET 9",
                ["commandShape"] = "Bash dotnet run command documented in README.md",
                ["validated"] = options.LinuxReviewerReplayValidated,
                ["claimImpact"] = options.LinuxReviewerReplayValidated
                    ? "Linux Bash replay has been validated."
                    : "Linux replay must be run before claiming Linux reviewer validation.",
            },
            ["verifierOutputs"] = outputs,
        };
    }

    private static JsonObject BuildNoSecretScanResult(
        VerifierCorpusGenerationOptions options,
        IReadOnlyList<VerifierCorpusScanFinding> findings,
        string status)
    {
        var findingArray = new JsonArray();
        foreach (var finding in findings.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ThenBy(x => x.Category, StringComparer.Ordinal))
        {
            findingArray.Add(new JsonObject
            {
                ["relativePath"] = finding.RelativePath,
                ["category"] = finding.Category,
                ["evidenceHash"] = Sha256Text(finding.Evidence),
                ["expectedTamperFixture"] = finding.ExpectedTamperFixture,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-no-secret-scan-result.v1",
            ["generatedAt"] = options.GeneratedAt.UtcDateTime.ToString("O"),
            ["status"] = status,
            ["unexpectedFindingCount"] = findings.Count(x => !x.ExpectedTamperFixture),
            ["expectedTamperFindingCount"] = findings.Count(x => x.ExpectedTamperFixture),
            ["findingCount"] = findings.Count,
            ["scanBoundary"] = "All generated public corpus files except this scan report and .git metadata.",
            ["expectedTamperPolicy"] = "SP-10 tamper fixtures may contain synthetic forbidden markers only to prove the verifier fails closed.",
            ["findings"] = findingArray,
        };
    }

    private static JsonObject BuildCorpusManifest(
        VerifierCorpusGenerationOptions options,
        IReadOnlyList<VerifierCorpusFixtureGenerationResult> fixtures,
        string fixtureIndexHash,
        string noSecretScanStatus,
        int unexpectedFindingCount,
        int expectedTamperFindingCount)
    {
        var good = fixtures.Single(x => string.Equals(x.FixtureId, GoodSampleFixtureId, StringComparison.Ordinal));
        var status = good.ExpectedOverallStatus == VerificationOverallStatus.Pass &&
            good.ExpectedExitCode == VerificationExitCodes.Pass &&
            unexpectedFindingCount == 0
                ? "accepted"
                : "blocked";

        var fixtureArray = new JsonArray();
        foreach (var fixture in fixtures.OrderBy(x => x.FixtureId, StringComparer.Ordinal))
        {
            fixtureArray.Add(new JsonObject
            {
                ["fixtureId"] = fixture.FixtureId,
                ["fixtureFamily"] = ResolveFixtureFamily(fixture.FixtureId),
                ["packagePath"] = PublicPackagePath(fixture),
                ["packageHash"] = fixture.PackageHash,
                ["expectedResultRef"] = $"expected-results/{fixture.FixtureId}.json",
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-manifest.v1",
            ["corpusId"] = "hushvoting-public-verifier-corpus",
            ["corpusFamily"] = options.CorpusFamily,
            ["corpusVersion"] = options.CorpusVersion,
            ["repositoryRelativePath"] = NormalizeRepositoryRelativePath(options.RepositoryRelativePath),
            ["status"] = status,
            ["visibility"] = "public",
            ["publicRepository"] = options.PublicRepository,
            ["publicRepositoryRef"] = options.PublicRepositoryRef,
            ["protocolPackage"] = new JsonObject
            {
                ["packageId"] = "omega-hushvoting-v1",
                ["packageVersion"] = "v1.2.0",
                ["source"] = "https://github.com/Hushnetwork-social/protocol-omega-packages",
                ["profileId"] = VerificationProfileIds.PublicAnonymousV1,
            },
            ["verifier"] = new JsonObject
            {
                ["repository"] = options.VerifierRepository,
                ["sourceRef"] = options.VerifierSourceRef,
                ["projectPath"] = options.VerifierProjectPath,
                ["runtime"] = ".NET 9",
                ["profileId"] = VerificationProfileIds.PublicAnonymousV1,
                ["binaryRelease"] = options.VerifierHash,
            },
            ["generator"] = new JsonObject
            {
                ["name"] = "VerifierCorpusPromoter",
                ["sourceRef"] = options.VerifierSourceRef,
                ["canonicalizationVersion"] = VerifierCorpusContracts.CanonicalizationVersion,
            },
            ["goodSample"] = new JsonObject
            {
                ["fixtureId"] = good.FixtureId,
                ["packagePath"] = PublicPackagePath(good),
                ["packageHash"] = good.PackageHash,
                ["expectedResultRef"] = $"expected-results/{good.FixtureId}.json",
                ["expectedOverallStatus"] = ToJsonEnumString(good.ExpectedOverallStatus),
                ["expectedExitCode"] = good.ExpectedExitCode,
            },
            ["fixtureIndex"] = new JsonObject
            {
                ["path"] = "fixtures/fixture-index.json",
                ["sha256Hash"] = fixtureIndexHash,
                ["fixtures"] = fixtureArray,
            },
            ["validationSummary"] = new JsonObject
            {
                ["path"] = "validation/clean-machine-validation-summary.json",
                ["status"] = BuildPlatformValidationStatus(options),
            },
            ["noSecretScan"] = new JsonObject
            {
                ["path"] = "validation/no-secret-scan-result.json",
                ["status"] = noSecretScanStatus,
                ["unexpectedFindingCount"] = unexpectedFindingCount,
                ["expectedTamperFindingCount"] = expectedTamperFindingCount,
            },
            ["readinessFragment"] = new JsonObject
            {
                ["path"] = "readiness/verifier-corpus-readiness-fragment.json",
            },
            ["downstreamHandoff"] = new JsonObject
            {
                ["path"] = "handoff/verifier-corpus-downstream-handoff.json",
            },
            ["generatedAt"] = options.GeneratedAt.UtcDateTime.ToString("O"),
            ["supersessionRules"] = BuildSupersessionRules(),
            ["publicBoundaryStatement"] = "Synthetic public corpus only. No private backend, database, cloud account, real voter data, real customer election data, or authority approval claim is required or included.",
        };
    }

    private static JsonObject BuildReadinessFragment(
        VerifierCorpusGenerationOptions options,
        IReadOnlyList<VerifierCorpusFixtureGenerationResult> fixtures,
        string manifestHash,
        string fixtureIndexHash,
        string noSecretScanStatus,
        IReadOnlyList<VerifierCorpusScanFinding> scanFindings)
    {
        var good = fixtures.Single(x => string.Equals(x.FixtureId, GoodSampleFixtureId, StringComparison.Ordinal));
        var status = good.ExpectedOverallStatus == VerificationOverallStatus.Pass &&
            good.ExpectedExitCode == VerificationExitCodes.Pass &&
            scanFindings.All(x => x.ExpectedTamperFixture)
                ? "accepted"
                : "blocked";

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-readiness-fragment.v1",
            ["fragmentId"] = $"AT-RDY-007-{options.CorpusVersion}",
            ["featureSlice"] = "public-verifier-sample-and-tamper-corpus",
            ["sourceGap"] = "Verifier/sample/tamper corpus",
            ["acceptanceGate"] = VerifierCorpusContracts.AcceptanceGate,
            ["dimensionId"] = "RDY-DIM-002",
            ["evidenceRefs"] = new JsonArray
            {
                new JsonObject
                {
                    ["path"] = "corpus-manifest.json",
                    ["sha256Hash"] = manifestHash,
                },
                new JsonObject
                {
                    ["path"] = "fixtures/fixture-index.json",
                    ["sha256Hash"] = fixtureIndexHash,
                },
                new JsonObject
                {
                    ["path"] = $"packages/{GoodSampleFixtureId}",
                    ["sha256Hash"] = good.PackageHash,
                },
                new JsonObject
                {
                    ["path"] = "validation/no-secret-scan-result.json",
                    ["status"] = noSecretScanStatus,
                },
            },
            ["checkResults"] = new JsonArray
            {
                new JsonObject
                {
                    ["checkId"] = "good-sample-pass",
                    ["status"] = ToJsonEnumString(good.ExpectedOverallStatus),
                    ["exitCode"] = good.ExpectedExitCode,
                },
                new JsonObject
                {
                    ["checkId"] = "tamper-fixtures-present",
                    ["status"] = fixtures.Count == FixtureSpecs.Length ? "pass" : "fail",
                    ["count"] = fixtures.Count,
                },
                new JsonObject
                {
                    ["checkId"] = "unexpected-public-material",
                    ["status"] = scanFindings.Any(x => !x.ExpectedTamperFixture) ? "fail" : "pass",
                    ["unexpectedFindingCount"] = scanFindings.Count(x => !x.ExpectedTamperFixture),
                },
            },
            ["status"] = status,
            ["visibility"] = "public",
            ["claimEffect"] = "Candidate evidence for the readiness register only; register promotion remains owned by FEAT-130.",
            ["residualRisk"] = "Synthetic corpus does not prove operating history, customer election delivery, cross-device receipt import, or external review.",
            ["doesNotMutateRegister"] = true,
            ["promotionInstructions"] = "FEAT-130 may ingest this fragment after maintainer review of the public corpus output and hashes.",
            ["supersessionRules"] = BuildSupersessionRules(),
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        VerifierCorpusGenerationOptions options,
        IReadOnlyList<VerifierCorpusFixtureGenerationResult> fixtures,
        string manifestHash,
        string fixtureIndexHash,
        string noSecretScanStatus)
    {
        var good = fixtures.Single(x => string.Equals(x.FixtureId, GoodSampleFixtureId, StringComparison.Ordinal));

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-downstream-handoff.v1",
            ["handoffId"] = $"FEAT-135-{options.CorpusVersion}-handoff",
            ["producerFeature"] = "FEAT-135",
            ["corpusVersion"] = options.CorpusVersion,
            ["publicRepositoryRef"] = options.PublicRepositoryRef,
            ["manifestHash"] = manifestHash,
            ["goodPackageHash"] = good.PackageHash,
            ["fixtureIndexHash"] = fixtureIndexHash,
            ["verifierSourceRef"] = options.VerifierSourceRef,
            ["verifierHash"] = options.VerifierHash,
            ["cleanMachineValidationSummary"] = new JsonObject
            {
                ["path"] = "validation/clean-machine-validation-summary.json",
                ["status"] = BuildPlatformValidationStatus(options),
            },
            ["noSecretScanResult"] = new JsonObject
            {
                ["path"] = "validation/no-secret-scan-result.json",
                ["status"] = noSecretScanStatus,
            },
            ["feat136ReuseNotes"] = new JsonObject
            {
                ["receiptImportImplementedHere"] = false,
                ["goodSamplePackageRef"] = $"packages/{GoodSampleFixtureId}",
                ["reuseInstruction"] = "FEAT-136 may reuse the good sample package shape and extend it with cross-device receipt inclusion fixtures.",
            },
            ["feat141ConsumerInstructions"] = new JsonObject
            {
                ["requiredRefs"] = new JsonArray
                {
                    "corpus-manifest.json",
                    "fixtures/fixture-index.json",
                    $"packages/{GoodSampleFixtureId}",
                    "validation/no-secret-scan-result.json",
                    "readiness/verifier-corpus-readiness-fragment.json",
                },
                ["observedDeliveryBoundary"] = "FEAT-141 must still collect real rehearsal or pilot evidence; this corpus is synthetic verifier replay evidence.",
            },
            ["residualRisk"] = "Does not replace pilot ceremony evidence, real deployment observation, external validation, or legal/regulatory approval.",
        };
    }

    private static string BuildPlatformValidationStatus(VerifierCorpusGenerationOptions options)
    {
        if (options.WindowsReviewerReplayValidated && options.LinuxReviewerReplayValidated)
        {
            return "windows_linux_replay_validated";
        }

        if (options.WindowsReviewerReplayValidated)
        {
            return "windows_replay_validated_linux_command_documented";
        }

        if (options.LinuxReviewerReplayValidated)
        {
            return "linux_replay_validated_windows_command_documented";
        }

        return "commands_documented_platform_replay_pending";
    }

    private static string BuildReadme(VerifierCorpusGenerationOptions options, string manifestHash) =>
        $$"""
        # HushVoting Verifier Corpus

        This repository contains synthetic public packages for replaying the HushVoting verifier. It is designed for reviewers who want to run a passing public anonymous package and a set of tamper packages without access to private infrastructure.

        Requirements:

        - .NET 9 SDK
        - A local checkout of `{{options.VerifierRepository}}`
        - This corpus checkout

        PowerShell good-sample run:

        ```powershell
        dotnet run --project {{BuildVerifierProjectReference(options, windows: true)}} -- --package .\packages\{{GoodSampleFixtureId}} --profile {{VerificationProfileIds.PublicAnonymousV1}} --output .\validation\local-run\{{GoodSampleFixtureId}}
        ```

        Bash good-sample run:

        ```bash
        dotnet run --project {{BuildVerifierProjectReference(options, windows: false)}} -- --package ./packages/{{GoodSampleFixtureId}} --profile {{VerificationProfileIds.PublicAnonymousV1}} --output ./validation/local-run/{{GoodSampleFixtureId}}
        ```

        Tamper packages are listed in `fixtures/fixture-index.json`. Each fixture has an expected result file in `expected-results/`.

        Public boundary:

        - Packages are synthetic samples.
        - No private backend, database, cloud account, private repository, or network service is required to verify local packages.
        - No real voter data, customer election data, trustee share material, receipt private material, cloud credential, or operational log dump is intentionally included.
        - This corpus does not make legal, authority, public-election, or certification claims.

        Manifest hash: `{{manifestHash}}`
        Corpus version: `{{options.CorpusVersion}}`
        Generated at: `{{options.GeneratedAt.UtcDateTime:O}}`
        """;

    private static string BuildVerifierProjectReference(VerifierCorpusGenerationOptions options, bool windows)
    {
        var separator = windows ? "\\" : "/";
        var segments = NormalizeRepositoryRelativePath(options.RepositoryRelativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentLevels = segments.Length + 1;
        var siblingPrefix = string.Join(separator, Enumerable.Repeat("..", parentLevels)) +
            separator +
            "hush-server-node";
        var projectPath = windows
            ? options.VerifierProjectPath.Replace('/', '\\')
            : options.VerifierProjectPath.Replace('\\', '/');
        return siblingPrefix + separator + projectPath;
    }

    private static string NormalizeRepositoryRelativePath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim('/');

    private static JsonArray BuildSupersessionRules() =>
    [
        "verifier source ref changes",
        "Protocol Omega package version changes",
        "verification package schema changes",
        "verifier result-code contract changes",
        "SP-07 publication proof contract changes",
        "SP-08 release integrity contract changes",
        "SP-10 operational evidence contract changes",
        "corpus schema changes",
    ];

    private static string ResolveFixtureFamily(string fixtureId)
    {
        if (string.Equals(fixtureId, GoodSampleFixtureId, StringComparison.Ordinal))
        {
            return "good_sample";
        }

        if (fixtureId is "tamper-missing-artifact" or "tamper-artifact-hash" or "tamper-malformed-package-json")
        {
            return "package_manifest";
        }

        if (fixtureId is "tamper-profile-mismatch" or "tamper-unsupported-live-dependency")
        {
            return "profile_input";
        }

        if (fixtureId is "tamper-wrong-election-id")
        {
            return "election_identity";
        }

        if (fixtureId.StartsWith("tamper-sp04-", StringComparison.Ordinal))
        {
            return "sp04_challenge_spoil";
        }

        if (fixtureId.StartsWith("tamper-sp05-", StringComparison.Ordinal))
        {
            return "sp05_public_boundary";
        }

        if (fixtureId.StartsWith("tamper-sp08-", StringComparison.Ordinal))
        {
            return "sp08_release_integrity";
        }

        if (fixtureId.StartsWith("tamper-sp10-", StringComparison.Ordinal))
        {
            return "sp10_operational_boundary";
        }

        if (fixtureId.StartsWith("tamper-published-", StringComparison.Ordinal))
        {
            return "published_ballots";
        }

        return "accepted_ballots";
    }

    private static string ChangedArtifact(string fixtureId) =>
        fixtureId switch
        {
            GoodSampleFixtureId => "none",
            "tamper-missing-artifact" => VerificationPackageFileNames.AcceptedBallotSet,
            "tamper-artifact-hash" => VerificationPackageFileNames.ResultBinding,
            "tamper-malformed-package-json" => VerificationPackageFileNames.ElectionRecord,
            "tamper-profile-mismatch" => VerificationPackageFileNames.VerifierProfile,
            "tamper-unsupported-live-dependency" => "package-path",
            "tamper-wrong-election-id" => VerificationPackageFileNames.AcceptedBallotSet,
            "tamper-duplicate-nullifier" => VerificationPackageFileNames.AcceptedBallotSet,
            "tamper-accepted-set-hash" => VerificationPackageFileNames.AcceptedBallotSet,
            "tamper-published-stream-sequence" => VerificationPackageFileNames.PublishedBallotStream,
            "tamper-published-stream-hash" => VerificationPackageFileNames.PublishedBallotStream,
            "tamper-sp04-receipt-set-hash" => VerificationPackageFileNames.Sp04Evidence,
            "tamper-sp04-count" => VerificationPackageFileNames.Sp04Evidence,
            "tamper-sp04-accepted-binding" => VerificationPackageFileNames.AcceptedBallotSet,
            "tamper-sp05-public-named-field" => VerificationPackageFileNames.Sp05EligibilitySummary,
            "tamper-sp05-count-reconciliation" => VerificationPackageFileNames.Sp05EligibilitySummary,
            "tamper-sp08-release-manifest-hash" => VerificationPackageFileNames.Sp08ReleaseManifest,
            "tamper-sp08-mutable-artifact-reference" => VerificationPackageFileNames.Sp08ReleaseManifest,
            "tamper-sp08-component-hash-mismatch" => VerificationPackageFileNames.Sp08ReleaseManifest,
            "tamper-sp08-protocol-package-mismatch" => VerificationPackageFileNames.Sp08ReleaseIntegrity,
            "tamper-sp08-circuit-key-hash-mismatch" => VerificationPackageFileNames.Sp08ReleaseManifest,
            "tamper-sp10-forbidden-leak" => VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            "tamper-sp10-kms-public-value-leak" => VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            _ => "unknown",
        };

    private static string MutationDescription(string fixtureId) =>
        fixtureId switch
        {
            GoodSampleFixtureId => "No mutation. This is the passing synthetic finalized package.",
            "tamper-missing-artifact" => "Deletes a manifest-listed accepted ballot artifact.",
            "tamper-artifact-hash" => "Changes artifact bytes without updating the package manifest hash.",
            "tamper-malformed-package-json" => "Replaces the election record JSON with malformed content.",
            "tamper-profile-mismatch" => "Changes the package verifier profile away from the requested public profile.",
            "tamper-unsupported-live-dependency" => "Uses a live URL package path, which the verifier must reject in v1.",
            "tamper-wrong-election-id" => "Changes one package artifact to a different election id.",
            "tamper-duplicate-nullifier" => "Duplicates a ballot nullifier in the accepted set.",
            "tamper-accepted-set-hash" => "Changes the accepted ballot inventory hash.",
            "tamper-published-stream-sequence" => "Breaks the published ballot stream sequence.",
            "tamper-published-stream-hash" => "Changes the published ballot stream hash.",
            "tamper-sp04-receipt-set-hash" => "Changes the SP-04 receipt commitment set hash.",
            "tamper-sp04-count" => "Changes the SP-04 accepted-bound receipt count.",
            "tamper-sp04-accepted-binding" => "Breaks accepted ballot to receipt commitment binding.",
            "tamper-sp05-public-named-field" => "Adds a public named-field leak to the eligibility summary.",
            "tamper-sp05-count-reconciliation" => "Breaks SP-05 public count reconciliation.",
            "tamper-sp08-release-manifest-hash" => "Changes release manifest content without preserving integrity binding.",
            "tamper-sp08-mutable-artifact-reference" => "Uses a mutable release artifact reference.",
            "tamper-sp08-component-hash-mismatch" => "Breaks release component hash shape.",
            "tamper-sp08-protocol-package-mismatch" => "Breaks the protocol package binding in release integrity.",
            "tamper-sp08-circuit-key-hash-mismatch" => "Breaks circuit and key digest shape.",
            "tamper-sp10-forbidden-leak" => "Adds synthetic forbidden public operational wording and boundary markers.",
            "tamper-sp10-kms-public-value-leak" => "Adds a synthetic public KMS-style value marker.",
            _ => "Synthetic tamper mutation.",
        };

    private static string PublicPackagePath(VerifierCorpusFixtureGenerationResult fixture) =>
        string.Equals(fixture.FixtureId, "tamper-unsupported-live-dependency", StringComparison.Ordinal)
            ? fixture.PackagePath
            : $"packages/{fixture.FixtureId}";

    private static bool IsExpectedTamperFinding(string relativePath) =>
        relativePath.StartsWith("packages/tamper-sp10-", StringComparison.Ordinal) ||
        relativePath.StartsWith("validation/verifier-output/tamper-sp10-", StringComparison.Ordinal);

    private static async Task<string> WriteJsonAsync(
        string outputRoot,
        string relativePath,
        JsonObject value,
        CancellationToken cancellationToken)
    {
        var content = CanonicalJson(value);
        await WriteTextAsync(outputRoot, relativePath, content, cancellationToken);
        return Sha256Text(content);
    }

    private static async Task WriteTextAsync(
        string outputRoot,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(outputRoot);
        var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsurePathUnder(fullRoot, path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), Utf8NoBom, cancellationToken);
    }
}

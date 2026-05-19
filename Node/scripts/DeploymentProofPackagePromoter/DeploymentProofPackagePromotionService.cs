using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeploymentProofPackagePromoter;

public sealed record DeploymentProofPackagePromotionOptions(
    DeploymentProofPackagePromotionPaths Paths,
    string? Mode,
    string? ComponentId,
    string? DeploymentProofId,
    string? CeremonyId,
    string? ClassificationInput,
    string? CdProvider,
    string? CdRunId,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly,
    bool Scaffold,
    bool CaptureLiveEvidence);

public sealed record DeploymentProofPackagePromotionResult(
    string Mode,
    string PackageId,
    DateTimeOffset GeneratedAt,
    string ManifestHash,
    string ArchiveHash,
    string CatalogPath,
    IReadOnlyList<string> WrittenFiles);

public sealed class DeploymentProofPackagePromotionException(
    string message,
    IReadOnlyList<string> details) : InvalidOperationException(message)
{
    public IReadOnlyList<string> Details { get; } = details;
}

public sealed class DeploymentProofPackagePromotionService
{
    public const string ModeComponentProof = "component_proof";
    public const string ModeBindingLedger = "binding_ledger";
    public const string ModeRehearsalCeremony = "rehearsal_ceremony";
    public const string CatalogFileName = "deployment-proof-catalog.json";

    private static readonly DateTimeOffset FixedZipTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public DeploymentProofPackagePromotionResult Promote(DeploymentProofPackagePromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidatePathConfiguration(options.Paths);

        if (options.CaptureLiveEvidence)
        {
            throw new DeploymentProofPackagePromotionException(
                "Deployment proof package promotion does not capture live evidence in the default maintainer workflow.",
                ["Use committed CD output fixtures or a separately approved live capture process."]);
        }

        if (options.Scaffold)
        {
            ScaffoldOutputRoots(options.Paths);
        }

        var schemaErrors = DeploymentProofPackageContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new DeploymentProofPackagePromotionException("Deployment proof package schema validation failed.", schemaErrors);
        }

        var generatedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(options.Mode))
        {
            if (!options.ValidateOnly)
            {
                throw new DeploymentProofPackagePromotionException(
                    "Promotion mode is required unless --validate-only is used for all fixture validation.",
                    ["Supported modes: component_proof, binding_ledger, rehearsal_ceremony."]);
            }

            ValidateAllFixtureModes(options.Paths);
            return new DeploymentProofPackagePromotionResult(
                "validate_all",
                "validate_all",
                generatedAt,
                string.Empty,
                string.Empty,
                options.Paths.CatalogPath,
                []);
        }

        return options.Mode switch
        {
            ModeComponentProof => PromoteComponentProof(options, generatedAt),
            ModeBindingLedger => PromoteBindingLedger(options, generatedAt),
            ModeRehearsalCeremony => PromoteRehearsalCeremony(options, generatedAt),
            _ => throw new DeploymentProofPackagePromotionException(
                $"Unsupported promotion mode: {options.Mode}",
                ["Supported modes: component_proof, binding_ledger, rehearsal_ceremony."]),
        };
    }

    private static DeploymentProofPackagePromotionResult PromoteComponentProof(
        DeploymentProofPackagePromotionOptions options,
        DateTimeOffset generatedAt)
    {
        var componentProof = FindComponentProof(options.Paths, options.ComponentId, options.DeploymentProofId);
        ApplyCommandOverrides(componentProof, options);
        var errors = DeploymentProofPackageContracts.ValidateComponentProof(componentProof).ToList();
        if (errors.Count > 0)
        {
            throw new DeploymentProofPackagePromotionException("Component deployment proof validation failed.", errors);
        }

        var componentId = GetRequiredString(componentProof, "componentId");
        var proofId = GetRequiredString(componentProof, "deploymentProofId");
        var outputRelativeRoot = NormalizeRelativePath(Path.Combine("packages", componentId, proofId));
        var proofBytes = ToCanonicalJsonBytes(componentProof);
        var summary = DeploymentProofPackageViewRenderer.GetPublicComponentSummary(componentProof);
        ValidatePublicMarkdown("public-safe-deployment-summary.md", summary);

        var filesWithoutManifest = new List<PromotedFile>
        {
            new($"{outputRelativeRoot}/deployment-proof-package.json", proofBytes),
            new($"{outputRelativeRoot}/public-safe-deployment-summary.md", TextBytes(summary)),
        };
        var package = BuildPackage(
            "component_proof",
            proofId,
            generatedAt,
            filesWithoutManifest,
            $"{outputRelativeRoot}/deployment-proof-package.zip",
            $"{outputRelativeRoot}/deployment-proof-manifest.json");

        return FinalizePromotion(
            options,
            package,
            catalog => UpsertCatalogEntry(catalog, "componentProofs", "deploymentProofId", proofId, package.ManifestHash, package.ArchiveHash, new JsonObject
            {
                ["deploymentProofId"] = proofId,
                ["componentId"] = componentId,
                ["status"] = GetRequiredString(componentProof, "status"),
                ["packagePath"] = outputRelativeRoot,
                ["manifestHash"] = package.ManifestHash,
                ["archiveHash"] = package.ArchiveHash,
            }));
    }

    private static DeploymentProofPackagePromotionResult PromoteBindingLedger(
        DeploymentProofPackagePromotionOptions options,
        DateTimeOffset generatedAt)
    {
        var proofSet = ReadExample(options.Paths, "bindings", "deployment-proof-set.json");
        var ledger = ReadExample(options.Paths, "bindings", "per-election-deployment-binding-ledger.json");
        var errors = DeploymentProofPackageContracts.ValidateProofSet(proofSet)
            .Concat(DeploymentProofPackageContracts.ValidateBindingLedger(ledger))
            .ToList();
        if (errors.Count > 0)
        {
            throw new DeploymentProofPackagePromotionException("Binding ledger validation failed.", errors);
        }

        var publicId = GetRequiredString(proofSet, "electionOrRehearsalPublicId");
        var ledgerId = GetRequiredString(ledger, "ledgerId");
        var outputRelativeRoot = NormalizeRelativePath(Path.Combine("election-bindings", publicId, ledgerId));
        var summary = DeploymentProofPackageViewRenderer.GetPublicBindingSummary(proofSet, ledger);
        ValidatePublicMarkdown("public-safe-binding-summary.md", summary);

        var filesWithoutManifest = new List<PromotedFile>
        {
            new($"{outputRelativeRoot}/deployment-proof-set.json", ToCanonicalJsonBytes(proofSet)),
            new($"{outputRelativeRoot}/per-election-deployment-binding-ledger.json", ToCanonicalJsonBytes(ledger)),
            new($"{outputRelativeRoot}/public-safe-binding-summary.md", TextBytes(summary)),
        };
        var package = BuildPackage(
            "binding_ledger",
            ledgerId,
            generatedAt,
            filesWithoutManifest,
            $"{outputRelativeRoot}/per-election-deployment-binding-ledger.zip",
            $"{outputRelativeRoot}/per-election-deployment-binding-ledger-manifest.json");

        return FinalizePromotion(
            options,
            package,
            catalog => UpsertCatalogEntry(catalog, "proofSets", "ledgerId", ledgerId, package.ManifestHash, package.ArchiveHash, new JsonObject
            {
                ["ledgerId"] = ledgerId,
                ["proofSetId"] = GetRequiredString(proofSet, "proofSetId"),
                ["electionOrRehearsalPublicId"] = publicId,
                ["packagePath"] = outputRelativeRoot,
                ["manifestHash"] = package.ManifestHash,
                ["archiveHash"] = package.ArchiveHash,
            }));
    }

    private static DeploymentProofPackagePromotionResult PromoteRehearsalCeremony(
        DeploymentProofPackagePromotionOptions options,
        DateTimeOffset generatedAt)
    {
        var ceremony = ReadExample(options.Paths, "ceremonies", "deployment-ceremony.json");
        var proofSet = ReadExample(options.Paths, "bindings", "deployment-proof-set.json");
        var ledger = ReadExample(options.Paths, "bindings", "per-election-deployment-binding-ledger.json");
        var readinessFragment = ReadExample(options.Paths, "readiness", "readiness-fragment.json");
        var downstreamHandoff = ReadExample(options.Paths, "handoffs", "downstream-handoff.json");
        var webClient = FindComponentProof(options.Paths, "hush-web-client", null);
        var serverNode = FindComponentProof(options.Paths, "hush-server-node", null);

        var errors = DeploymentProofPackageContracts.ValidateComponentProof(webClient)
            .Concat(DeploymentProofPackageContracts.ValidateComponentProof(serverNode))
            .Concat(DeploymentProofPackageContracts.ValidateProofSet(proofSet))
            .Concat(DeploymentProofPackageContracts.ValidateBindingLedger(ledger))
            .Concat(DeploymentProofPackageContracts.ValidateCeremony(ceremony))
            .ToList();
        ValidateReadinessFragment(readinessFragment, errors);
        ValidateDownstreamHandoff(downstreamHandoff, errors);
        if (errors.Count > 0)
        {
            throw new DeploymentProofPackagePromotionException("Rehearsal ceremony validation failed.", errors);
        }

        var ceremonyId = GetRequiredString(ceremony, "ceremonyId");
        var outputRelativeRoot = NormalizeRelativePath(Path.Combine("ceremonies", ceremonyId));
        var publicSummary = DeploymentProofPackageViewRenderer.GetPublicBindingSummary(proofSet, ledger);
        ValidatePublicMarkdown("public-safe-binding-summary.md", publicSummary);
        var restrictedCeremonyIndex = DeploymentProofPackageViewRenderer.GetRestrictedCeremonyIndex(ceremony);
        var restrictedDeploymentIndex = DeploymentProofPackageViewRenderer.GetRestrictedDeploymentEvidenceIndex(ceremony, [webClient, serverNode]);

        var filesWithoutManifest = new List<PromotedFile>
        {
            new($"{outputRelativeRoot}/deployment-ceremony.json", ToCanonicalJsonBytes(ceremony)),
            new($"{outputRelativeRoot}/readiness-fragment.json", ToCanonicalJsonBytes(readinessFragment)),
            new($"{outputRelativeRoot}/downstream-handoff.json", ToCanonicalJsonBytes(downstreamHandoff)),
            new($"{outputRelativeRoot}/public-safe-binding-summary.md", TextBytes(publicSummary)),
        };
        var package = BuildPackage(
            "rehearsal_ceremony",
            ceremonyId,
            generatedAt,
            filesWithoutManifest,
            $"{outputRelativeRoot}/deployment-ceremony.zip",
            $"{outputRelativeRoot}/deployment-ceremony-manifest.json");

        var restrictedRelativeRoot = NormalizeRelativePath(ceremonyId);
        package = package with
        {
            RestrictedFiles =
            [
                new($"{restrictedRelativeRoot}/restricted-ceremony-evidence-index.md", TextBytes(restrictedCeremonyIndex)),
                new($"{restrictedRelativeRoot}/restricted-deployment-evidence-index.md", TextBytes(restrictedDeploymentIndex)),
            ],
        };

        return FinalizePromotion(
            options,
            package,
            catalog => UpsertCatalogEntry(catalog, "ceremonies", "ceremonyId", ceremonyId, package.ManifestHash, package.ArchiveHash, new JsonObject
            {
                ["ceremonyId"] = ceremonyId,
                ["rehearsalElectionId"] = GetRequiredString(ceremony, "rehearsalElectionId"),
                ["packagePath"] = outputRelativeRoot,
                ["manifestHash"] = package.ManifestHash,
                ["archiveHash"] = package.ArchiveHash,
                ["readinessFragmentId"] = GetRequiredString(readinessFragment, "fragmentId"),
                ["downstreamHandoffId"] = GetRequiredString(downstreamHandoff, "handoffId"),
            }));
    }

    private static DeploymentProofPackagePromotionResult FinalizePromotion(
        DeploymentProofPackagePromotionOptions options,
        BuiltPackage package,
        Action<JsonObject> catalogUpdate)
    {
        var catalog = LoadOrCreateCatalog(options.Paths, package.GeneratedAt);
        catalogUpdate(catalog);

        if (options.ValidateOnly)
        {
            return new DeploymentProofPackagePromotionResult(
                package.PackageKind,
                package.PackageId,
                package.GeneratedAt,
                package.ManifestHash,
                package.ArchiveHash,
                options.Paths.CatalogPath,
                []);
        }

        foreach (var file in package.PublicFiles)
        {
            WriteFileInside(options.Paths.PublicOutputRoot, file);
        }

        foreach (var file in package.RestrictedFiles)
        {
            WriteFileInside(options.Paths.RestrictedOutputRoot, file);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.Paths.CatalogPath)!);
        File.WriteAllBytes(options.Paths.CatalogPath, ToCanonicalJsonBytes(catalog));

        return new DeploymentProofPackagePromotionResult(
            package.PackageKind,
            package.PackageId,
            package.GeneratedAt,
            package.ManifestHash,
            package.ArchiveHash,
            options.Paths.CatalogPath,
            package.PublicFiles.Select(file => Path.Combine(options.Paths.PublicOutputRoot, file.RelativePath))
                .Concat(package.RestrictedFiles.Select(file => Path.Combine(options.Paths.RestrictedOutputRoot, file.RelativePath)))
                .Append(options.Paths.CatalogPath)
                .ToArray());
    }

    private static BuiltPackage BuildPackage(
        string packageKind,
        string packageId,
        DateTimeOffset generatedAt,
        IReadOnlyList<PromotedFile> filesWithoutManifest,
        string archiveRelativePath,
        string manifestRelativePath)
    {
        var archiveBytes = BuildDeterministicArchive(filesWithoutManifest);
        var archiveHash = Sha256Hex(archiveBytes);
        var manifest = BuildManifest(packageKind, packageId, generatedAt, filesWithoutManifest, archiveRelativePath, archiveHash);
        var manifestBytes = ToCanonicalJsonBytes(manifest);
        var manifestHash = Sha256Hex(manifestBytes);
        var allFiles = filesWithoutManifest
            .Concat(
            [
                new PromotedFile(manifestRelativePath, manifestBytes),
                new PromotedFile(archiveRelativePath, archiveBytes),
            ])
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new BuiltPackage(packageKind, packageId, generatedAt, manifestHash, archiveHash, allFiles, []);
    }

    private static JsonObject BuildManifest(
        string packageKind,
        string packageId,
        DateTimeOffset generatedAt,
        IReadOnlyList<PromotedFile> filesWithoutManifest,
        string archiveRelativePath,
        string archiveHash) =>
        new()
        {
            ["manifestId"] = $"MANIFEST-{packageId}",
            ["packageKind"] = packageKind,
            ["packageId"] = packageId,
            ["generatedAt"] = generatedAt.UtcDateTime.ToString("O"),
            ["canonicalizationVersion"] = "deployment-proof-canonical-json-v1",
            ["files"] = new JsonArray(filesWithoutManifest
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new JsonObject
                {
                    ["path"] = file.RelativePath,
                    ["sha256Hash"] = Sha256Hex(file.Bytes),
                    ["byteLength"] = file.Bytes.Length,
                })
                .Cast<JsonNode>()
                .ToArray()),
            ["hashes"] = new JsonObject
            {
                ["algorithm"] = "SHA-256",
            },
            ["archive"] = new JsonObject
            {
                ["path"] = archiveRelativePath,
                ["sha256Hash"] = archiveHash,
            },
            ["sourceRefs"] = new JsonArray(),
            ["validationResults"] = new JsonObject
            {
                ["status"] = "passed",
            },
            ["redactionScanResults"] = new JsonObject
            {
                ["publicForbiddenMaterialScan"] = "passed",
            },
        };

    private static JsonObject LoadOrCreateCatalog(DeploymentProofPackagePromotionPaths paths, DateTimeOffset generatedAt)
    {
        if (File.Exists(paths.CatalogPath))
        {
            return DeploymentProofPackageContracts.ReadJsonObject(paths.CatalogPath, CatalogFileName);
        }

        return new JsonObject
        {
            ["catalogVersion"] = "1.0",
            ["generatedAt"] = generatedAt.UtcDateTime.ToString("O"),
            ["publicRepository"] = "https://github.com/Hushnetwork-social/Deployment-Proof-Packages",
            ["componentProofs"] = new JsonArray(),
            ["proofSets"] = new JsonArray(),
            ["ceremonies"] = new JsonArray(),
            ["supersededProofs"] = new JsonArray(),
            ["latestAcceptedByComponentAndTarget"] = new JsonObject(),
        };
    }

    private static void UpsertCatalogEntry(
        JsonObject catalog,
        string arrayName,
        string idName,
        string idValue,
        string manifestHash,
        string archiveHash,
        JsonObject newEntry)
    {
        var entries = catalog[arrayName] as JsonArray ?? new JsonArray();
        catalog[arrayName] = entries;
        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (!string.Equals(GetOptionalString(entry, idName), idValue, StringComparison.Ordinal))
            {
                continue;
            }

            var existingManifestHash = GetOptionalString(entry, "manifestHash");
            var existingArchiveHash = GetOptionalString(entry, "archiveHash");
            if (string.Equals(existingManifestHash, manifestHash, StringComparison.Ordinal) &&
                string.Equals(existingArchiveHash, archiveHash, StringComparison.Ordinal))
            {
                return;
            }

            throw new DeploymentProofPackagePromotionException(
                "Catalog entry conflict detected for deployment proof package.",
                [$"{idName}={idValue} already exists with different manifest/archive hashes."]);
        }

        entries.Add(newEntry);
    }

    private static void ValidateAllFixtureModes(DeploymentProofPackagePromotionPaths paths)
    {
        var componentErrors = FindComponentProof(paths, "hush-web-client", null)
            .Pipe(DeploymentProofPackageContracts.ValidateComponentProof)
            .Concat(FindComponentProof(paths, "hush-server-node", null).Pipe(DeploymentProofPackageContracts.ValidateComponentProof));
        var proofSet = ReadExample(paths, "bindings", "deployment-proof-set.json");
        var ledger = ReadExample(paths, "bindings", "per-election-deployment-binding-ledger.json");
        var ceremony = ReadExample(paths, "ceremonies", "deployment-ceremony.json");
        var readiness = ReadExample(paths, "readiness", "readiness-fragment.json");
        var errors = componentErrors
            .Concat(DeploymentProofPackageContracts.ValidateProofSet(proofSet))
            .Concat(DeploymentProofPackageContracts.ValidateBindingLedger(ledger))
            .Concat(DeploymentProofPackageContracts.ValidateCeremony(ceremony))
            .ToList();
        ValidateReadinessFragment(readiness, errors);

        if (errors.Count > 0)
        {
            throw new DeploymentProofPackagePromotionException("Validate-only fixture validation failed.", errors);
        }
    }

    private static void ValidateReadinessFragment(JsonObject readinessFragment, List<string> errors)
    {
        if (GetOptionalString(readinessFragment, "featureSlice") != "FEAT-132")
        {
            errors.Add("readiness fragment must reference FEAT-132.");
        }

        if (readinessFragment["dimensionScoreChange"] is not JsonObject scoreChange ||
            GetOptionalString(scoreChange, "dimensionId") != "RDY-DIM-006" ||
            scoreChange["previousScore"]?.GetValue<int>() != 4 ||
            scoreChange["acceptedScore"]?.GetValue<int>() != 8)
        {
            errors.Add("readiness fragment must record RDY-DIM-006 moving from 4 to 8.");
        }
    }

    private static void ValidateDownstreamHandoff(JsonObject handoff, List<string> errors)
    {
        if (GetOptionalString(handoff, "sourceFeature") != "FEAT-132")
        {
            errors.Add("downstream handoff must identify FEAT-132 as sourceFeature.");
        }

        if (GetOptionalString(handoff, "acceptanceGate") != "AT-RDY-005")
        {
            errors.Add("downstream handoff must identify AT-RDY-005.");
        }

        if (handoff["readinessRegisterHandoff"] is not JsonObject readiness ||
            readiness["dimensionScoreChange"] is not JsonObject scoreChange ||
            GetOptionalString(scoreChange, "dimensionId") != "RDY-DIM-006" ||
            scoreChange["previousScore"]?.GetValue<int>() != 4 ||
            scoreChange["acceptedScore"]?.GetValue<int>() != 8)
        {
            errors.Add("downstream handoff must carry RDY-DIM-006 4 -> 8 for FEAT-130.");
        }

        if (handoff["operationalEvidenceHandoff"] is not JsonObject operational ||
            operational["webClientProof"] is not JsonObject ||
            operational["hushServerNodeProof"] is not JsonObject ||
            string.IsNullOrWhiteSpace(GetOptionalString(operational, "bindingLedgerId")))
        {
            errors.Add("downstream handoff must carry FEAT-133 component proof and binding refs.");
        }

        if (handoff["pilotRehearsalHandoff"] is not JsonObject pilot ||
            pilot["publicRefs"] is not JsonArray ||
            pilot["restrictedRefs"] is not JsonArray)
        {
            errors.Add("downstream handoff must carry FEAT-141 public and restricted refs.");
        }

        if (handoff["runtimeVisibilityContract"] is not JsonObject runtime ||
            runtime["reconciliationCheckpoints"] is not JsonArray checkpoints ||
            !checkpoints.Any(node => string.Equals(node?.GetValue<string>(), "final_package_export", StringComparison.Ordinal)))
        {
            errors.Add("downstream handoff must preserve runtime visibility reconciliation checkpoints.");
        }

        var publicScan = DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown(
            "downstream-handoff.json",
            handoff.ToJsonString());
        errors.AddRange(publicScan);
    }

    private static JsonObject FindComponentProof(
        DeploymentProofPackagePromotionPaths paths,
        string? componentId,
        string? deploymentProofId)
    {
        foreach (var file in Directory.EnumerateFiles(paths.ComponentProofExamplesRoot, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var proof = DeploymentProofPackageContracts.ReadJsonObject(file, Path.GetFileName(file));
            if (!string.IsNullOrWhiteSpace(componentId) &&
                !string.Equals(GetOptionalString(proof, "componentId"), componentId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(deploymentProofId) &&
                !string.Equals(GetOptionalString(proof, "deploymentProofId"), deploymentProofId, StringComparison.Ordinal))
            {
                continue;
            }

            return proof;
        }

        throw new DeploymentProofPackagePromotionException(
            "Component proof source was not found.",
            [$"componentId={componentId ?? "<any>"} deploymentProofId={deploymentProofId ?? "<any>"}"]);
    }

    private static JsonObject ReadExample(DeploymentProofPackagePromotionPaths paths, string folder, string fileName) =>
        DeploymentProofPackageContracts.ReadJsonObject(Path.Combine(paths.ExamplesRoot, folder, fileName), fileName);

    private static void ApplyCommandOverrides(JsonObject componentProof, DeploymentProofPackagePromotionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CdProvider))
        {
            componentProof["cdProvider"] = options.CdProvider;
        }

        if (!string.IsNullOrWhiteSpace(options.CdRunId))
        {
            componentProof["cdRunId"] = options.CdRunId;
        }
    }

    private static void ValidatePathConfiguration(DeploymentProofPackagePromotionPaths paths)
    {
        var workspaceRoot = Path.GetFullPath(paths.WorkspaceRoot);
        EnsureDirectoryPathInside(workspaceRoot, paths.SourceRoot, "source root");
        EnsureDirectoryPathInside(workspaceRoot, paths.PublicOutputRoot, "public output root");
        EnsureDirectoryPathInside(workspaceRoot, paths.RestrictedOutputRoot, "restricted output root");
    }

    private static void EnsureDirectoryPathInside(string root, string candidate, string label)
    {
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidate));
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeploymentProofPackagePromotionException(
                $"Configured {label} escapes the workspace root.",
                [$"{label}: {fullCandidate}", $"workspace root: {fullRoot}"]);
        }
    }

    private static void WriteFileInside(string root, PromotedFile file)
    {
        var outputPath = Path.GetFullPath(Path.Combine(root, file.RelativePath));
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        if (!outputPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeploymentProofPackagePromotionException(
                "Generated output path escapes configured root.",
                [file.RelativePath]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, file.Bytes);
    }

    private static byte[] BuildDeterministicArchive(IReadOnlyList<PromotedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                entry.LastWriteTime = FixedZipTimestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.Bytes, 0, file.Bytes.Length);
            }
        }

        return stream.ToArray();
    }

    private static void ValidatePublicMarkdown(string fileName, string markdown)
    {
        var errors = DeploymentProofPackagePublicRedactionScanner.ScanPublicMarkdown(fileName, markdown);
        if (errors.Count > 0)
        {
            throw new DeploymentProofPackagePromotionException("Public output failed forbidden-material scan.", errors);
        }
    }

    private static byte[] ToCanonicalJsonBytes(JsonObject json) =>
        TextBytes(SortJson(json).ToJsonString(JsonOptions));

    private static JsonNode SortJson(JsonNode node) =>
        node switch
        {
            JsonObject obj => new JsonObject(obj
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value is null ? null : SortJson(kvp.Value).DeepClone()))),
            JsonArray array => new JsonArray(array.Select(item => item is null ? null : SortJson(item).DeepClone()).ToArray()),
            _ => node.DeepClone(),
        };

    private static byte[] TextBytes(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new UTF8Encoding(false).GetBytes(normalized);
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string GetRequiredString(JsonObject obj, string name) =>
        GetOptionalString(obj, name) ?? throw new InvalidOperationException($"{name} is required.");

    private static string? GetOptionalString(JsonObject obj, string name)
    {
        try
        {
            return obj[name]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void ScaffoldOutputRoots(DeploymentProofPackagePromotionPaths paths)
    {
        Directory.CreateDirectory(paths.PublicOutputRoot);
        Directory.CreateDirectory(paths.RestrictedOutputRoot);
    }

    private sealed record PromotedFile(string RelativePath, byte[] Bytes);

    private sealed record BuiltPackage(
        string PackageKind,
        string PackageId,
        DateTimeOffset GeneratedAt,
        string ManifestHash,
        string ArchiveHash,
        IReadOnlyList<PromotedFile> PublicFiles,
        IReadOnlyList<PromotedFile> RestrictedFiles);
}

internal static class DeploymentProofPackageFunctionalExtensions
{
    public static TResult Pipe<T, TResult>(this T value, Func<T, TResult> func) => func(value);
}

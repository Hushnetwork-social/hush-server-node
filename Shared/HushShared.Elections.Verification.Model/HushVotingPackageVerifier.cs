using System.Text;
using System.Text.Json;

namespace HushShared.Elections.Verification.Model;

public record HushVotingPackageVerificationRequest(
    string PackagePath,
    string VerifierProfileId,
    string? OutputPath = null);

public record HushVotingPackageVerificationResult(
    VerifierOutputRecord Output,
    string SummaryMarkdown,
    int ExitCode);

public sealed class HushVotingPackageVerifier
{
    public async Task<HushVotingPackageVerificationResult> VerifyAsync(
        HushVotingPackageVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsLiveDependency(request.PackagePath))
        {
            var output = CreateOutput(
                packageId: "unavailable",
                electionId: "unavailable",
                request.VerifierProfileId,
                VerificationOverallStatus.Fail,
                [
                    CreateResult(
                        "VFY-CLI-001",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.UnsupportedLiveDependency,
                        "The verifier only accepts local package paths in v1."),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }

        if (!Directory.Exists(request.PackagePath))
        {
            var output = CreateOutput(
                packageId: "unavailable",
                electionId: "unavailable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-PKG-001",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnreadable,
                        "The package path does not exist or is not a directory."),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }

        try
        {
            var manifest = await ReadJsonAsync<AuditPackageManifestRecord>(
                request.PackagePath,
                VerificationPackageFileNames.AuditPackageManifest,
                cancellationToken);
            var inputManifest = await ReadJsonAsync<VerifierInputManifestRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VerifierInputManifest,
                cancellationToken);
            var profile = await ReadJsonAsync<VerifierProfileRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VerifierProfile,
                cancellationToken);
            _ = await ReadJsonAsync<ElectionRecordReferenceRecord>(
                request.PackagePath,
                VerificationPackageFileNames.ElectionRecord,
                cancellationToken);

            var results = new List<VerifierCheckResultRecord>();
            var manifestResults = await CheckManifestAsync(request.PackagePath, manifest, cancellationToken);
            results.AddRange(manifestResults);
            results.Add(CheckProfile(request.VerifierProfileId, inputManifest, profile));

            if (manifestResults.Any(x => x.ResultCode == VerificationResultCodes.PackageManifestMissingArtifact))
            {
                var missingOutput = CreateOutput(
                    manifest.PackageId,
                    manifest.ElectionId,
                    request.VerifierProfileId,
                    VerificationOverallStatus.Fail,
                    results);
                return await WriteOutputAsync(request, missingOutput, cancellationToken);
            }

            results.Add(await CheckAcceptedBallotsAsync(request.PackagePath, cancellationToken));
            results.Add(await CheckPublishedBallotsAsync(request.PackagePath, cancellationToken));
            results.AddRange(await CheckPrivacyBoundaryAsync(request.PackagePath, manifest, cancellationToken));
            results.Add(await CheckFuturePublicationProofAsync(request.PackagePath, request.VerifierProfileId, cancellationToken));

            var overall = CalculateOverallStatus(results);
            var output = CreateOutput(
                manifest.PackageId,
                manifest.ElectionId,
                request.VerifierProfileId,
                overall,
                results);

            return await WriteOutputAsync(request, output, cancellationToken);
        }
        catch (JsonException exception)
        {
            var output = CreateOutput(
                packageId: "unparseable",
                electionId: "unparseable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-PKG-002",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnparseable,
                        exception.Message),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
        catch (IOException exception)
        {
            var output = CreateOutput(
                packageId: "unreadable",
                electionId: "unreadable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-PKG-003",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnreadable,
                        exception.Message),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
    }

    public static bool IsLiveDependency(string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return false;
        }

        return Uri.TryCreate(packagePath, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "postgres" or "sqlserver";
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckManifestAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        var results = new List<VerifierCheckResultRecord>();

        foreach (var entry in manifest.Entries)
        {
            var fullPath = ResolvePackagePath(packagePath, entry.Path);
            if (!File.Exists(fullPath))
            {
                results.Add(CreateResult(
                    "VFY-MAN-001",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"Manifest entry '{entry.Path}' is missing."));
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            var actual = VerificationCanonicalHash.ComputeManifestFileSha256(bytes);
            if (!string.Equals(actual, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(CreateResult(
                    "VFY-MAN-002",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestArtifactHashMismatch,
                    $"Manifest entry '{entry.Path}' hash does not match exported bytes.",
                    new Dictionary<string, string>
                    {
                        ["expected"] = entry.Sha256Hash,
                        ["actual"] = actual,
                    }));
            }
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "VFY-MAN-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageManifestValid,
                "All manifest entries exist and match their SHA-256 hashes."));
        }

        return results;
    }

    private static VerifierCheckResultRecord CheckProfile(
        string requestedProfile,
        VerifierInputManifestRecord inputManifest,
        VerifierProfileRecord profile)
    {
        if (!VerificationProfileIds.All.Contains(requestedProfile) ||
            !string.Equals(requestedProfile, inputManifest.VerifierProfileId, StringComparison.Ordinal) ||
            !string.Equals(requestedProfile, profile.ProfileId, StringComparison.Ordinal))
        {
            return CreateResult(
                "VFY-PROFILE-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.VerifierProfilePackageMismatch,
                "Requested verifier profile does not match the package profile.");
        }

        return CreateResult(
            "VFY-PROFILE-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Verifier profile matches the package input manifest.");
    }

    private static async Task<VerifierCheckResultRecord> CheckAcceptedBallotsAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var accepted = await ReadJsonAsync<AcceptedBallotSetArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.AcceptedBallotSet,
            cancellationToken);
        var duplicate = accepted.AcceptedBallots
            .GroupBy(x => x.BallotNullifier, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
        {
            return CreateResult(
                "VFY-ACCEPTED-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AcceptedBallotDuplicateNullifier,
                $"Duplicate ballot nullifier '{duplicate.Key}' found.");
        }

        var records = accepted.AcceptedBallots
            .Select(x => new HushShared.Elections.Model.ElectionAcceptedBallotRecord(
                Guid.NewGuid(),
                new HushShared.Elections.Model.ElectionId(Guid.Parse(accepted.ElectionId)),
                x.EncryptedBallotPackage,
                x.ProofBundle,
                x.BallotNullifier,
                DateTime.UnixEpoch))
            .ToArray();
        var actualHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(records));

        if (!string.Equals(actualHash, accepted.AcceptedBallotInventoryHash, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                "VFY-ACCEPTED-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AcceptedBallotInventoryHashMismatch,
                "Accepted ballot inventory hash does not match the accepted ballot set artifact.");
        }

        return CreateResult(
            "VFY-ACCEPTED-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Accepted ballot inventory hash and nullifier uniqueness passed.");
    }

    private static async Task<VerifierCheckResultRecord> CheckPublishedBallotsAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var published = await ReadJsonAsync<PublishedBallotStreamArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.PublishedBallotStream,
            cancellationToken);
        var sequences = published.PublishedBallots
            .Select(x => x.PublicationSequence)
            .Order()
            .ToArray();
        var expectedSequences = Enumerable.Range(1, sequences.Length).Select(x => (long)x).ToArray();

        if (!sequences.SequenceEqual(expectedSequences))
        {
            return CreateResult(
                "VFY-PUBLISHED-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublishedBallotSequenceInvalid,
                "Published ballot sequence must be contiguous and start at 1.");
        }

        var records = published.PublishedBallots
            .Select(x => new HushShared.Elections.Model.ElectionPublishedBallotRecord(
                Guid.NewGuid(),
                new HushShared.Elections.Model.ElectionId(Guid.Parse(published.ElectionId)),
                x.PublicationSequence,
                x.EncryptedBallotPackage,
                x.ProofBundle,
                DateTime.UnixEpoch,
                SourceBlockHeight: null,
                SourceBlockId: null))
            .ToArray();
        var actualHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputePublishedBallotStreamHash(records));

        if (!string.Equals(actualHash, published.PublishedBallotStreamHash, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                "VFY-PUBLISHED-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PublishedBallotStreamHashMismatch,
                "Published ballot stream hash does not match the published ballot stream artifact.");
        }

        return CreateResult(
            "VFY-PUBLISHED-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Published ballot stream hash and sequence checks passed.");
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckPrivacyBoundaryAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.PackageView != VerificationPackageView.PublicAnonymous)
        {
            return
            [
                CreateResult(
                    "VFY-PRIVACY-000",
                    VerificationCheckStatus.NotApplicable,
                    VerificationResultCodes.PackageStructureValid,
                    "Public privacy-boundary check is not applicable to restricted packages."),
            ];
        }

        var forbiddenFields = new List<string>();
        foreach (var entry in manifest.Entries)
        {
            if (VerificationPrivacyBoundary.IsRestrictedArtifactEntry(entry))
            {
                forbiddenFields.Add(entry.Path);
                continue;
            }

            if (!entry.MediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(ResolvePackagePath(packagePath, entry.Path), cancellationToken);
            forbiddenFields.AddRange(VerificationPrivacyBoundary.FindForbiddenPublicFields(CollectJsonPropertyNames(text)));
        }

        var distinctFields = forbiddenFields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctFields.Length > 0)
        {
            return
            [
                CreateResult(
                    "VFY-PRIVACY-001",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicRestrictedFieldLeak,
                    "Public package contains restricted evidence fields.",
                    distinctFields.ToDictionary(x => x, x => "forbidden", StringComparer.OrdinalIgnoreCase)),
            ];
        }

        return
        [
            CreateResult(
                "VFY-PRIVACY-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageStructureValid,
                "Public package contains no known restricted evidence fields."),
        ];
    }

    private static async Task<VerifierCheckResultRecord> CheckFuturePublicationProofAsync(
        string packagePath,
        string profileId,
        CancellationToken cancellationToken)
    {
        var tallyReplay = await ReadJsonAsync<TallyReplayArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.TallyReplay,
            cancellationToken);

        if (!string.Equals(tallyReplay.ResultCode, VerificationResultCodes.PublicationProofPendingFeat117, StringComparison.Ordinal))
        {
            return CreateResult(
                "VFY-SP07-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageStructureValid,
                "Publication proof evidence is present for the selected profile.");
        }

        var isHighAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        return CreateResult(
            "VFY-SP07-001",
            isHighAssurance ? VerificationCheckStatus.Fail : VerificationCheckStatus.Warn,
            VerificationResultCodes.PublicationProofPendingFeat117,
            isHighAssurance
                ? "High-assurance profile requires SP-07 publication proof evidence."
                : "SP-07 publication proof evidence is pending a later feature.");
    }

    private static VerificationOverallStatus CalculateOverallStatus(
        IReadOnlyList<VerifierCheckResultRecord> results)
    {
        if (results.Any(x => x.Status == VerificationCheckStatus.Fail))
        {
            return VerificationOverallStatus.Fail;
        }

        if (results.Any(x => x.Status == VerificationCheckStatus.Warn))
        {
            return VerificationOverallStatus.Warn;
        }

        return VerificationOverallStatus.Pass;
    }

    private static VerifierOutputRecord CreateOutput(
        string packageId,
        string electionId,
        string profileId,
        VerificationOverallStatus status,
        IReadOnlyList<VerifierCheckResultRecord> results) =>
        new(
            OutputVersion: "1.0",
            packageId,
            electionId,
            profileId,
            status,
            VerificationExitCodes.FromOverallStatus(status),
            DateTime.UtcNow,
            results);

    private static VerifierCheckResultRecord CreateResult(
        string checkCode,
        VerificationCheckStatus status,
        string resultCode,
        string message,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            checkCode,
            status,
            resultCode,
            message,
            evidence ?? new Dictionary<string, string>());

    private static async Task<T> ReadJsonAsync<T>(
        string packagePath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(ResolvePackagePath(packagePath, relativePath));
        return await JsonSerializer.DeserializeAsync<T>(stream, VerificationJson.Options, cancellationToken)
            ?? throw new JsonException($"Package file '{relativePath}' is empty.");
    }

    private static IEnumerable<string> CollectJsonPropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CollectPropertyNames(document.RootElement).ToArray();
    }

    private static IEnumerable<string> CollectPropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var childProperty in CollectPropertyNames(property.Value))
                {
                    yield return childProperty;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var childProperty in CollectPropertyNames(child))
                {
                    yield return childProperty;
                }
            }
        }
    }

    private static async Task<HushVotingPackageVerificationResult> WriteOutputAsync(
        HushVotingPackageVerificationRequest request,
        VerifierOutputRecord output,
        CancellationToken cancellationToken)
    {
        var summary = BuildSummary(output);
        var outputDirectory = request.OutputPath ??
            Path.Combine(
                Directory.Exists(request.PackagePath) ? request.PackagePath : Environment.CurrentDirectory,
                "verifier-output");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "VerifierOutput.json"),
            JsonSerializer.Serialize(output, VerificationJson.Options),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "VerifierSummary.md"),
            summary,
            cancellationToken);

        return new HushVotingPackageVerificationResult(output, summary, output.ExitCode);
    }

    private static string BuildSummary(VerifierOutputRecord output)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# HushVoting Verifier Summary");
        builder.AppendLine();
        builder.AppendLine($"Package: {output.PackageId}");
        builder.AppendLine($"Election: {output.ElectionId}");
        builder.AppendLine($"Profile: {output.VerifierProfileId}");
        builder.AppendLine($"Status: {output.OverallStatus}");
        builder.AppendLine();
        foreach (var result in output.Results)
        {
            builder.AppendLine($"- {result.CheckCode}: {result.Status} ({result.ResultCode})");
        }

        return builder.ToString();
    }

    private static string ResolvePackagePath(string packagePath, string relativePath) =>
        Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}

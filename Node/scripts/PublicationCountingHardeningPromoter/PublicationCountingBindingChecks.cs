using System.Text.Json.Nodes;

namespace PublicationCountingHardeningPromoter;

public sealed record PublicationCountingBindingCheckResult(
    string Status,
    IReadOnlyList<string> Errors,
    JsonObject Diagnostics)
{
    public bool Passed => Errors.Count == 0;
}

public static class PublicationCountingBindingChecks
{
    private const string AcceptedBallotSetPath = "artifacts/election-record/accepted-ballot-set.json";
    private const string PublishedBallotStreamPath = "artifacts/election-record/published-ballot-stream.json";
    private const string PublicationProofTranscriptPath = "artifacts/election-record/publication-proof-transcript.json";
    private const string PublicationProofVerifierOutputPath = "artifacts/election-record/publication-proof-verifier-output.json";
    private const string TallyReplayPath = "artifacts/election-record/tally-replay.json";
    private const string TrusteeReleaseEvidencePath = "artifacts/election-record/trustee-release-evidence.json";
    private const string TrusteeVerifierOutputPath = "artifacts/election-record/trustee-verifier-output.json";
    private const string ResultBindingPath = "artifacts/election-record/result-binding.json";

    public static PublicationCountingBindingCheckResult CheckAcceptedToPublished(
        PublicationCountingHardeningPromotionPaths paths,
        JsonObject source)
    {
        var errors = new List<string>();
        var packageRoot = ResolvePackageRoot(paths, source);
        var manifest = ReadPackageJson(packageRoot, "AuditPackageManifest.json", errors);
        var profile = ReadPackageJson(packageRoot, "VerifierProfile.json", errors);
        var accepted = ReadPackageJson(packageRoot, AcceptedBallotSetPath, errors);
        var published = ReadPackageJson(packageRoot, PublishedBallotStreamPath, errors);
        var transcript = ReadPackageJson(packageRoot, PublicationProofTranscriptPath, errors);
        var verifierOutput = ReadPackageJson(packageRoot, PublicationProofVerifierOutputPath, errors);
        var tally = ReadPackageJson(packageRoot, TallyReplayPath, errors);

        CheckManifestHashes(packageRoot, manifest, errors, [
            AcceptedBallotSetPath,
            PublishedBallotStreamPath,
            PublicationProofTranscriptPath,
            PublicationProofVerifierOutputPath,
            TallyReplayPath,
        ]);

        if (profile is not null &&
            !string.Equals(PublicationCountingHardeningContracts.GetString(profile, "profileId"), "public_anonymous_v1", StringComparison.Ordinal))
        {
            errors.Add("Unsupported verifier profile for FEAT-153 v1 publication/counting hardening.");
        }

        if (accepted is not null && published is not null)
        {
            var acceptedCount = PublicationCountingHardeningContracts.GetInt(accepted, "acceptedBallotCount");
            var publishedCount = PublicationCountingHardeningContracts.GetInt(published, "publishedBallotCount");
            CompareString(
                PublicationCountingHardeningContracts.GetString(accepted, "electionId"),
                PublicationCountingHardeningContracts.GetString(published, "electionId"),
                "accepted ballot set electionId must match published stream",
                errors);
            if (acceptedCount != publishedCount)
            {
                errors.Add($"Accepted/published count mismatch: accepted={acceptedCount}, published={publishedCount}.");
            }

            var acceptedProofHashes = GetStringValues(accepted, "acceptedBallots", "proofBundleHash")
                .Select(NormalizeHash)
                .ToArray();
            var publishedProofHashes = GetStringValues(published, "publishedBallots", "proofBundleHash")
                .Select(NormalizeHash)
                .ToArray();
            CompareInt(acceptedCount, acceptedProofHashes.Length, "accepted ballot count must match accepted ballot array", errors);
            CompareInt(publishedCount, publishedProofHashes.Length, "published ballot count must match published ballot array", errors);
            if (acceptedProofHashes.Any(string.IsNullOrWhiteSpace) || publishedProofHashes.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add("Accepted and published ballot proof hashes must not be empty.");
            }

            if (acceptedProofHashes.Length != acceptedProofHashes.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                errors.Add("Accepted ballot proof hashes contain duplicates.");
            }

            if (publishedProofHashes.Length != publishedProofHashes.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                errors.Add("Published ballot proof hashes contain duplicates.");
            }

            if (!acceptedProofHashes
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(publishedProofHashes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Published proof hash set does not match accepted proof hash set exactly once.");
            }

            var expectedSequence = Enumerable.Range(1, publishedProofHashes.Length).ToArray();
            var observedSequence = GetIntValues(published, "publishedBallots", "publicationSequence");
            if (!observedSequence.SequenceEqual(expectedSequence))
            {
                errors.Add("Published stream sequence must be contiguous and 1-based.");
            }
        }

        if (accepted is not null && tally is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(accepted, "acceptedBallotInventoryHash"),
                PublicationCountingHardeningContracts.GetString(tally, "acceptedBallotSetHash"),
                "accepted ballot inventory hash must match tally replay acceptedBallotSetHash",
                errors);
        }

        if (published is not null && tally is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(published, "publishedBallotStreamHash"),
                PublicationCountingHardeningContracts.GetString(tally, "publishedBallotStreamHash"),
                "published stream hash must match tally replay publishedBallotStreamHash",
                errors);
        }

        if (accepted is not null && published is not null && transcript is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(accepted, "electionId"),
                PublicationCountingHardeningContracts.GetString(transcript, "electionId"),
                "accepted ballot set electionId must match publication proof transcript",
                errors);
            CompareString(
                PublicationCountingHardeningContracts.GetString(published, "electionId"),
                PublicationCountingHardeningContracts.GetString(transcript, "electionId"),
                "published stream electionId must match publication proof transcript",
                errors);
            CompareString(
                PublicationCountingHardeningContracts.GetString(accepted, "acceptedBallotInventoryHash"),
                PublicationCountingHardeningContracts.GetString(transcript, "acceptedBallotSetHash"),
                "accepted ballot inventory hash must match publication proof transcript",
                errors);
            CompareString(
                PublicationCountingHardeningContracts.GetString(published, "publishedBallotStreamHash"),
                PublicationCountingHardeningContracts.GetString(transcript, "publishedBallotStreamHash"),
                "published stream hash must match publication proof transcript",
                errors);
            CompareInt(
                PublicationCountingHardeningContracts.GetInt(accepted, "acceptedBallotCount"),
                PublicationCountingHardeningContracts.GetInt(transcript, "acceptedBallotCount"),
                "accepted ballot count must match publication proof transcript",
                errors);
            CompareInt(
                PublicationCountingHardeningContracts.GetInt(published, "publishedBallotCount"),
                PublicationCountingHardeningContracts.GetInt(transcript, "publishedBallotCount"),
                "published ballot count must match publication proof transcript",
                errors);
        }

        if (transcript is not null && tally is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(transcript, "transcriptHash"),
                PublicationCountingHardeningContracts.GetString(tally, "publicationProofTranscriptHash"),
                "publication proof transcript hash must match tally replay",
                errors);
            CompareString(
                PublicationCountingHardeningContracts.GetString(transcript, "proofHash"),
                PublicationCountingHardeningContracts.GetString(tally, "publicationProofHash"),
                "publication proof hash must match tally replay",
                errors);
        }

        CheckPublicationVerifierOutput(verifierOutput, accepted, published, errors);

        return CreateResult(errors, new JsonObject
        {
            ["packagePath"] = PublicationCountingHardeningContracts.GetString(
                PublicationCountingHardeningContracts.RequireObject(source, "packageRefs"),
                "packagePath"),
            ["checkedArtifactPaths"] = Strings([
                AcceptedBallotSetPath,
                PublishedBallotStreamPath,
                PublicationProofTranscriptPath,
                PublicationProofVerifierOutputPath,
                TallyReplayPath,
            ]),
        });
    }

    public static PublicationCountingBindingCheckResult CheckTallyReplay(
        PublicationCountingHardeningPromotionPaths paths,
        JsonObject source)
    {
        var errors = new List<string>();
        var packageRoot = ResolvePackageRoot(paths, source);
        var manifest = ReadPackageJson(packageRoot, "AuditPackageManifest.json", errors);
        var published = ReadPackageJson(packageRoot, PublishedBallotStreamPath, errors);
        var tally = ReadPackageJson(packageRoot, TallyReplayPath, errors);
        var trusteeRelease = ReadPackageJson(packageRoot, TrusteeReleaseEvidencePath, errors);
        var trusteeVerifierOutput = ReadPackageJson(packageRoot, TrusteeVerifierOutputPath, errors);
        var resultBinding = ReadPackageJson(packageRoot, ResultBindingPath, errors);

        CheckManifestHashes(packageRoot, manifest, errors, [
            PublishedBallotStreamPath,
            TallyReplayPath,
            TrusteeReleaseEvidencePath,
            TrusteeVerifierOutputPath,
            ResultBindingPath,
        ]);

        if (published is not null && tally is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(published, "electionId"),
                PublicationCountingHardeningContracts.GetString(tally, "electionId"),
                "published stream electionId must match tally replay",
                errors);
            CompareString(
                PublicationCountingHardeningContracts.GetString(published, "publishedBallotStreamHash"),
                PublicationCountingHardeningContracts.GetString(tally, "publishedBallotStreamHash"),
                "published stream hash must match tally replay",
                errors);
        }

        if (tally is not null && resultBinding is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(tally, "electionId"),
                PublicationCountingHardeningContracts.GetString(resultBinding, "electionId"),
                "tally replay electionId must match result binding",
                errors);
            if (!PublicationCountingHardeningContracts.GetBool(resultBinding, "cleanFinalization"))
            {
                errors.Add("Result binding must record cleanFinalization=true for the supported v1 happy path.");
            }

            RequireValue(resultBinding, "outcomeStatus", "clean_finalized", errors);
            RequireValue(resultBinding, "finalizationMode", "clean_finalization", errors);
            RequireNonEmpty(resultBinding, "officialResultArtifactId", errors);
            RequireNonEmpty(resultBinding, "unofficialResultArtifactId", errors);
            RequireNonEmpty(resultBinding, "reportPackageHash", errors);
        }

        if (tally is not null && trusteeRelease is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(tally, "electionId"),
                PublicationCountingHardeningContracts.GetString(trusteeRelease, "electionId"),
                "tally replay electionId must match trustee release evidence",
                errors);
        }

        CheckTrusteeVerifierOutput(trusteeVerifierOutput, errors);
        if (tally is not null)
        {
            RequireValue(tally, "evidenceStatus", "pass", errors);
            RequireValue(tally, "resultCode", "publication_proof_evidence_valid", errors);
        }

        return CreateResult(errors, new JsonObject
        {
            ["packagePath"] = PublicationCountingHardeningContracts.GetString(
                PublicationCountingHardeningContracts.RequireObject(source, "packageRefs"),
                "packagePath"),
            ["checkedArtifactPaths"] = Strings([
                PublishedBallotStreamPath,
                TallyReplayPath,
                TrusteeReleaseEvidencePath,
                TrusteeVerifierOutputPath,
                ResultBindingPath,
            ]),
        });
    }

    private static string ResolvePackageRoot(PublicationCountingHardeningPromotionPaths paths, JsonObject source)
    {
        var packagePath = PublicationCountingHardeningContracts.GetString(
            PublicationCountingHardeningContracts.RequireObject(source, "packageRefs"),
            "packagePath");
        return PublicationCountingHardeningContracts.ResolveWorkspaceRelativePath(paths.WorkspaceRoot, packagePath);
    }

    private static JsonObject? ReadPackageJson(string packageRoot, string relativePath, List<string> errors)
    {
        var path = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            errors.Add($"Missing package artifact: {relativePath}");
            return null;
        }

        return PublicationCountingHardeningContracts.ReadJsonObject(path, relativePath);
    }

    private static void CheckManifestHashes(
        string packageRoot,
        JsonObject? manifest,
        List<string> errors,
        IReadOnlyList<string> requiredPaths)
    {
        if (manifest is null)
        {
            return;
        }

        var entries = PublicationCountingHardeningContracts.RequireArray(manifest, "entries")
            .OfType<JsonObject>()
            .ToDictionary(
                entry => PublicationCountingHardeningContracts.GetString(entry, "path"),
                entry => PublicationCountingHardeningContracts.GetString(entry, "sha256Hash"),
                StringComparer.Ordinal);

        foreach (var relativePath in requiredPaths)
        {
            if (!entries.TryGetValue(relativePath, out var expectedHash))
            {
                errors.Add($"AuditPackageManifest.json is missing entry for {relativePath}.");
                continue;
            }

            var path = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var observedHash = PublicationCountingHardeningContracts.Sha256File(path).Replace("sha256:", "", StringComparison.Ordinal);
            if (!string.Equals(observedHash, NormalizeHash(expectedHash), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"AuditPackageManifest hash mismatch for {relativePath}: expected {NormalizeHash(expectedHash)}, observed {observedHash}.");
            }
        }
    }

    private static void CheckPublicationVerifierOutput(
        JsonObject? verifierOutput,
        JsonObject? accepted,
        JsonObject? published,
        List<string> errors)
    {
        if (verifierOutput is null)
        {
            return;
        }

        var passResult = PublicationCountingHardeningContracts.RequireArray(verifierOutput, "results")
            .OfType<JsonObject>()
            .FirstOrDefault(result =>
                PublicationCountingHardeningContracts.GetString(result, "status") == "pass" &&
                PublicationCountingHardeningContracts.GetString(result, "resultCode") == "publication_proof_evidence_valid");
        if (passResult is null)
        {
            errors.Add("Publication proof verifier output must contain a passing publication_proof_evidence_valid result.");
            return;
        }

        var evidence = PublicationCountingHardeningContracts.RequireObject(passResult, "evidence");
        if (accepted is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(accepted, "electionId"),
                PublicationCountingHardeningContracts.GetString(verifierOutput, "electionId"),
                "publication proof verifier electionId must match accepted set",
                errors);
        }

        if (published is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetString(published, "electionId"),
                PublicationCountingHardeningContracts.GetString(verifierOutput, "electionId"),
                "publication proof verifier electionId must match published stream",
                errors);
        }

        if (accepted is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetInt(accepted, "acceptedBallotCount").ToString(System.Globalization.CultureInfo.InvariantCulture),
                PublicationCountingHardeningContracts.GetString(evidence, "accepted_ballot_count"),
                "publication proof verifier accepted ballot count must match accepted set",
                errors);
        }

        if (published is not null)
        {
            CompareString(
                PublicationCountingHardeningContracts.GetInt(published, "publishedBallotCount").ToString(System.Globalization.CultureInfo.InvariantCulture),
                PublicationCountingHardeningContracts.GetString(evidence, "published_ballot_count"),
                "publication proof verifier published ballot count must match published stream",
                errors);
        }
    }

    private static void CheckTrusteeVerifierOutput(JsonObject? trusteeVerifierOutput, List<string> errors)
    {
        if (trusteeVerifierOutput is null)
        {
            return;
        }

        var hasPass = PublicationCountingHardeningContracts.RequireArray(trusteeVerifierOutput, "results")
            .OfType<JsonObject>()
            .Any(result =>
                PublicationCountingHardeningContracts.GetString(result, "status") == "pass" &&
                PublicationCountingHardeningContracts.GetString(result, "resultCode") == "trustee_control_domain_evidence_valid");
        if (!hasPass)
        {
            errors.Add("Trustee verifier output must contain a passing trustee_control_domain_evidence_valid result.");
        }
    }

    private static IReadOnlyList<string> GetStringValues(JsonObject source, string arrayProperty, string property)
    {
        return PublicationCountingHardeningContracts.RequireArray(source, arrayProperty)
            .OfType<JsonObject>()
            .Select(item => PublicationCountingHardeningContracts.GetString(item, property))
            .ToArray();
    }

    private static IReadOnlyList<int> GetIntValues(JsonObject source, string arrayProperty, string property)
    {
        return PublicationCountingHardeningContracts.RequireArray(source, arrayProperty)
            .OfType<JsonObject>()
            .Select(item => PublicationCountingHardeningContracts.GetInt(item, property))
            .ToArray();
    }

    private static void CompareString(string expected, string observed, string label, List<string> errors)
    {
        if (!string.Equals(expected, observed, StringComparison.Ordinal))
        {
            errors.Add($"{label}: expected {expected}, observed {observed}.");
        }
    }

    private static void CompareInt(int expected, int observed, string label, List<string> errors)
    {
        if (expected != observed)
        {
            errors.Add($"{label}: expected {expected}, observed {observed}.");
        }
    }

    private static void RequireNonEmpty(JsonObject value, string property, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(PublicationCountingHardeningContracts.GetString(value, property)))
        {
            errors.Add($"{property} must not be empty.");
        }
    }

    private static void RequireValue(JsonObject value, string property, string expected, List<string> errors)
    {
        var observed = PublicationCountingHardeningContracts.GetString(value, property);
        if (!string.Equals(expected, observed, StringComparison.Ordinal))
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static string NormalizeHash(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value["sha256:".Length..].ToLowerInvariant()
            : value.ToLowerInvariant();

    private static PublicationCountingBindingCheckResult CreateResult(List<string> errors, JsonObject diagnostics) =>
        new(errors.Count == 0 ? "accepted" : "blocked", errors, diagnostics);

    private static JsonArray Strings(IReadOnlyList<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
}

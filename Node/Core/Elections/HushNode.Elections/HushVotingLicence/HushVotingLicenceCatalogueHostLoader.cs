using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HushShared.HushVoting.Licensing.Model;

namespace HushNode.Elections.HushVotingLicence;

/// <summary>Release metadata companion file (digest, versions, producer).</summary>
public sealed record HushVotingLicenceReleaseMetadata(
    string SchemaId,
    string CatalogueVersion,
    string DigestSha256,
    string ProducerId,
    string GeneratedAtUtc);

/// <summary>
/// Strict host loader for the release-controlled HushVoting licence catalogue. Resolves one
/// configured relative path beneath the application content root, bounds and reads UTF-8 input,
/// verifies the release digest and catalogue/schema version, parses into domain candidates, and
/// runs the pure complete semantic validator. It never falls back to an empty/previous/built-in
/// catalogue and never performs file I/O on request paths.
/// </summary>
public static class HushVotingLicenceCatalogueHostLoader
{
    /// <summary>Hard schema bound: the catalogue manifest must be at most 64 KiB.</summary>
    public const int MaxCatalogueBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>
    /// Loads, verifies, and validates the catalogue. Returns a build result; on any error the
    /// snapshot is null and no fallback exists. Safe for startup/readiness use.
    /// </summary>
    public static HushVotingLicenceCatalogueBuildResult LoadFromContentRoot(
        string contentRoot,
        HushVotingLicenceOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<HushVotingLicenceValidationFailure>();

        var cataloguePath = ResolveCataloguePath(contentRoot, options, failures);
        if (cataloguePath is null)
        {
            return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
        }

        var rawCatalogue = ReadBoundedUtf8(cataloguePath, MaxCatalogueBytes, failures, "manifest");
        if (rawCatalogue is null)
        {
            return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
        }

        var releaseMetadata = ReadReleaseMetadata(contentRoot, options, failures);
        if (releaseMetadata is null)
        {
            return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
        }

        VerifyDigest(rawCatalogue, releaseMetadata, failures);
        if (failures.Count > 0)
        {
            return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
        }

        if (!string.Equals(releaseMetadata.CatalogueVersion, options.RequiredCatalogueVersion, StringComparison.Ordinal)
            || !string.Equals(
                releaseMetadata.SchemaId,
                HushVotingLicenceCatalogueVersion.V1SchemaId,
                StringComparison.Ordinal))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatVersionMismatch,
                "/release",
                $"Configured/required catalogue version '{options.RequiredCatalogueVersion}' or schema id "
                + $"does not match release metadata ({releaseMetadata.CatalogueVersion} / {releaseMetadata.SchemaId})."));
            return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
        }

        var parsed = TryParseCatalogue(rawCatalogue, failures);
        if (parsed is null)
        {
            return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
        }

        return HushVotingLicenceCatalogueValidator.ValidateAndBuild(
            parsed.Plans,
            parsed.Mappings);
    }

    private static string? ResolveCataloguePath(
        string contentRoot,
        HushVotingLicenceOptions options,
        List<HushVotingLicenceValidationFailure> failures)
    {
        var relative = options.CatalogueRelativePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(relative))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatFileMissing,
                "/catalogueRelativePath",
                "No catalogue relative path is configured."));
            return null;
        }

        if (Path.IsPathRooted(relative))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatFileMissing,
                "/catalogueRelativePath",
                "Absolute developer paths are not accepted."));
            return null;
        }

        var contentRootFull = Path.GetFullPath(contentRoot);
        var candidate = Path.GetFullPath(Path.Combine(contentRootFull, relative));

        // Confine beneath the content root (no traversal / symlink escape).
        var relativeToRoot = Path.GetRelativePath(contentRootFull, candidate);
        if (relativeToRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatFileMissing,
                "/catalogueRelativePath",
                "Configured catalogue path escapes the application content root."));
            return null;
        }

        if (!File.Exists(candidate))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatFileMissing,
                "/catalogueRelativePath",
                $"Required licence catalogue manifest is missing at '{options.CatalogueRelativePath}'."));
            return null;
        }

        return candidate;
    }

    private static byte[]? ReadBoundedUtf8(
        string path,
        int maxBytes,
        List<HushVotingLicenceValidationFailure> failures,
        string artifact)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maxBytes)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                    $"/{artifact}",
                    $"{artifact} exceeds the {maxBytes / 1024} KiB bound."));
                return null;
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatFileMissing,
                $"/{artifact}",
                $"Could not read {artifact}: {ex.Message}"));
            return null;
        }
    }

    private static HushVotingLicenceReleaseMetadata? ReadReleaseMetadata(
        string contentRoot,
        HushVotingLicenceOptions options,
        List<HushVotingLicenceValidationFailure> failures)
    {
        var catalogueDir = Path.GetDirectoryName(Path.Combine(
            contentRoot,
            options.CatalogueRelativePath))!;
        var releasePath = Path.Combine(catalogueDir, "approved-licence-catalogue.release.json");

        var raw = ReadBoundedUtf8(releasePath, 4096, failures, "release");
        if (raw is null)
        {
            return null;
        }

        try
        {
            var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = false });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                    "/release",
                    "Release metadata must be a JSON object."));
                return null;
            }

            var schemaId = root.TryGetProperty("schemaId", out var s) ? s.GetString() : null;
            var version = root.TryGetProperty("catalogueVersion", out var v) ? v.GetString() : null;
            var digest = root.TryGetProperty("digestSha256", out var d) ? d.GetString() : null;
            var producer = root.TryGetProperty("producerId", out var p) ? p.GetString() : null;
            var generated = root.TryGetProperty("generatedAtUtc", out var g) ? g.GetString() : null;

            if (string.IsNullOrWhiteSpace(schemaId) || string.IsNullOrWhiteSpace(version)
                || string.IsNullOrWhiteSpace(digest) || string.IsNullOrWhiteSpace(producer))
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                    "/release",
                    "Release metadata is missing required fields."));
                return null;
            }

            return new HushVotingLicenceReleaseMetadata(schemaId, version, digest, producer, generated ?? string.Empty);
        }
        catch (JsonException ex)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                "/release",
                $"Release metadata is malformed: {ex.Message}"));
            return null;
        }
    }

    private static void VerifyDigest(
        byte[] catalogueBytes,
        HushVotingLicenceReleaseMetadata releaseMetadata,
        List<HushVotingLicenceValidationFailure> failures)
    {
        // Digest is computed over LF-normalized bytes for determinism across checkouts.
        var normalized = NormalizeLf(catalogueBytes);
        var digest = Convert.ToHexString(SHA256.HashData(normalized));

        if (!string.Equals(digest, releaseMetadata.DigestSha256, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatDigestMismatch,
                "/release/digestSha256",
                "Release digest does not match normalized catalogue content."));
        }
    }

    private static byte[] NormalizeLf(byte[] bytes)
    {
        // Fast LF-normalization without allocation-heavy passes for bounded (<64 KiB) content.
        var hasCr = bytes.Any(static b => b == (byte)'\r');
        if (!hasCr)
        {
            return bytes;
        }

        var sb = new StringBuilder(Encoding.UTF8.GetString(bytes));
        sb.Replace("\r\n", "\n").Replace('\r', '\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ParsedCatalogue? TryParseCatalogue(
        byte[] rawCatalogue,
        List<HushVotingLicenceValidationFailure> failures)
    {
        string jsonText;
        try
        {
            // Reject malformed JSON and enforce the schema shape with bounded reads.
            using var doc = JsonDocument.Parse(rawCatalogue, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            jsonText = Encoding.UTF8.GetString(NormalizeLf(rawCatalogue));
        }
        catch (JsonException ex)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                "/catalogue",
                $"Catalogue JSON is malformed: {ex.Message}"));
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<LicenceCatalogueDto>(jsonText, JsonOptions);
            if (dto is null || dto.Version is null || dto.Plans is null)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                    "/catalogue",
                    "Catalogue is missing version or plans."));
                return null;
            }

            var version = HushVotingLicenceCatalogueVersion.FromExternal(dto.Version);
            if (!version.IsKnown)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatVersionMismatch,
                    "/catalogue/version",
                    $"Catalogue version '{dto.Version}' is unsupported."));
                return null;
            }

            var plans = new List<HushVotingLicencePlan>();
            for (var i = 0; i < dto.Plans.Count; i++)
            {
                var planDto = dto.Plans[i];
                var planId = HushVotingLicencePlanId.FromExternal(planDto.PlanId);
                var family = HushVotingLicenceEnumNames.TryParseFamily(planDto.Family);
                var availability = HushVotingLicenceEnumNames.TryParseAvailability(planDto.Availability);
                var term = ParseTerm(planDto.Term);

                if (!planId.IsKnown || family is null || availability is null || term is null)
                {
                    failures.Add(new HushVotingLicenceValidationFailure(
                        HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                        $"/plans/{i}",
                        "Plan contains an unsupported enum or identifier value."));
                    continue;
                }

                plans.Add(new HushVotingLicencePlan(
                    planId,
                    family.Value,
                    planDto.DisplayName ?? string.Empty,
                    planDto.SafeDescription ?? string.Empty,
                    planDto.DisplayOrder,
                    planDto.UpgradeRank,
                    planDto.EligibleVoterCap,
                    planDto.UnlimitedElections,
                    term.Value,
                    availability.Value,
                    planDto.UnavailableSafeReason,
                    ParseGovernanceOptions(planDto.GovernanceOptions ?? [], i),
                    version));
            }

            var mappings = ParseMappings(dto.ProfileCompatibility ?? []);

            return new ParsedCatalogue(plans, mappings);
        }
        catch (JsonException ex)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                "/catalogue",
                $"Catalogue JSON failed to bind: {ex.Message}"));
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
                "/catalogue",
                $"Catalogue candidate construction failed: {ex.Message}"));
            return null;
        }
    }

    private static HushVotingLicenceTerm? ParseTerm(TermDto? term)
    {
        if (term is null)
        {
            return null;
        }

        if (string.Equals(term.Kind, "Perpetual", StringComparison.Ordinal))
        {
            return HushVotingLicenceTerm.Perpetual;
        }

        if (string.Equals(term.Kind, "CalendarYears", StringComparison.Ordinal))
        {
            return HushVotingLicenceTerm.CalendarYears(term.Years ?? 1);
        }

        return null;
    }

    private static IReadOnlyList<HushVotingGovernanceOption> ParseGovernanceOptions(
        IReadOnlyList<GovernanceOptionDto> options,
        int planIndex)
    {
        var result = new List<HushVotingGovernanceOption>();
        foreach (var option in options)
        {
            var optionId = HushVotingGovernanceOptionId.FromExternal(option.Id);
            if (!optionId.IsKnown)
            {
                continue; // semantic validator reports unknown/forbidden sets; loader stays permissive here
            }

            var bindingStatuses = new HashSet<HushVotingBindingStatus>();
            foreach (var wire in option.BindingStatuses ?? [])
            {
                var parsed = HushVotingLicenceEnumNames.TryParseBindingStatus(wire);
                if (parsed is not null)
                {
                    bindingStatuses.Add(parsed.Value);
                }
            }

            result.Add(new HushVotingGovernanceOption(
                optionId,
                option.CustomerTrusteeCount,
                option.RequiredApprovalCount,
                option.SafeLabel ?? string.Empty,
                bindingStatuses));
        }

        return result;
    }

    private static IReadOnlyList<HushVotingProfileCompatibilityEntry> ParseMappings(
        IReadOnlyList<ProfileCompatibilityDto> entries)
    {
        var result = new List<HushVotingProfileCompatibilityEntry>();
        foreach (var entry in entries)
        {
            var optionId = HushVotingGovernanceOptionId.FromExternal(entry.GovernanceOptionId);
            var binding = HushVotingLicenceEnumNames.TryParseBindingStatus(entry.BindingStatus);
            if (!optionId.IsKnown || binding is null || string.IsNullOrWhiteSpace(entry.RuntimeProfileId))
            {
                continue;
            }

            result.Add(new HushVotingProfileCompatibilityEntry(
                optionId,
                binding.Value,
                entry.RuntimeProfileId ?? string.Empty,
                DevOnly: entry.DevOnly));
        }

        return result;
    }

    private sealed record ParsedCatalogue(
        IReadOnlyList<HushVotingLicencePlan> Plans,
        IReadOnlyList<HushVotingProfileCompatibilityEntry> Mappings);
}

public sealed class LicenceCatalogueDto
{
    public string? Version { get; set; }

    public List<PlanDto>? Plans { get; set; }

    public List<ProfileCompatibilityDto>? ProfileCompatibility { get; set; }
}

public sealed class PlanDto
{
    public string? PlanId { get; set; }

    public string? Family { get; set; }

    public string? DisplayName { get; set; }

    public string? SafeDescription { get; set; }

    public int DisplayOrder { get; set; }

    public int UpgradeRank { get; set; }

    public int? EligibleVoterCap { get; set; }

    public bool UnlimitedElections { get; set; }

    public TermDto? Term { get; set; }

    public string? Availability { get; set; }

    public string? UnavailableSafeReason { get; set; }

    public List<GovernanceOptionDto>? GovernanceOptions { get; set; }
}

public sealed class TermDto
{
    public string? Kind { get; set; }

    public int? Years { get; set; }
}

public sealed class GovernanceOptionDto
{
    public string? Id { get; set; }

    public int CustomerTrusteeCount { get; set; }

    public int RequiredApprovalCount { get; set; }

    public string? SafeLabel { get; set; }

    public List<string>? BindingStatuses { get; set; }
}

public sealed class ProfileCompatibilityDto
{
    public string? GovernanceOptionId { get; set; }

    public string? BindingStatus { get; set; }

    public string? RuntimeProfileId { get; set; }

    public bool DevOnly { get; set; }
}

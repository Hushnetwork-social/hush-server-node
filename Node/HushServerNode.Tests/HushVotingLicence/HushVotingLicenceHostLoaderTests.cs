using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HushNode.Elections.HushVotingLicence;
using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// Host loader and registry cross-validation tests (Phase 6). Uses isolated temporary content roots;
/// never touches real host assets or the network.
/// </summary>
public sealed class HushVotingLicenceHostLoaderTests : IDisposable
{
    private readonly string _root;

    public HushVotingLicenceHostLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"feat012-licence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteAsset(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }

    private void WriteReleaseMetadata(string digest, string version = HushVotingLicenceCatalogueVersion.V1Value)
    {
        WriteAsset(
            "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.release.json",
            $$"""
            {
              "schemaId": "hushvoting-licence-catalogue/v1",
              "catalogueVersion": "{{version}}",
              "digestSha256": "{{digest}}",
              "producerId": "unit-test",
              "generatedAtUtc": "2026-09-03T00:00:00Z"
            }
            """);
    }

    private static string Sha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n"));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ValidCatalogueJson() => """
        {
          "version": "hushvoting-licence-catalogue/v1.0.0",
          "plans": [
            {
              "planId": "hushvoting.direct.free",
              "family": "Direct",
              "displayName": "HushVoting! Direct Free",
              "safeDescription": "Admin-controlled elections for up to 100 eligible voters, with no customer trustees.",
              "displayOrder": 10,
              "upgradeRank": 0,
              "eligibleVoterCap": 100,
              "unlimitedElections": true,
              "term": { "kind": "Perpetual" },
              "availability": "Default",
              "governanceOptions": [
                { "id": "no-customer-trustees", "customerTrusteeCount": 0, "requiredApprovalCount": 0,
                  "safeLabel": "No customer trustees", "bindingStatuses": ["NonBinding", "Binding"] }
              ],
              "catalogueVersion": "hushvoting-licence-catalogue/v1.0.0"
            },
            {
              "planId": "hushvoting.veritas.500",
              "family": "Veritas",
              "displayName": "HushVoting! Veritas 500",
              "safeDescription": "Elections for up to 500 eligible voters, with no trustees or a fixed 3-of-5 trustee ceremony.",
              "displayOrder": 20,
              "upgradeRank": 1000,
              "eligibleVoterCap": 500,
              "unlimitedElections": true,
              "term": { "kind": "CalendarYears", "years": 1 },
              "availability": "AutomaticUpgrade",
              "governanceOptions": [
                { "id": "no-customer-trustees", "customerTrusteeCount": 0, "requiredApprovalCount": 0,
                  "safeLabel": "No customer trustees", "bindingStatuses": ["NonBinding", "Binding"] },
                { "id": "trustees-3of5", "customerTrusteeCount": 5, "requiredApprovalCount": 3,
                  "safeLabel": "3 of 5 trustees", "bindingStatuses": ["NonBinding", "Binding"] }
              ],
              "catalogueVersion": "hushvoting-licence-catalogue/v1.0.0"
            },
            {
              "planId": "hushvoting.veritas.2000",
              "family": "Veritas",
              "displayName": "HushVoting! Veritas 2k",
              "safeDescription": "Elections for up to 2,000 eligible voters, with no trustees or a fixed 3-of-5 or 7-of-10 trustee ceremony.",
              "displayOrder": 30,
              "upgradeRank": 2000,
              "eligibleVoterCap": 2000,
              "unlimitedElections": true,
              "term": { "kind": "CalendarYears", "years": 1 },
              "availability": "AutomaticUpgrade",
              "governanceOptions": [
                { "id": "no-customer-trustees", "customerTrusteeCount": 0, "requiredApprovalCount": 0,
                  "safeLabel": "No customer trustees", "bindingStatuses": ["NonBinding", "Binding"] },
                { "id": "trustees-3of5", "customerTrusteeCount": 5, "requiredApprovalCount": 3,
                  "safeLabel": "3 of 5 trustees", "bindingStatuses": ["NonBinding", "Binding"] },
                { "id": "trustees-7of10", "customerTrusteeCount": 10, "requiredApprovalCount": 7,
                  "safeLabel": "7 of 10 trustees", "bindingStatuses": ["NonBinding", "Binding"] }
              ],
              "catalogueVersion": "hushvoting-licence-catalogue/v1.0.0"
            },
            {
              "planId": "hushvoting.veritas.10000",
              "family": "Veritas",
              "displayName": "HushVoting! Veritas 10k",
              "safeDescription": "Elections for up to 10,000 eligible voters, with no trustees or a fixed 3-of-5, 7-of-10, or 8-of-13 trustee ceremony.",
              "displayOrder": 40,
              "upgradeRank": 3000,
              "eligibleVoterCap": 10000,
              "unlimitedElections": true,
              "term": { "kind": "CalendarYears", "years": 1 },
              "availability": "AutomaticUpgrade",
              "governanceOptions": [
                { "id": "no-customer-trustees", "customerTrusteeCount": 0, "requiredApprovalCount": 0,
                  "safeLabel": "No customer trustees", "bindingStatuses": ["NonBinding", "Binding"] },
                { "id": "trustees-3of5", "customerTrusteeCount": 5, "requiredApprovalCount": 3,
                  "safeLabel": "3 of 5 trustees", "bindingStatuses": ["NonBinding", "Binding"] },
                { "id": "trustees-7of10", "customerTrusteeCount": 10, "requiredApprovalCount": 7,
                  "safeLabel": "7 of 10 trustees", "bindingStatuses": ["NonBinding", "Binding"] },
                { "id": "trustees-8of13", "customerTrusteeCount": 13, "requiredApprovalCount": 8,
                  "safeLabel": "8 of 13 trustees", "bindingStatuses": ["NonBinding", "Binding"] }
              ],
              "catalogueVersion": "hushvoting-licence-catalogue/v1.0.0"
            },
            {
              "planId": "hushvoting.enterprise",
              "family": "Enterprise",
              "displayName": "HushVoting! Enterprise",
              "safeDescription": "Customer-specific voter and trustee configuration. Contact provider - not yet available.",
              "displayOrder": 50,
              "upgradeRank": 4000,
              "eligibleVoterCap": null,
              "unlimitedElections": true,
              "term": { "kind": "CalendarYears", "years": 1 },
              "availability": "Unavailable",
              "unavailableSafeReason": "Customer-specific configuration is not yet available in v1.",
              "governanceOptions": [],
              "catalogueVersion": "hushvoting-licence-catalogue/v1.0.0"
            }
          ],
          "profileCompatibility": [
            { "governanceOptionId": "no-customer-trustees", "bindingStatus": "NonBinding", "runtimeProfileId": "admin-dev-1of1", "devOnly": true },
            { "governanceOptionId": "no-customer-trustees", "bindingStatus": "Binding", "runtimeProfileId": "admin-prod-1of1", "devOnly": false },
            { "governanceOptionId": "trustees-3of5", "bindingStatus": "NonBinding", "runtimeProfileId": "dkg-dev-3of5", "devOnly": true },
            { "governanceOptionId": "trustees-3of5", "bindingStatus": "Binding", "runtimeProfileId": "dkg-prod-3of5", "devOnly": false },
            { "governanceOptionId": "trustees-7of10", "bindingStatus": "NonBinding", "runtimeProfileId": "dkg-dev-7of10", "devOnly": true },
            { "governanceOptionId": "trustees-7of10", "bindingStatus": "Binding", "runtimeProfileId": "dkg-prod-7of10", "devOnly": false },
            { "governanceOptionId": "trustees-8of13", "bindingStatus": "NonBinding", "runtimeProfileId": "dkg-dev-8of13", "devOnly": true },
            { "governanceOptionId": "trustees-8of13", "bindingStatus": "Binding", "runtimeProfileId": "dkg-prod-8of13", "devOnly": false }
          ]
        }
        """;

    private void WriteValidCatalogue()
    {
        var json = ValidCatalogueJson();
        WriteAsset(
            "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json",
            json);
        WriteReleaseMetadata(Sha256(json));
    }

    private HushVotingLicenceOptions Options(string relative = "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json") =>
        new() { CatalogueRelativePath = relative };

    [Fact]
    public void Load_ValidAssets_ProducesImmutableSnapshot()
    {
        WriteValidCatalogue();

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, Options());

        result.IsValid.Should().BeTrue();
        result.Catalogue.Should().NotBeNull();
        result.Catalogue!.Plans.Should().HaveCount(5);
    }

    [Fact]
    public void Load_MissingFile_ReturnsFileMissing()
    {
        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, Options());

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatFileMissing);
    }

    [Fact]
    public void Load_AbsolutePath_IsRejected()
    {
        var options = new HushVotingLicenceOptions
        {
            CatalogueRelativePath = Path.Combine(_root, "x.json"),
        };

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, options);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatFileMissing &&
            f.Message.Contains("Absolute", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_TraversalPath_IsRejected()
    {
        var options = new HushVotingLicenceOptions
        {
            CatalogueRelativePath = Path.Combine("..", "..", "etc", "passwd"),
        };

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, options);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatFileMissing &&
            f.Message.Contains("escape", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_DigestMismatch_IsRejected()
    {
        var json = ValidCatalogueJson();
        WriteAsset(
            "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json",
            json);
        WriteReleaseMetadata(Sha256(json + " "));

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, Options());

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatDigestMismatch);
    }

    [Fact]
    public void Load_VersionMismatch_IsRejected()
    {
        var json = ValidCatalogueJson();
        WriteAsset(
            "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json",
            json);
        WriteReleaseMetadata(Sha256(json), version: "hushvoting-licence-catalogue/v9.9.9");

        var options = Options();

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, options);

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatVersionMismatch);
    }

    [Fact]
    public void Load_MalformedJson_IsRejected()
    {
        const string malformed = "{ not json";
        WriteAsset(
            "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json",
            malformed);
        WriteReleaseMetadata(Sha256(malformed));

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, Options());

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatSchemaInvalid);
    }

    [Fact]
    public void Load_OversizeAsset_IsRejected()
    {
        WriteAsset(
            "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json",
            new string('x', 70 * 1024));
        WriteReleaseMetadata("0000000000000000000000000000000000000000000000000000000000000000");

        var result = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(_root, Options());

        result.IsValid.Should().BeFalse();
        result.Validation.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatSchemaInvalid &&
            f.Message.Contains("64 KiB", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistryCrossValidation_ValidMappings_Pass()
    {
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var registry = BuildRegistry();

        var result = HushVotingLicenceProfileRegistryValidator.ValidateAgainstRegistry(catalogue, registry);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RegistryCrossValidation_MissingProfile_Fails()
    {
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var registry = BuildRegistry().Where(p => p.ProfileId != "dkg-prod-3of5").ToArray();

        var result = HushVotingLicenceProfileRegistryValidator.ValidateAgainstRegistry(catalogue, registry);

        result.IsValid.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatProfileMissing &&
            f.Message.Contains("dkg-prod-3of5", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistryCrossValidation_DevOnlyMismatch_Fails()
    {
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var registry = BuildRegistry()
            .Select(p => p.ProfileId == "dkg-prod-3of5"
                ? p with { DevOnly = true }
                : p)
            .ToArray();

        var result = HushVotingLicenceProfileRegistryValidator.ValidateAgainstRegistry(catalogue, registry);

        result.IsValid.Should().BeFalse();
        result.Failures.Should().Contain(f =>
            f.Code == HushVotingLicenceValidationCodes.LicCatProfileMismatch);
    }

    private static IReadOnlyList<ElectionCeremonyProfileRecord> BuildRegistry() =>
    [
        Profile("admin-dev-1of1", 1, 1, devOnly: true),
        Profile("admin-prod-1of1", 1, 1, devOnly: false),
        Profile("dkg-dev-3of5", 5, 3, devOnly: true),
        Profile("dkg-prod-3of5", 5, 3, devOnly: false),
        Profile("dkg-dev-7of10", 10, 7, devOnly: true),
        Profile("dkg-prod-7of10", 10, 7, devOnly: false),
        Profile("dkg-dev-8of13", 13, 8, devOnly: true),
        Profile("dkg-prod-8of13", 13, 8, devOnly: false),
    ];

    private static ElectionCeremonyProfileRecord Profile(string id, int trustees, int approvals, bool devOnly) =>
        new(
            id,
            id,
            "test",
            "unit-test",
            "v1",
            trustees,
            approvals,
            devOnly,
            DateTime.UtcNow,
            DateTime.UtcNow);
}

public sealed class HushVotingLicenceOptionsTests
{
    [Fact]
    public void DefaultOptions_PointAtCommittedV1Asset()
    {
        var options = new HushVotingLicenceOptions();

        options.CatalogueRelativePath.Should()
            .Be("licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json");
        options.RequiredCatalogueVersion.Should().Be("hushvoting-licence-catalogue/v1.0.0");
    }
}

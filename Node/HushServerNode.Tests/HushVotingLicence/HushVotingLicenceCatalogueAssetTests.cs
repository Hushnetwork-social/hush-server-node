using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// Release-asset tests for the committed v1 catalogue, schema, and release metadata. These are
/// file-presence/content tests; the strict loader, digest verification, and schema validation live
/// with Phase 6 and the domain semantic validator lives with Phase 3.
/// </summary>
public sealed class HushVotingLicenceCatalogueAssetTests
{
    private static readonly string AssetRoot = ResolveAssetRoot();

    private static string ResolveAssetRoot()
    {
        // Prefer the publish/output copy when present (Phase 6 wires csproj copy); otherwise read
        // from the committed source tree next to HushServerNode.
        var output = Path.Combine(AppContext.BaseDirectory, "licence-catalogues", "hushvoting-v1.0.0");
        if (Directory.Exists(output))
        {
            return output;
        }

        var source = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "HushServerNode",
            "licence-catalogues",
            "hushvoting-v1.0.0"));
        return source;
    }

    private static string ReadRequiredAsset(string fileName)
    {
        var path = Path.Combine(AssetRoot, fileName);
        if (!File.Exists(path))
        {
            throw new Xunit.Sdk.XunitException($"Required release asset missing: {path}");
        }

        return File.ReadAllText(path);
    }

    [Fact]
    public void ReleaseFolder_ContainsCatalogueSchemaAndReleaseMetadata()
    {
        Directory.Exists(AssetRoot).Should().BeTrue();
        File.Exists(Path.Combine(AssetRoot, "approved-licence-catalogue.json")).Should().BeTrue();
        File.Exists(Path.Combine(AssetRoot, "approved-licence-catalogue.schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(AssetRoot, "approved-licence-catalogue.release.json")).Should().BeTrue();
    }

    [Fact]
    public void Catalogue_IsValidJsonWithFiveExactPlansInOrder()
    {
        var doc = JsonDocument.Parse(ReadRequiredAsset("approved-licence-catalogue.json"));

        doc.RootElement.GetProperty("version").GetString().Should()
            .Be("hushvoting-licence-catalogue/v1.0.0");

        var plans = doc.RootElement.GetProperty("plans");
        plans.GetArrayLength().Should().Be(5);
        plans.EnumerateArray().Select(static p => p.GetProperty("planId").GetString()).Should().Equal(
            "hushvoting.direct.free",
            "hushvoting.veritas.500",
            "hushvoting.veritas.2000",
            "hushvoting.veritas.10000",
            "hushvoting.enterprise");
    }

    [Fact]
    public void Catalogue_ContainsNoForbiddenCopyOrPaidDirectTiers()
    {
        var text = ReadRequiredAsset("approved-licence-catalogue.json");

        text.Should().NotContain("hushvoting.direct.100");
        text.Should().NotContain("hushvoting.direct.250");
        text.Should().NotContain("hushvoting.direct.500");
        text.Should().NotContain("price");
        text.Should().NotContain("currency");
        text.Should().NotContain("payment");
        text.Should().NotContain("providerKey");
        text.Should().NotContain("billing");
        text.Should().NotContain("legal");
        text.Should().NotContain("readiness claim");
    }

    [Fact]
    public void Catalogue_GovernanceOptionsAreCumulativePerTier()
    {
        var doc = JsonDocument.Parse(ReadRequiredAsset("approved-licence-catalogue.json"));
        var plans = doc.RootElement.GetProperty("plans");

        static string[] OptionIds(JsonElement plan) =>
            plan.GetProperty("governanceOptions").EnumerateArray()
                .Select(static o => o.GetProperty("id").GetString()!)
                .ToArray();

        OptionIds(plans[0]).Should().Equal("no-customer-trustees");
        OptionIds(plans[1]).Should().Equal("no-customer-trustees", "trustees-3of5");
        OptionIds(plans[2]).Should().Equal("no-customer-trustees", "trustees-3of5", "trustees-7of10");
        OptionIds(plans[3]).Should().Equal(
            "no-customer-trustees", "trustees-3of5", "trustees-7of10", "trustees-8of13");
        plans[4].GetProperty("governanceOptions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Catalogue_EnterpriseIsCustomerSpecificAndUnavailable()
    {
        var doc = JsonDocument.Parse(ReadRequiredAsset("approved-licence-catalogue.json"));
        var enterprise = doc.RootElement.GetProperty("plans")[4];

        enterprise.GetProperty("planId").GetString().Should().Be("hushvoting.enterprise");
        enterprise.TryGetProperty("eligibleVoterCap", out var cap).Should().BeTrue();
        cap.ValueKind.Should().Be(JsonValueKind.Null);
        enterprise.GetProperty("availability").GetString().Should().Be("Unavailable");
        enterprise.GetProperty("family").GetString().Should().Be("Enterprise");
        enterprise.GetProperty("safeDescription").GetString()!.Should().Contain("Contact provider");
    }

    [Fact]
    public void Schema_DeclaresExpectedSchemaIdAndBounds()
    {
        var doc = JsonDocument.Parse(ReadRequiredAsset("approved-licence-catalogue.schema.json"));

        doc.RootElement.GetProperty("$id").GetString().Should().Be("hushvoting-licence-catalogue/v1");
        doc.RootElement.GetProperty("$defs").GetProperty("plan")
            .GetProperty("properties").GetProperty("displayName")
            .GetProperty("maxLength").GetInt32().Should().Be(80);
        doc.RootElement.GetProperty("$defs").GetProperty("plan")
            .GetProperty("properties").GetProperty("safeDescription")
            .GetProperty("maxLength").GetInt32().Should().Be(320);
        doc.RootElement.GetProperty("properties").GetProperty("plans")
            .GetProperty("maxItems").GetInt32().Should().Be(32);
    }

    [Fact]
    public void ReleaseMetadata_IdentifiesDigestSchemaAndVersion()
    {
        var doc = JsonDocument.Parse(ReadRequiredAsset("approved-licence-catalogue.release.json"));

        doc.RootElement.GetProperty("schemaId").GetString().Should().Be("hushvoting-licence-catalogue/v1");
        doc.RootElement.GetProperty("catalogueVersion").GetString().Should()
            .Be("hushvoting-licence-catalogue/v1.0.0");
        doc.RootElement.GetProperty("digestSha256").GetString().Should()
            .MatchRegex("^[0-9A-F]{64}$");
        doc.RootElement.GetProperty("producerId").GetString().Should().Contain("FEAT-012");
    }
}

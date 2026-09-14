using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// Architecture duplication guard (FEAT-012 Phase 7): the HushVoting client must not contain a
/// duplicated editable catalogue (stable-plan constant sets, safe-copy catalogue, or bundled release
/// manifest). The server release manifest is the single source of plan truth.
/// </summary>
public sealed class HushVotingLicenceClientDuplicationTests
{
    private static string ResolveRepoRoot()
    {
        // Tests run from Node/HushServerNode.Tests/bin/Debug -> workspace root is four levels up.
        var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        while (!Directory.Exists(Path.Combine(candidate, "hush-voting-web-client")))
        {
            var parent = Path.GetDirectoryName(candidate);
            if (parent is null || parent == candidate)
            {
                return string.Empty;
            }

            candidate = parent;
        }

        return candidate;
    }

    [Fact]
    public void ClientRepository_DoesNotBundleAnEditableCatalogueTruth()
    {
        var root = ResolveRepoRoot();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "hush-voting-web-client")))
        {
            // Client repo not present in this checkout; guard is satisfied vacuously here and the
            // dedicated-client CI checkout covers it (see FeatureTasks Phase 7 CI note).
            return;
        }

        var clientSourceFiles = Directory.EnumerateFiles(
                Path.Combine(root, "hush-voting-web-client", "src"),
                "*.ts",
                SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(root, "hush-voting-web-client", "src"),
                "*.tsx",
                SearchOption.AllDirectories))
            .Where(p => !p.Contains("node_modules", StringComparison.Ordinal))
            .Where(p => !p.Contains(".test.", StringComparison.Ordinal) && !p.Contains(".spec.", StringComparison.Ordinal))
            .ToArray();

        clientSourceFiles.Should().NotBeEmpty("expected client source to exist for the duplication scan");

        var forbiddenTokens = new[]
        {
            "hushvoting.direct.free",
            "hushvoting.veritas.500",
            "hushvoting.veritas.2000",
            "hushvoting.veritas.10000",
            "hushvoting.enterprise",
            "hushvoting-licence-catalogue/v1.0.0",
        };

        foreach (var file in clientSourceFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                text.Should().NotContain(
                    token,
                    $"client must not duplicate catalogue truth '{token}' (found in {file}); server catalogue is the single source");
            }
        }
    }

    [Fact]
    public void CommittedReleaseAsset_IsReplayedByCurrentReaderContract()
    {
        // The accepted-fixture corpus must replay through the current pure domain contract.
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var asset = HushVotingLicenceCatalogueAssetTestsFixture.ReadV1Catalogue();

        asset.Version.Should().Be("hushvoting-licence-catalogue/v1.0.0");
        asset.Plans.Select(static p => p.PlanId).Should().Equal(
            catalogue.Plans.Select(static p => p.Id.Value));
    }
}

internal static class HushVotingLicenceCatalogueAssetTestsFixture
{
    private static readonly string AssetRoot = ResolveAssetRoot();

    public static LicenceCatalogueView ReadV1Catalogue()
    {
        var path = Path.Combine(AssetRoot, "approved-licence-catalogue.json");
        if (!File.Exists(path))
        {
            throw new Xunit.Sdk.XunitException($"Required release asset missing: {path}");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var plans = root.GetProperty("plans").EnumerateArray()
            .Select(static p => new LicencePlanView(
                p.GetProperty("planId").GetString()!,
                p.GetProperty("displayOrder").GetInt32()))
            .OrderBy(static p => p.DisplayOrder)
            .ToArray();

        return new LicenceCatalogueView(root.GetProperty("version").GetString()!, plans);
    }

    private static string ResolveAssetRoot()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "licence-catalogues", "hushvoting-v1.0.0");
        if (Directory.Exists(output))
        {
            return output;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "HushServerNode",
            "licence-catalogues",
            "hushvoting-v1.0.0"));
    }
}

public sealed record LicenceCatalogueView(string Version, IReadOnlyList<LicencePlanView> Plans);

public sealed record LicencePlanView(string PlanId, int DisplayOrder);

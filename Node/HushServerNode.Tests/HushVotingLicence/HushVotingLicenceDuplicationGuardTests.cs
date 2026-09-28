using System.Diagnostics;
using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// Architecture duplication guard (FEAT-012 Phase 7): the HushVoting client must not contain a
/// duplicated editable catalogue (policy terms, plan metadata tables, or bundled release manifest).
/// Stable wire vocabulary and compatibility comparisons are required by FEAT-016 AC-016-018.
/// The server release manifest remains the single source of plan truth.
/// </summary>
public sealed class HushVotingLicenceClientDuplicationTests
{
    private static string ResolveRepoRoot()
    {
        // Find the nearest paired checkout, including isolated Git worktrees.
        var candidate = AppContext.BaseDirectory;
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
    public async Task ClientRepository_DoesNotBundleAnEditableCatalogueTruth()
    {
        var root = ResolveRepoRoot();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "hush-voting-web-client")))
        {
            // Client repo not present in this checkout; guard is satisfied vacuously here and the
            // dedicated-client CI checkout covers it (see FeatureTasks Phase 7 CI note).
            return;
        }

        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Fixtures", "licensing", "client-catalogue-guard.cjs"));
        start.ArgumentList.Add(Path.Combine(root, "hush-voting-web-client"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new Xunit.Sdk.XunitException("Client catalogue architecture guard timed out.");
        }
        process.ExitCode.Should().Be(0,
            "FEAT-012 AC-012-024 forbids client policy authority while FEAT-016 AC-016-018 requires compatibility vocabulary. {0} {1}",
            await output, await error);
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

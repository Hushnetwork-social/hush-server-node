using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ProductionLikeOperationalRunPromoter;

namespace HushServerNode.Tests.Elections;

internal static class ProductionLikeOperationalRunTestHelpers
{
    private static readonly string[] SourceRootRelativePath =
    [
        "Overview",
        "HushVotingReadiness",
        "Production-Like-Operational-Run"
    ];

    private static readonly string[] FixtureRootRelativePath =
    [
        "Node",
        "HushServerNode.Tests",
        "Elections",
        "TestFixtures",
        "Production-Like-Operational-Run"
    ];

    public static ProductionLikeOperationalRunPromotionPaths CreatePaths([CallerFilePath] string sourceFilePath = "")
    {
        var workspaceRoot = FindWorkspaceRoot(sourceFilePath);
        var paths = ProductionLikeOperationalRunPromotionPaths.FromWorkspaceRoot(workspaceRoot);
        var sourceRoot = ResolveSourceRoot(workspaceRoot);
        var examplesRoot = Path.Combine(sourceRoot, "examples");

        return paths with
        {
            SourceRoot = sourceRoot,
            SchemasRoot = Path.Combine(sourceRoot, "schemas"),
            ExamplesRoot = examplesRoot,
            DefaultSourceInput = Path.Combine(
                examplesRoot,
                "release-baseline",
                ProductionLikeOperationalRunPromotionPaths.SourceFileName),
            FixtureCatalogPath = Path.Combine(
                examplesRoot,
                ProductionLikeOperationalRunPromotionPaths.FixtureCatalogFileName)
        };
    }

    public static JsonObject LoadSchema() =>
        LoadMemoryBankJson(
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run",
            "schemas",
            "production-like-operational-run-source.schema.json");

    public static JsonObject LoadBaseline() =>
        LoadMemoryBankJson(
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run",
            "examples",
            "release-baseline",
            "production-like-operational-run-source.json");

    public static JsonObject LoadCatalog() =>
        LoadMemoryBankJson(
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run",
            "examples",
            "fixture-catalog.json");

    public static JsonObject[] LoadCases() =>
        LoadCatalog()["cases"]!.AsArray().Select(item => item!.AsObject()).ToArray();

    public static JsonObject LoadSourceForCase(JsonObject fixtureCase)
    {
        var source = fixtureCase.TryGetPropertyValue("sourcePath", out var sourcePathNode) &&
            sourcePathNode is not null
                ? LoadMemoryBankJsonFromRelativePath(sourcePathNode.GetValue<string>())
                : LoadBaseline();

        if (fixtureCase.TryGetPropertyValue("mutationPath", out var mutationPathNode) &&
            mutationPathNode is not null)
        {
            ApplyMutation(
                source,
                mutationPathNode.GetValue<string>(),
                fixtureCase["invalidValue"]!.DeepClone());
        }

        return source;
    }

    public static JsonObject LoadSourceForCategory(string category)
    {
        var fixtureCase = LoadCases().Single(item => item["category"]!.GetValue<string>() == category);
        return LoadSourceForCase(fixtureCase);
    }

    public static JsonObject LoadMemoryBankJson(params string[] relativePath)
    {
        var workspaceRoot = FindWorkspaceRoot();
        var fullPath = Path.Combine(new[] { ResolveSourceRoot(workspaceRoot) }.Concat(relativePath.Skip(3)).ToArray());
        return JsonNode.Parse(File.ReadAllText(fullPath))!.AsObject();
    }

    public static JsonObject LoadMemoryBankJsonFromRelativePath(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return LoadMemoryBankJson(parts);
    }

    public static bool SourceRelativeFileExists(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var workspaceRoot = FindWorkspaceRoot();
        var fullPath = Path.Combine(new[] { ResolveSourceRoot(workspaceRoot) }.Concat(parts.Skip(3)).ToArray());
        return File.Exists(fullPath);
    }

    public static string FindWorkspaceRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), sourceFilePath })
        {
            var root = FindWorkspaceRootFrom(startPath);
            if (root is not null)
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException("Could not locate HushNetworkOrg workspace root.");
    }

    private static void ApplyMutation(JsonObject source, string mutationPath, JsonNode? invalidValue)
    {
        var path = mutationPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length == 0)
        {
            throw new InvalidOperationException("Mutation path cannot be empty.");
        }

        var current = source;
        foreach (var segment in path.Take(path.Length - 1))
        {
            current = current[segment]?.AsObject() ??
                throw new InvalidOperationException($"Mutation segment '{segment}' is not an object.");
        }

        current[path[^1]] = invalidValue;
    }

    private static string? FindWorkspaceRootFrom(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (IsWorkspaceRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsWorkspaceRoot(string path) =>
        Directory.Exists(GetMemoryBankSourceRoot(path)) ||
        Directory.Exists(GetVendoredFixtureSourceRoot(path));

    private static string ResolveSourceRoot(string workspaceRoot)
    {
        var memoryBankRoot = GetMemoryBankSourceRoot(workspaceRoot);
        return Directory.Exists(memoryBankRoot)
            ? memoryBankRoot
            : GetVendoredFixtureSourceRoot(workspaceRoot);
    }

    private static string GetMemoryBankSourceRoot(string workspaceRoot) =>
        Path.Combine(new[] { workspaceRoot, "hush-memory-bank" }.Concat(SourceRootRelativePath).ToArray());

    private static string GetVendoredFixtureSourceRoot(string workspaceRoot) =>
        Path.Combine(new[] { workspaceRoot }.Concat(FixtureRootRelativePath).ToArray());
}

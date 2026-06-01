using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ProductionLikeOperationalRunPromoter;

namespace HushServerNode.Tests.Elections;

internal static class ProductionLikeOperationalRunTestHelpers
{
    public static ProductionLikeOperationalRunPromotionPaths CreatePaths([CallerFilePath] string sourceFilePath = "") =>
        ProductionLikeOperationalRunPromotionPaths.FromWorkspaceRoot(FindWorkspaceRoot(sourceFilePath));

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
        var fullPath = Path.Combine(new[] { FindWorkspaceRoot(), "hush-memory-bank" }.Concat(relativePath).ToArray());
        return JsonNode.Parse(File.ReadAllText(fullPath))!.AsObject();
    }

    public static JsonObject LoadMemoryBankJsonFromRelativePath(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return LoadMemoryBankJson(parts);
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
        Directory.Exists(Path.Combine(path, "hush-memory-bank")) &&
        (Directory.Exists(Path.Combine(path, "hush-server-node")) ||
            Directory.Exists(Path.Combine(path, "Node")));
}

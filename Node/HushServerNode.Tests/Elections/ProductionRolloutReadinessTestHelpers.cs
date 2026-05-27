using System.Text.Json.Nodes;
using ProductionRolloutReadinessPromoter;

namespace HushServerNode.Tests.Elections;

internal static class ProductionRolloutReadinessTestHelpers
{
    public static ProductionRolloutReadinessPromotionPaths CreatePaths() =>
        HushVotingReadinessTestArtifacts.CreateProductionRolloutReadinessPaths();

    public static JsonObject LoadExample(string exampleFolder)
    {
        var paths = CreatePaths();
        var path = Path.Combine(paths.ExamplesRoot, exampleFolder, ProductionRolloutReadinessPromotionPaths.SourceFileName);
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
            throw new InvalidOperationException($"Example fixture {exampleFolder} is not a JSON object.");
    }

    public static JsonObject ReadArtifactJson(ProductionRolloutGeneratedPackage package, string relativePath)
    {
        var artifact = package.Artifacts.Single(item => item.RelativePath == relativePath);
        return JsonNode.Parse(artifact.Content)?.AsObject() ??
            throw new InvalidOperationException($"Artifact {relativePath} is not a JSON object.");
    }

    public static string WriteSourceExample(
        ProductionRolloutReadinessPromotionPaths paths,
        JsonObject source,
        string exampleName)
    {
        var folder = Path.Combine(paths.ExamplesRoot, $"{exampleName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ProductionRolloutReadinessPromotionPaths.SourceFileName);
        File.WriteAllText(path, ProductionRolloutReadinessContracts.CanonicalJson(source));
        return folder;
    }
}

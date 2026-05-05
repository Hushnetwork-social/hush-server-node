using System.Text;
using System.Text.Json;

namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
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


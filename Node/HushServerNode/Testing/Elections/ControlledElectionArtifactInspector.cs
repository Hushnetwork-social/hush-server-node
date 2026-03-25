using System.Collections.Immutable;

namespace HushServerNode.Testing.Elections;

internal static class ControlledElectionArtifactInspector
{
    private static readonly ImmutableArray<string> DefaultForbiddenFileNameFragments =
    [
        "full-private-key",
        "full_key",
        "full-key",
        "private-key",
        "master-key",
        "election-secret",
    ];

    public static ControlledElectionArtifactInspectionResult InspectWorkspaceForForbiddenArtifacts(
        string workspaceRoot,
        ImmutableArray<string> forbiddenContentNeedles,
        ImmutableArray<string>? forbiddenFileNameFragments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        if (!Directory.Exists(workspaceRoot))
        {
            return ControlledElectionArtifactInspectionResult.Dirty(
                "Controlled artifact search workspace root does not exist.",
                ImmutableArray.Create(workspaceRoot));
        }

        var findings = ImmutableArray.CreateBuilder<string>();
        var fileNameFragments = (forbiddenFileNameFragments ?? DefaultForbiddenFileNameFragments)
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .ToImmutableArray();
        var contentNeedles = forbiddenContentNeedles
            .Where(needle => !string.IsNullOrWhiteSpace(needle))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

        foreach (var filePath in Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(workspaceRoot, filePath).Replace('\\', '/');

            if (fileNameFragments.Any(fragment =>
                    relativePath.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add($"Suspicious file name detected: {relativePath}");
            }

            string? content = null;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var forbiddenNeedle in contentNeedles)
            {
                if (content.Contains(forbiddenNeedle, StringComparison.Ordinal))
                {
                    findings.Add($"Forbidden key material detected in: {relativePath}");
                    break;
                }
            }
        }

        return findings.Count == 0
            ? ControlledElectionArtifactInspectionResult.Clean(
                "No normal full-key artifact was found in the inspected controlled workspace.")
            : ControlledElectionArtifactInspectionResult.Dirty(
                "Controlled artifact inspection found suspicious full-key material.",
                findings.ToImmutable());
    }
}

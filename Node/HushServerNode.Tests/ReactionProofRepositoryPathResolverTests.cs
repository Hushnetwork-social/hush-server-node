using FluentAssertions;
using HushServerNode.Testing;
using Xunit;

namespace HushServerNode.Tests;

public sealed class ReactionProofRepositoryPathResolverTests
{
    [Fact]
    public void ResolveFromWorkspaceRoot_WhenMonorepoLayoutExists_ShouldReturnServerAndWebRoots()
    {
        using var temp = new TemporaryMonorepoLayout();

        var paths = ReactionProofRepositoryPathResolver.ResolveFromWorkspaceRoot(temp.WorkspaceRoot);

        paths.ServerRepositoryRoot.Should().Be(Path.Combine(temp.WorkspaceRoot, "hush-server-node"));
        paths.WebClientRoot.Should().Be(Path.Combine(temp.WorkspaceRoot, "hush-web-client"));
    }

    [Fact]
    public void ResolveFromRuntimeBase_WhenStartedFromIntegrationBinDebug_ShouldReturnSiblingWebClientRoot()
    {
        using var temp = new TemporaryMonorepoLayout();
        var runtimeBaseDirectory = Path.Combine(
            temp.WorkspaceRoot,
            "hush-server-node",
            "Node",
            "HushNode.IntegrationTests",
            "bin",
            "Debug");
        Directory.CreateDirectory(runtimeBaseDirectory);

        var paths = ReactionProofRepositoryPathResolver.ResolveFromRuntimeBase(runtimeBaseDirectory);

        paths.ServerRepositoryRoot.Should().Be(Path.Combine(temp.WorkspaceRoot, "hush-server-node"));
        paths.WebClientRoot.Should().Be(Path.Combine(temp.WorkspaceRoot, "hush-web-client"));
    }

    private sealed class TemporaryMonorepoLayout : IDisposable
    {
        public TemporaryMonorepoLayout()
        {
            WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"feat087-paths-{Guid.NewGuid():N}");
            Directory.CreateDirectory(WorkspaceRoot);

            var serverRoot = Path.Combine(WorkspaceRoot, "hush-server-node");
            var nodeRoot = Path.Combine(serverRoot, "Node");
            var webRoot = Path.Combine(WorkspaceRoot, "hush-web-client");

            Directory.CreateDirectory(nodeRoot);
            Directory.CreateDirectory(Path.Combine(webRoot, "scripts"));

            File.WriteAllText(Path.Combine(nodeRoot, "HushServerNode.sln"), string.Empty);
            File.WriteAllText(Path.Combine(webRoot, "package.json"), """{ "name": "hush-web-client" }""");
            File.WriteAllText(Path.Combine(webRoot, "scripts", "generate-reaction-proof.mjs"), "export {};");
        }

        public string WorkspaceRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(WorkspaceRoot))
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }
        }
    }
}

using FluentAssertions;
using HushServerNode.Testing;
using Xunit;

namespace HushServerNode.Tests;

public sealed class ReactionProofPathInspectorTests
{
    [Fact]
    public void InspectFromWorkspaceRoot_WhenArtifactsMissing_ShouldReportConcreteBlockers()
    {
        using var temp = new TemporaryWorkspaceRoot();

        var readiness = ReactionProofPathInspector.InspectFromWorkspaceRoot(temp.RootPath, testHostDevModeEnabled: false);

        readiness.NonDevBenchmarkReady.Should().BeFalse();
        readiness.ClientProverArtifactsAvailable.Should().BeFalse();
        readiness.ClientHeadlessProverDependencyAvailable.Should().BeFalse();
        readiness.ServerVerificationKeyAvailable.Should().BeFalse();
        readiness.Notes.Should().Contain("Client prover artifacts missing");
        readiness.Notes.Should().Contain("Client snarkjs dependency missing");
        readiness.Notes.Should().Contain("Server verification key missing");
        readiness.TestHostDevModeEnabled.Should().BeFalse();
        readiness.ServerVerificationKeyParsingImplemented.Should().BeTrue();
        readiness.ServerFullGroth16VerificationImplemented.Should().BeTrue();
    }

    [Fact]
    public void InspectFromWorkspaceRoot_WhenArtifactsExistAndDevModeDisabled_ShouldReportReady()
    {
        using var temp = new TemporaryWorkspaceRoot();
        temp.CreateClientArtifacts();
        temp.CreateClientPackageJsonWithSnarkJs();
        temp.CreateInstalledSnarkJsPackage();
        temp.CreateServerVerificationKey();

        var readiness = ReactionProofPathInspector.InspectFromWorkspaceRoot(temp.RootPath, testHostDevModeEnabled: false);

        readiness.NonDevBenchmarkReady.Should().BeTrue();
        readiness.ClientProverArtifactsAvailable.Should().BeTrue();
        readiness.ClientHeadlessProverDependencyAvailable.Should().BeTrue();
        readiness.ServerVerificationKeyAvailable.Should().BeTrue();
        readiness.Notes.Should().Be("Basic non-dev proof path prerequisites detected");
    }

    [Fact]
    public void InspectFromWorkspaceRoot_WhenArtifactsExistButDevModeEnabled_ShouldStayBlocked()
    {
        using var temp = new TemporaryWorkspaceRoot();
        temp.CreateClientArtifacts();
        temp.CreateClientPackageJsonWithSnarkJs();
        temp.CreateInstalledSnarkJsPackage();
        temp.CreateServerVerificationKey();

        var readiness = ReactionProofPathInspector.InspectFromWorkspaceRoot(temp.RootPath, testHostDevModeEnabled: true);

        readiness.NonDevBenchmarkReady.Should().BeFalse();
        readiness.ClientProverArtifactsAvailable.Should().BeTrue();
        readiness.ServerVerificationKeyAvailable.Should().BeTrue();
        readiness.TestHostDevModeEnabled.Should().BeTrue();
        readiness.Notes.Should().Contain("Integration test host currently runs with Reactions:DevMode=true");
    }

    private sealed class TemporaryWorkspaceRoot : IDisposable
    {
        public TemporaryWorkspaceRoot()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"feat087-readiness-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void CreateClientArtifacts()
        {
            var directory = Path.Combine(RootPath, "hush-web-client", "public", "circuits", "omega-v1.0.0");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "reaction.wasm"), "wasm");
            File.WriteAllText(Path.Combine(directory, "reaction.zkey"), "zkey");
        }

        public void CreateServerVerificationKey()
        {
            var directory = Path.Combine(RootPath, "hush-server-node", "Node", "HushServerNode", "circuits", "omega-v1.0.0");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "verification_key.json"), "{}");
        }

        public void CreateClientPackageJsonWithSnarkJs()
        {
            var directory = Path.Combine(RootPath, "hush-web-client");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "package.json"),
                """
                {
                  "dependencies": {
                    "snarkjs": "^0.7.5"
                  }
                }
                """);
        }

        public void CreateInstalledSnarkJsPackage()
        {
            var directory = Path.Combine(RootPath, "hush-web-client", "node_modules", "snarkjs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.json"), """{ "name": "snarkjs" }""");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

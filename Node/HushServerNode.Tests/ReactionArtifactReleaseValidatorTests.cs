using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HushServerNode.Testing;
using Xunit;

namespace HushServerNode.Tests;

public sealed class ReactionArtifactReleaseValidatorTests
{
    [Fact]
    public void ValidateFromWorkspaceRoot_WhenManifestMissing_ShouldReportMissing()
    {
        using var temp = new TemporaryWorkspaceRoot();

        var result = ReactionArtifactReleaseValidator.ValidateFromWorkspaceRoot(temp.RootPath);

        result.IsValid.Should().BeFalse();
        result.Notes.Should().Be("Approved circuit artifact release manifest missing");
    }

    [Fact]
    public void ValidateFromWorkspaceRoot_WhenFilesMatchManifest_ShouldReportValid()
    {
        using var temp = new TemporaryWorkspaceRoot();
        temp.CreateArtifactsAndManifest();

        var result = ReactionArtifactReleaseValidator.ValidateFromWorkspaceRoot(temp.RootPath);

        result.IsValid.Should().BeTrue();
        result.Notes.Should().Be("Approved circuit artifact release manifest and installed files match");
    }

    [Fact]
    public void ValidateFromWorkspaceRoot_WhenChecksumDiffers_ShouldReportMismatch()
    {
        using var temp = new TemporaryWorkspaceRoot();
        temp.CreateArtifactsAndManifest();
        File.AppendAllText(
            Path.Combine(temp.RootPath, "hush-web-client", "public", "circuits", "omega-v1.0.0", "reaction.zkey"),
            "tampered");

        var result = ReactionArtifactReleaseValidator.ValidateFromWorkspaceRoot(temp.RootPath);

        result.IsValid.Should().BeFalse();
        result.Notes.Should().Be("SHA-256 mismatch for 'hush-web-client/public/circuits/omega-v1.0.0/reaction.zkey'");
    }

    private sealed class TemporaryWorkspaceRoot : IDisposable
    {
        public TemporaryWorkspaceRoot()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"feat087-artifacts-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void CreateArtifactsAndManifest()
        {
            var clientDirectory = Path.Combine(RootPath, "hush-web-client", "public", "circuits", "omega-v1.0.0");
            Directory.CreateDirectory(clientDirectory);
            var wasmPath = Path.Combine(clientDirectory, "reaction.wasm");
            var zkeyPath = Path.Combine(clientDirectory, "reaction.zkey");
            File.WriteAllText(wasmPath, "wasm-artifact");
            File.WriteAllText(zkeyPath, "zkey-artifact");

            var serverDirectory = Path.Combine(RootPath, "hush-server-node", "Node", "HushServerNode", "circuits", "omega-v1.0.0");
            Directory.CreateDirectory(serverDirectory);
            var verificationKeyPath = Path.Combine(serverDirectory, "verification_key.json");
            File.WriteAllText(verificationKeyPath, """{ "protocol": "groth16" }""");

            var manifestDirectory = Path.Combine(
                RootPath,
                "hush-memory-bank",
                "Features",
                "03_IN_PROGRESS",
                "FEAT-087-reactions-privacy-preserving-semantics");
            Directory.CreateDirectory(manifestDirectory);

            var manifest = new ReactionArtifactReleaseManifest(
                Version: "omega-v1.0.0",
                Provenance: "unit-test",
                TrustedSetup: "unit-test-ptau",
                GeneratedBy: "unit-test",
                Files:
                [
                    new ReactionArtifactReleaseFile("hush-web-client/public/circuits/omega-v1.0.0/reaction.wasm", ComputeSha256Hex(wasmPath)),
                    new ReactionArtifactReleaseFile("hush-web-client/public/circuits/omega-v1.0.0/reaction.zkey", ComputeSha256Hex(zkeyPath)),
                    new ReactionArtifactReleaseFile("hush-server-node/Node/HushServerNode/circuits/omega-v1.0.0/verification_key.json", ComputeSha256Hex(verificationKeyPath)),
                ]);

            File.WriteAllText(
                Path.Combine(manifestDirectory, "approved-circuit-artifact-release.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
    }
}

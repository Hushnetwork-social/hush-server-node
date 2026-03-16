using HushNode.Reactions.ZK;

namespace HushServerNode.Testing;

public static class ReactionProofPathInspector
{
    public static ReactionProofPathReadiness InspectFromCurrentRuntime(bool testHostDevModeEnabled)
    {
        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return InspectFromWorkspaceRoot(workspaceRoot, testHostDevModeEnabled);
    }

    public static ReactionProofPathReadiness InspectFromWorkspaceRoot(string workspaceRoot, bool testHostDevModeEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedRoot = Path.GetFullPath(workspaceRoot);
        var clientWasmPath = Path.Combine(normalizedRoot, "hush-web-client", "public", "circuits", "omega-v1.0.0", "reaction.wasm");
        var clientZkeyPath = Path.Combine(normalizedRoot, "hush-web-client", "public", "circuits", "omega-v1.0.0", "reaction.zkey");
        var clientPackageJsonPath = Path.Combine(normalizedRoot, "hush-web-client", "package.json");
        var clientInstalledSnarkJsPath = Path.Combine(normalizedRoot, "hush-web-client", "node_modules", "snarkjs", "package.json");
        var serverVerificationKeyPath = Path.Combine(normalizedRoot, "hush-server-node", "Node", "HushServerNode", "circuits", "omega-v1.0.0", "verification_key.json");

        var clientArtifactsAvailable = File.Exists(clientWasmPath) && File.Exists(clientZkeyPath);
        var clientHeadlessProverDependencyAvailable = HasSnarkJsDependency(clientPackageJsonPath, clientInstalledSnarkJsPath);
        var serverVerificationKeyAvailable = File.Exists(serverVerificationKeyPath);
        var serverVerificationKeyParsingImplemented = ReactionProofRuntimeCapabilities.ServerVerificationKeyParsingImplemented;
        var serverFullGroth16VerificationImplemented = ReactionProofRuntimeCapabilities.ServerFullGroth16VerificationImplemented;

        var notes = new List<string>();
        if (!clientArtifactsAvailable)
        {
            notes.Add("Client prover artifacts missing");
        }

        if (!clientHeadlessProverDependencyAvailable)
        {
            notes.Add("Client snarkjs dependency missing");
        }

        if (!serverVerificationKeyAvailable)
        {
            notes.Add("Server verification key missing");
        }

        if (!serverVerificationKeyParsingImplemented)
        {
            notes.Add("Server verification key parsing not implemented");
        }

        if (!serverFullGroth16VerificationImplemented)
        {
            notes.Add("Server full Groth16 verification not implemented");
        }

        if (testHostDevModeEnabled)
        {
            notes.Add("Integration test host currently runs with Reactions:DevMode=true");
        }

        var nonDevBenchmarkReady =
            clientArtifactsAvailable &&
            clientHeadlessProverDependencyAvailable &&
            serverVerificationKeyAvailable &&
            serverVerificationKeyParsingImplemented &&
            serverFullGroth16VerificationImplemented &&
            !testHostDevModeEnabled;
        if (notes.Count == 0)
        {
            notes.Add("Basic non-dev proof path prerequisites detected");
        }

        return new ReactionProofPathReadiness(
            NonDevBenchmarkReady: nonDevBenchmarkReady,
            ClientProverArtifactsAvailable: clientArtifactsAvailable,
            ClientHeadlessProverDependencyAvailable: clientHeadlessProverDependencyAvailable,
            ClientWasmPath: clientWasmPath,
            ClientZkeyPath: clientZkeyPath,
            ClientPackageJsonPath: clientPackageJsonPath,
            ServerVerificationKeyAvailable: serverVerificationKeyAvailable,
            ServerVerificationKeyParsingImplemented: serverVerificationKeyParsingImplemented,
            ServerFullGroth16VerificationImplemented: serverFullGroth16VerificationImplemented,
            ServerVerificationKeyPath: serverVerificationKeyPath,
            TestHostDevModeEnabled: testHostDevModeEnabled,
            Notes: string.Join("; ", notes));
    }

    private static bool HasSnarkJsDependency(string packageJsonPath, string installedPackageJsonPath)
    {
        if (!File.Exists(packageJsonPath))
        {
            return false;
        }

        var contents = File.ReadAllText(packageJsonPath);
        return contents.Contains("\"snarkjs\"", StringComparison.Ordinal) && File.Exists(installedPackageJsonPath);
    }
}

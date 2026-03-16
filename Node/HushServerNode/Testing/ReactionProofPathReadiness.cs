namespace HushServerNode.Testing;

public sealed record ReactionProofPathReadiness(
    bool NonDevBenchmarkReady,
    bool ClientProverArtifactsAvailable,
    bool ClientHeadlessProverDependencyAvailable,
    string ClientWasmPath,
    string ClientZkeyPath,
    string ClientPackageJsonPath,
    bool ServerVerificationKeyAvailable,
    bool ServerVerificationKeyParsingImplemented,
    bool ServerFullGroth16VerificationImplemented,
    string ServerVerificationKeyPath,
    bool TestHostDevModeEnabled,
    string Notes);

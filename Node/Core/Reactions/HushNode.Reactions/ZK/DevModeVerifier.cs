using Microsoft.Extensions.Logging;

namespace HushNode.Reactions.ZK;

/// <summary>
/// Development mode ZK verifier that always accepts proofs.
/// Used for testing the full reaction flow without real ZK circuits.
///
/// WARNING: This should NEVER be used in production!
/// </summary>
public class DevModeVerifier : IZkVerifier
{
    private readonly ILogger<DevModeVerifier> _logger;
    private const string DevVersion = "dev-mode-v1";

    public DevModeVerifier(ILogger<DevModeVerifier> logger)
    {
        _logger = logger;
        _logger.LogWarning("===========================================");
        _logger.LogWarning("DEV MODE ZK VERIFIER ACTIVE!");
        _logger.LogWarning("All ZK proofs will be accepted without verification.");
        _logger.LogWarning("This should NEVER be used in production!");
        _logger.LogWarning("===========================================");
    }

    public Task<VerifyResult> VerifyAsync(byte[] proof, PublicInputs inputs, string circuitVersion)
    {
        _logger.LogDebug(
            "[DevModeVerifier] Accepting proof for message {MessageId} (circuit: {Version})",
            BitConverter.ToString(inputs.MessageId).Replace("-", "")[..16],
            circuitVersion);

        // Always accept proofs in dev mode
        return Task.FromResult(VerifyResult.SuccessWithWarning(
            "DEV MODE: Proof accepted without verification. Do not use in production!"));
    }

    public string GetCurrentVersion() => DevVersion;

    public bool IsVersionSupported(string version) => true;

    public bool IsVulnerableVersion(string version) => false;
}

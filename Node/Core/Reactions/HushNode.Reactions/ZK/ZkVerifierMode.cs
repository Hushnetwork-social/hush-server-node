using Microsoft.Extensions.Configuration;

namespace HushNode.Reactions.ZK;

public enum ZkVerifierMode
{
    Real,
    Dev
}

public static class ZkVerifierModeResolver
{
    public static ZkVerifierMode Resolve(IConfiguration configuration)
    {
        var configuredMode = configuration["Reactions:VerifierMode"];
        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            return configuredMode.Trim().ToLowerInvariant() switch
            {
                "real" or "groth16" => ZkVerifierMode.Real,
                "dev" or "devmode" or "dev-mode" => ZkVerifierMode.Dev,
                _ => throw new InvalidOperationException(
                    $"Unsupported Reactions:VerifierMode '{configuredMode}'. Use 'real' or 'dev'.")
            };
        }

        // Legacy compatibility: older environments used a boolean toggle.
        var legacyDevMode = configuration.GetValue<bool>("Reactions:DevMode", false);
        return legacyDevMode ? ZkVerifierMode.Dev : ZkVerifierMode.Real;
    }
}

namespace HushNode.Reactions.ZK;

/// <summary>
/// Central capability declaration for FEAT-087 real non-dev proof execution.
/// Update these flags only when the corresponding runtime path is actually implemented.
/// </summary>
public static class ReactionProofRuntimeCapabilities
{
    public static bool ServerVerificationKeyParsingImplemented => true;

    public static bool ServerFullGroth16VerificationImplemented => true;

    public static string DescribeServerVerifierReadiness()
    {
        var blockers = new List<string>();

        if (!ServerVerificationKeyParsingImplemented)
        {
            blockers.Add("Server verification key parsing not implemented");
        }

        if (!ServerFullGroth16VerificationImplemented)
        {
            blockers.Add("Server full Groth16 verification not implemented");
        }

        return blockers.Count == 0
            ? "Server non-dev verifier path ready"
            : string.Join("; ", blockers);
    }
}

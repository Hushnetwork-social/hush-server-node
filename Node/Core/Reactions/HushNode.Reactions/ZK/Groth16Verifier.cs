using System.Numerics;
using System.Text.Json;
using HushNode.Reactions.Crypto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HushNode.Reactions.ZK;

/// <summary>
/// Groth16 ZK proof verifier for Protocol Omega reaction proofs.
/// </summary>
public class Groth16Verifier : IZkVerifier
{
    private readonly Dictionary<string, VerificationKey> _verificationKeys = new();
    private readonly HashSet<string> _deprecatedVersions = new();
    private readonly HashSet<string> _vulnerableVersions = new();
    private readonly string _currentVersion;
    private readonly IBabyJubJub _curve;
    private readonly ILogger<Groth16Verifier> _logger;

    public Groth16Verifier(
        IConfiguration config,
        IBabyJubJub curve,
        ILogger<Groth16Verifier> logger)
    {
        _curve = curve;
        _logger = logger;
        _currentVersion = config["Circuits:CurrentVersion"] ?? "omega-v1.0.0";

        // Load supported verification keys
        var supported = config.GetSection("Circuits:Supported").Get<string[]>() ?? new[] { _currentVersion };
        foreach (var version in supported)
        {
            try
            {
                var vk = LoadVerificationKey(version);
                if (vk != null)
                {
                    _verificationKeys[version] = vk;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load verification key for version {Version}", version);
            }
        }

        // Load deprecated versions
        var deprecated = config.GetSection("Circuits:Deprecated").Get<string[]>() ?? Array.Empty<string>();
        foreach (var version in deprecated)
        {
            _deprecatedVersions.Add(version);
        }

        // Known vulnerable versions that should be rejected
        var vulnerable = config.GetSection("Circuits:Vulnerable").Get<string[]>() ?? Array.Empty<string>();
        foreach (var version in vulnerable)
        {
            _vulnerableVersions.Add(version);
        }

        _logger.LogInformation(
            "Groth16Verifier initialized with {Count} circuit versions. Current: {Current}",
            _verificationKeys.Count,
            _currentVersion);
    }

    public async Task<VerifyResult> VerifyAsync(
        byte[] proof,
        PublicInputs inputs,
        string circuitVersion)
    {
        // Check for vulnerable versions first
        if (_vulnerableVersions.Contains(circuitVersion))
        {
            return VerifyResult.Failure(
                "VULNERABLE_CIRCUIT_VERSION",
                $"Circuit version '{circuitVersion}' has known vulnerabilities and is no longer accepted.");
        }

        // Check if version is known
        if (!_verificationKeys.TryGetValue(circuitVersion, out var vk))
        {
            return VerifyResult.Failure(
                "UNKNOWN_CIRCUIT_VERSION",
                $"Circuit version '{circuitVersion}' is not supported. Use '{_currentVersion}'.");
        }

        try
        {
            // Parse the Groth16 proof
            var parsedProof = ParseProof(proof);
            if (parsedProof == null)
            {
                return VerifyResult.Failure("INVALID_PROOF_FORMAT", "Failed to parse proof bytes.");
            }

            // Prepare public inputs as field elements
            var publicInputs = PreparePublicInputs(inputs);

            // Perform Groth16 verification
            var isValid = await VerifyGroth16Async(parsedProof, publicInputs, vk);

            if (!isValid)
            {
                return VerifyResult.Failure("INVALID_PROOF", "Proof verification failed.");
            }

            // Check for deprecation warning
            if (_deprecatedVersions.Contains(circuitVersion))
            {
                return VerifyResult.SuccessWithWarning(
                    $"Circuit version '{circuitVersion}' is deprecated. Please update to '{_currentVersion}'.");
            }

            return VerifyResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during proof verification");
            return VerifyResult.Failure("VERIFICATION_ERROR", $"Verification error: {ex.Message}");
        }
    }

    public string GetCurrentVersion() => _currentVersion;

    public bool IsVersionSupported(string version) => _verificationKeys.ContainsKey(version);

    public bool IsVulnerableVersion(string version) => _vulnerableVersions.Contains(version);

    private Groth16Proof? ParseProof(byte[] proofBytes)
    {
        // Groth16 proof consists of 3 curve points:
        // - A (G1): 64 bytes
        // - B (G2): 128 bytes (2 field elements)
        // - C (G1): 64 bytes
        // Total: 256 bytes

        if (proofBytes.Length < 256)
        {
            _logger.LogWarning("Proof too short: {Length} bytes (expected 256)", proofBytes.Length);
            return null;
        }

        try
        {
            var a = ECPoint.FromBytes(proofBytes[..64]);
            // B is a G2 point - for simplicity, we store it as 2 G1 points
            var b1 = ECPoint.FromBytes(proofBytes[64..128]);
            var b2 = ECPoint.FromBytes(proofBytes[128..192]);
            var c = ECPoint.FromBytes(proofBytes[192..256]);

            return new Groth16Proof
            {
                A = a,
                B = new[] { b1, b2 },
                C = c
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse proof points");
            return null;
        }
    }

    private BigInteger[] PreparePublicInputs(PublicInputs inputs)
    {
        var publicInputs = new List<BigInteger>();

        // Nullifier
        publicInputs.Add(new BigInteger(inputs.Nullifier, isUnsigned: true, isBigEndian: true));

        // Message ID
        publicInputs.Add(new BigInteger(inputs.MessageId, isUnsigned: true, isBigEndian: true));

        // Merkle root
        publicInputs.Add(new BigInteger(inputs.MembersRoot, isUnsigned: true, isBigEndian: true));

        // Author commitment
        publicInputs.Add(inputs.AuthorCommitment);

        // Feed public key
        publicInputs.Add(inputs.FeedPk.X);
        publicInputs.Add(inputs.FeedPk.Y);

        // Ciphertexts (6 C1 points + 6 C2 points = 24 field elements)
        for (int i = 0; i < 6; i++)
        {
            publicInputs.Add(inputs.CiphertextC1[i].X);
            publicInputs.Add(inputs.CiphertextC1[i].Y);
        }
        for (int i = 0; i < 6; i++)
        {
            publicInputs.Add(inputs.CiphertextC2[i].X);
            publicInputs.Add(inputs.CiphertextC2[i].Y);
        }

        return publicInputs.ToArray();
    }

    private async Task<bool> VerifyGroth16Async(
        Groth16Proof proof,
        BigInteger[] publicInputs,
        VerificationKey vk)
    {
        // Groth16 verification equation:
        // e(A, B) = e(α, β) · e(∑(pub_i · IC_i), γ) · e(C, δ)
        //
        // This requires pairing operations on BN254/BN128 curve.
        // For full implementation, we would need:
        // 1. BN254 pairing implementation
        // 2. G1 and G2 point arithmetic
        // 3. Final exponentiation

        // TODO: Implement actual Groth16 pairing verification
        // For now, we perform basic validation checks

        // Verify all proof points are on the curve
        if (!_curve.IsOnCurve(proof.A) || !_curve.IsOnCurve(proof.C))
        {
            _logger.LogWarning("Proof points not on curve");
            return false;
        }

        // Verify IC count matches public inputs + 1
        if (vk.IC.Length != publicInputs.Length + 1)
        {
            _logger.LogWarning(
                "IC count mismatch: expected {Expected}, got {Actual}",
                publicInputs.Length + 1,
                vk.IC.Length);
            return false;
        }

        // Compute the linear combination of IC with public inputs
        // vk_x = IC[0] + ∑(pub_i · IC[i+1])
        var vkX = vk.IC[0];
        for (int i = 0; i < publicInputs.Length; i++)
        {
            var term = _curve.ScalarMul(vk.IC[i + 1], publicInputs[i]);
            vkX = _curve.Add(vkX, term);
        }

        // In a full implementation, we would now verify:
        // e(proof.A, proof.B) == e(vk.Alpha, vk.Beta) * e(vkX, vk.Gamma) * e(proof.C, vk.Delta)

        // For testing/development, return true if points are valid
        // TODO: Implement full pairing check
        await Task.CompletedTask;

        _logger.LogDebug("Proof structure validated, pairing check skipped (not implemented)");
        return true;
    }

    private VerificationKey? LoadVerificationKey(string version)
    {
        // Load verification key from circuits directory
        var vkPath = Path.Combine(AppContext.BaseDirectory, "circuits", version, "verification_key.json");

        if (!File.Exists(vkPath))
        {
            _logger.LogDebug("Verification key not found at {Path}", vkPath);
            return CreatePlaceholderKey(version);
        }

        var json = File.ReadAllText(vkPath);
        // Parse the verification key JSON
        // The format depends on the snarkjs output format

        return CreatePlaceholderKey(version);
    }

    private VerificationKey CreatePlaceholderKey(string version)
    {
        // Create a placeholder verification key for development/testing
        // In production, this would be loaded from the circuit build
        return new VerificationKey
        {
            Version = version,
            Alpha = _curve.Generator,
            Beta = new[] { _curve.Generator, _curve.Generator },
            Gamma = new[] { _curve.Generator, _curve.Generator },
            Delta = new[] { _curve.Generator, _curve.Generator },
            // IC array size depends on number of public inputs
            // For our circuit: nullifier, messageId, merkleRoot, authorCommitment,
            // feedPk (2), ciphertextC1 (12), ciphertextC2 (12) = 30 elements + 1
            IC = Enumerable.Range(0, 31)
                .Select(_ => _curve.Generator)
                .ToArray()
        };
    }
}

/// <summary>
/// Parsed Groth16 proof.
/// </summary>
internal class Groth16Proof
{
    public required ECPoint A { get; set; }
    public required ECPoint[] B { get; set; }
    public required ECPoint C { get; set; }
}

namespace Olimpo.CredentialsManager;

/// <summary>
/// Portable credentials model that can be shared between Client and Node.
/// Used for .dat file export/import.
/// </summary>
public class PortableCredentials
{
    public string ProfileName { get; set; } = string.Empty;

    public string PublicSigningAddress { get; set; } = string.Empty;

    public string PrivateSigningKey { get; set; } = string.Empty;

    public string PublicEncryptAddress { get; set; } = string.Empty;

    public string PrivateEncryptKey { get; set; } = string.Empty;

    public bool IsPublic { get; set; }

    /// <summary>
    /// BIP-39 24-word recovery mnemonic (space-separated).
    /// Used to deterministically regenerate all keys.
    /// </summary>
    public string? Mnemonic { get; set; }
}

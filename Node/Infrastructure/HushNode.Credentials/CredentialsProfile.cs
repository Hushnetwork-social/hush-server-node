namespace HushNode.Credentials;

public sealed record CredentialsProfile
{
    // === Inline credentials (Option 1: direct in config) ===
    public string ProfileName { get; set; } = string.Empty;
    public string PublicSigningAddress { get; init; } = string.Empty;
    public string PrivateSigningKey { get; init; } = string.Empty;
    public string PublicEncryptAddress { get; init; } = string.Empty;
    public string PrivateEncryptKey { get; init; } = string.Empty;
    public bool IsPublic { get; set; }

    // === File-based credentials (Option 2: .dat file) ===
    /// <summary>
    /// Path to encrypted .dat credentials file.
    /// If set, credentials will be loaded from this file instead of inline values.
    /// </summary>
    public string? CredentialsFile { get; init; }

    /// <summary>
    /// Password for decrypting the .dat file.
    /// Can use environment variable syntax: ${ENV_VAR_NAME}
    /// If not set and CredentialsFile is specified, will prompt interactively.
    /// </summary>
    public string? CredentialsPassword { get; init; }

    /// <summary>
    /// Environment variable name containing the password.
    /// Alternative to CredentialsPassword for cleaner config.
    /// </summary>
    public string? CredentialsPasswordEnvVar { get; init; }

    /// <summary>
    /// Returns true if credentials should be loaded from a file.
    /// </summary>
    public bool UseFileBasedCredentials => !string.IsNullOrEmpty(CredentialsFile);
}
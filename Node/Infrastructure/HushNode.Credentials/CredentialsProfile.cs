namespace HushNode.Credentials;

public sealed record CredentialsProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public string PublicSigningAddress { get; init; } = string.Empty;
    public string PrivateSigningKey { get; init; } = string.Empty;
    public string PublicEncryptAddress { get; init; } = string.Empty;
    public string PrivateEncryptKey { get; init; } = string.Empty;
    public bool IsPublic { get; set; }
}
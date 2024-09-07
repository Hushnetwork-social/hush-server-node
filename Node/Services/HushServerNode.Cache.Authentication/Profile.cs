namespace HushServerNode.Cache.Authentication;

public class Profile
{
    public string PublicSigningAddress { get; set; } = string.Empty;

    public string PublicEncryptAddress { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public bool IsPublic { get; set; }
}

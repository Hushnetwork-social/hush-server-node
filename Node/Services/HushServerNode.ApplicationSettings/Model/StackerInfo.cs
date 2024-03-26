namespace HushServerNode.ApplicationSettings.Model;

public class StackerInfo 
{
    public string PublicSigningAddress { get; set; } = string.Empty;

    public string PrivateSigningKey { get; set; } = string.Empty;
    
    public string PublicEncryptAddress { get; set; } = string.Empty;
    
    public string PrivateEncryptKey { get; set; } = string.Empty;
}

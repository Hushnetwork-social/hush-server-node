namespace HushNode.InternalModules.Identity.Model;

public record Profile(
    string PublicSigningAddress, 
    string PublicEncryptionKey, 
    string Alias, 
    bool IsPublic);

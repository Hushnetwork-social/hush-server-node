namespace HushNode.Credentials;

public interface ICredentialsProvider
{
    CredentialsProfile GetCredentials();
}

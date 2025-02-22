using Microsoft.Extensions.Options;

namespace HushNode.Credentials;

public class CredentialsProvider : ICredentialsProvider
{
    private readonly CredentialsProfile _credentialsProfile;

    public CredentialsProvider(IOptions<CredentialsProfile> credentials)
    {
        this._credentialsProfile = credentials.Value;
    }

    public CredentialsProfile GetCredentials() => this._credentialsProfile;
}

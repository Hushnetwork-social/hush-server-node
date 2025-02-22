using System.Reactive.Subjects;
using Microsoft.Extensions.Options;
using Olimpo;

namespace HushNode.Credentials;

public class CredentialsBoostrapper : IBootstrapper
{
    private CredentialsProfile _credentials;

    // private readonly IOptions<CredentialsProfile> _credentials;

    public Subject<bool> BootstrapFinished { get; } = new Subject<bool>();

    public int Priority { get; set; } = 5;

    public CredentialsBoostrapper(IOptions<CredentialsProfile> credentials)
    {
        this._credentials = credentials.Value;
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this.BootstrapFinished.OnNext(true);
        return Task.CompletedTask;
    }
}

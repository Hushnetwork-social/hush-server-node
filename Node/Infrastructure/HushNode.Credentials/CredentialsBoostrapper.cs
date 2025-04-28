using System.Reactive.Subjects;
using Microsoft.Extensions.Options;
using Olimpo;

namespace HushNode.Credentials;

public class CredentialsBoostrapper(IOptions<CredentialsProfile> credentials) : IBootstrapper
{
    private readonly CredentialsProfile _credentials = credentials.Value;

    public Subject<string> BootstrapFinished { get; } = new Subject<string>();

    public int Priority { get; set; } = 5;

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this.BootstrapFinished.OnNext("Credentials");
        return Task.CompletedTask;
    }
}

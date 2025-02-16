using System.Reactive.Subjects;
using Olimpo;

namespace HushServerNode.InternalModule.Bank;

public class BankBootstrapper : IBootstrapper
{
    public Subject<bool> BootstrapFinished { get; }
    public int Priority { get; set; } = 10;

    public BankBootstrapper()
    {
        this.BootstrapFinished = new Subject<bool>();
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

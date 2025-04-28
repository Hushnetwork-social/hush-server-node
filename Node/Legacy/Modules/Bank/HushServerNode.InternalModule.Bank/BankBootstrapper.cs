using System.Reactive.Subjects;
using Olimpo;

namespace HushServerNode.InternalModule.Bank;

public class BankBootstrapper : IBootstrapper
{
    public Subject<string> BootstrapFinished { get; }
    public int Priority { get; set; } = 10;

    public BankBootstrapper()
    {
        this.BootstrapFinished = new Subject<string>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        this.BootstrapFinished.OnNext("Bank");
        return Task.CompletedTask;
    }
}

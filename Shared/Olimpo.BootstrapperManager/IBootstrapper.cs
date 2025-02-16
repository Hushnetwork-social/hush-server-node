using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace Olimpo;

public interface IBootstrapper
{
    Subject<bool> BootstrapFinished { get; }

    int Priority { get; set; }

    Task Startup();

    void Shutdown();
}

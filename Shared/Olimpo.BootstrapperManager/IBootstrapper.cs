using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace Olimpo;

public interface IBootstrapper
{
    Subject<string> BootstrapFinished { get; }

    int Priority { get; set; }

    Task Startup();

    void Shutdown();
}

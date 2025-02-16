using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace Olimpo;

public interface IBootstrapperManager
{
    Subject<bool> AllModulesBootstrapped { get; }

    Subject<bool> ModuleBootstrapped { get; }

    int ModulesCount { get; }

    public Task Start();
}
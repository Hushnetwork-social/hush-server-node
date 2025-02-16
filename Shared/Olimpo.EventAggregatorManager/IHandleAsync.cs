using System.Threading.Tasks;

namespace Olimpo;

public interface IHandleAsync<T>
{
    Task HandleAsync(T message);
}

using System.Threading.Tasks;

namespace Olimpo;

public interface IEventAggregator
{
    void Subscribe(object subscriber);

    void Unsubscribe(object subscriber);

    void Unsubscribe<T>() where T : class;

    Task PublishAsync<T>(T message) where T : class;
}
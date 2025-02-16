namespace Olimpo;

public interface IHandle<T>
{
    void Handle(T message);
}

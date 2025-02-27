namespace Olimpo.EntityFramework.Persistency;

public interface IRepository { }

public interface IRepositoryWithContext<TContext>
{
    void SetContext(TContext context);
}

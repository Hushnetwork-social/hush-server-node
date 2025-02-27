namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IRepository { }

public interface IRepositoryWithContext<TContext>
{
    void SetContext(TContext context);
}

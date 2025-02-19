using Microsoft.EntityFrameworkCore;

namespace HushNode.Intefaces;

public interface IDbContextConfigurator
{
    void Configure(ModelBuilder modelBuilder);
}

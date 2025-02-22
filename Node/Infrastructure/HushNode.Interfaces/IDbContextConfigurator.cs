using Microsoft.EntityFrameworkCore;

namespace HushNode.Interfaces;

public interface IDbContextConfigurator
{
    void Configure(ModelBuilder modelBuilder);
}

using Microsoft.EntityFrameworkCore;

namespace HushServerNode.Interfaces;

public interface IDbContextConfigurator
{
    void Configure(ModelBuilder modelBuilder);    
}

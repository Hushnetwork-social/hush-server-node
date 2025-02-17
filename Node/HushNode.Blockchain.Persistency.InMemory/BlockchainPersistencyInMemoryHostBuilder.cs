using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HushNode.Blockchain.Persistency.InMemory;

public static class BlockchainPersistencyInMemoryHostBuilder
{
    public static IHostBuilder RegisterInMemoryPersistency(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
            services.AddDbContext<BlockchainDbContext>(options =>
            {
                options.UseInMemoryDatabase("HushNetworkDb");
            });

            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
        });

        return builder;
    }
}

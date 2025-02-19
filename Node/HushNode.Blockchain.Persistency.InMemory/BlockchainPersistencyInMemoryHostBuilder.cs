using HushNode.Blockchain.Persistency.EntityFramework;
using HushNode.Intefaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
                options
                    .UseInMemoryDatabase("HushNetworkDb")
                    .ConfigureWarnings(warnings => 
                        warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });
        });

        return builder;
    }
}

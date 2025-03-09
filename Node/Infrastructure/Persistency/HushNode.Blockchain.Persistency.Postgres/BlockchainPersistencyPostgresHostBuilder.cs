// using HushNode.Blockchain.Persistency.EntityFramework;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;

// namespace HushNode.Blockchain.Persistency.Postgres;

// public static class BlockchainPersistencyPostgresHostBuilder
// {
//     public static IHostBuilder RegisterPostgresPersistency(this IHostBuilder builder)
//     {
//         builder.ConfigureServices((hostContext, services) => 
//         {
//             services.AddDbContext<BlockchainDbContext>(options =>
//             {
//                 options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
//             });
//         });

//         return builder;
//     }
// }

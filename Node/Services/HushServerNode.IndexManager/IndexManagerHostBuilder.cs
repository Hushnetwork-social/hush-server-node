using Microsoft.Extensions.Hosting;

namespace HushServerNode.IndexManager;

public static class IndexManagerHostBuilder
{
    public static IHostBuilder RegisterIndexManager(this IHostBuilder builder)
    {
        builder.ConfigureServices((hostContext, services) => 
        {
        
        });

        return builder;
    }
}

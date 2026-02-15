using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public static class StorageHostBuild
{
    public static void RegisterFeedsStorageServices(this IServiceCollection services, HostBuilderContext hostContext)
    {
        services.AddDbContext<FeedsDbContext>((provider, options) =>
        {
            options.UseNpgsql(hostContext.Configuration.GetConnectionString("HushNetworkDb"));
            options.EnableSensitiveDataLogging();  // For debugging
            options.EnableDetailedErrors();  // For debugging
        });

        services.AddTransient<IUnitOfWorkProvider<FeedsDbContext>, UnitOfWorkProvider<FeedsDbContext>>();

        services.AddSingleton<IFeedsStorageService, FeedsStorageService>();
        services.AddSingleton<IFeedMessageStorageService, FeedMessageStorageService>();
        services.AddSingleton<IFeedReadPositionStorageService, FeedReadPositionStorageService>();
        services.AddTransient<IFeedsRepository, FeedsRepository>();
        services.AddTransient<IFeedMessageRepository, FeedMessageRepository>();
        services.AddTransient<IGroupFeedMemberCommitmentRepository, GroupFeedMemberCommitmentRepository>();
        services.AddTransient<IFeedReadPositionRepository, FeedReadPositionRepository>();

        // FEAT-066: Attachment storage services
        services.AddTransient<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IAttachmentStorageService, AttachmentStorageService>();
        services.AddSingleton<IAttachmentTempStorageService>(sp =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "hush-attachment-temp");
            var logger = sp.GetRequiredService<ILogger<AttachmentTempStorageService>>();
            return new AttachmentTempStorageService(tempDir, logger);
        });

        services.AddTransient<IDbContextConfigurator, FeedsDbContextConfigurator>();
        services.AddTransient<FeedsDbContextConfigurator>();
    }
}

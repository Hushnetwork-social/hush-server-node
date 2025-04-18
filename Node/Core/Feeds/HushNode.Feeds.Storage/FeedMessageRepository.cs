using HushShared.Feeds.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public class FeedMessageRepository : RepositoryBase<FeedsDbContext>, IFeedMessageRepository
{
    public async Task CreateFeedMessage(FeedMessage feedMessage) => 
        await this.Context.FeedMessages.AddAsync(feedMessage);
}
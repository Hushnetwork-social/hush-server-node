using HushShared.Feeds.Model;
using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public class FeedsRepository : RepositoryBase<FeedsDbContext>, IFeedsRepository
{
    public async Task<bool> HasPersonalFeed(string publicSigningAddress) => 
        await this.Context.FeedParticipants
            .AnyAsync(x => 
                x.ParticipantPublicAddress == publicSigningAddress &&
                x.ParticipantType == HushShared.Feeds.Model.ParticipantType.Owner &&
                x.Feed.FeedType == HushShared.Feeds.Model.FeedType.Personal);

    public async Task CreateFeed(Feed feed) => 
        await  this.Context.Feeds.AddAsync(feed);
}

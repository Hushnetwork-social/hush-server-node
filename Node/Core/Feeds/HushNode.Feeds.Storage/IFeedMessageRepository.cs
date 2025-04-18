using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageRepository : IRepository
{
    Task CreateFeedMessage(FeedMessage feedMessage);
}

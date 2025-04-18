using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageStorageService
{
    Task CreateFeedMessage(FeedMessage feedMessage);
}

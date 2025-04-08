namespace HushNode.Feeds.Storage;

public interface IFeedsStorageService
{
    Task<bool> HasPersonalFeed(string publicSigningAddress);
}
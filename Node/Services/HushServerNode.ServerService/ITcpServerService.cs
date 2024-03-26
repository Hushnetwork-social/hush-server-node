namespace HushServerNode.ServerService;

public interface ITcpServerService
{
    void SendThroughChannel(string channelId, string message);
}
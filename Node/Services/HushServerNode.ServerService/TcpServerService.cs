using System;
using System.Collections.Generic;
using System.Linq;
using HushEcosystem;
using HushEcosystem.Model.Rpc.CommandDeserializeStrategies;
using Olimpo;

namespace HushServerNode.ServerService;

public class TcpServerService: ITcpServerService
{
    private readonly IServer _server;
    private readonly IEnumerable<ICommandDeserializeStrategy> _strategies;

    public TcpServerService(
        IServer server,
        IEnumerable<ICommandDeserializeStrategy> strategies)
    {
        this._server = server;
        this._strategies = strategies;

        this._server.DataReceived.Subscribe(OnDataReceived);
    }

    public void SendThroughChannel(string channelId, string message)
    {
        var channel = this._server.ConnectedChannels.OpenChannels
            .Single(x => x.Key == channelId).Value;
        
        channel.Send(message);
    }

    private void OnDataReceived(DataReceivedArgs args)
    {
        var decompressedMessage = args.Message.Decompress();

        var commandStrategy = this._strategies.SingleOrDefault(x => x.CanHandle(decompressedMessage));
        if (commandStrategy == null)
        {
            throw new InvalidOperationException($"There is no strategy for the command: : {decompressedMessage}");
        }
        
        commandStrategy.Handle(decompressedMessage, args.ChannelId);
    }
}

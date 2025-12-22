using Google.Protobuf;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Reactions.Storage;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Reactions.gRPC;

/// <summary>
/// gRPC service for anonymous reactions (read-only operations).
///
/// NOTE: Reaction submission goes through BlockchainGrpcService.SubmitSignedTransaction.
/// This service only provides read-only query endpoints.
/// </summary>
public class ReactionsGrpcService : HushReactions.HushReactionsBase
{
    private readonly IReactionService _reactionService;
    private readonly ILogger<ReactionsGrpcService> _logger;

    public ReactionsGrpcService(
        IReactionService reactionService,
        ILogger<ReactionsGrpcService> logger)
    {
        _reactionService = reactionService;
        _logger = logger;
    }

    public override async Task<GetTalliesResponse> GetReactionTallies(
        GetTalliesRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.FeedId.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feed_id length"));

            var feedId = new FeedId(new Guid(request.FeedId.ToByteArray()));
            var messageIds = request.MessageIds
                .Select(id => new FeedMessageId(new Guid(id.ToByteArray())))
                .ToList();

            var tallies = await _reactionService.GetTalliesAsync(feedId, messageIds);

            var response = new GetTalliesResponse();
            foreach (var tally in tallies)
            {
                var messageTally = new MessageTally
                {
                    MessageId = ByteString.CopyFrom(tally.MessageId.Value.ToByteArray()),
                    TotalCount = tally.TotalCount
                };

                // Add C1 points
                for (int i = 0; i < 6; i++)
                {
                    messageTally.TallyC1.Add(new HushNetwork.proto.ECPoint
                    {
                        X = ByteString.CopyFrom(tally.TallyC1X[i]),
                        Y = ByteString.CopyFrom(tally.TallyC1Y[i])
                    });
                }

                // Add C2 points
                for (int i = 0; i < 6; i++)
                {
                    messageTally.TallyC2.Add(new HushNetwork.proto.ECPoint
                    {
                        X = ByteString.CopyFrom(tally.TallyC2X[i]),
                        Y = ByteString.CopyFrom(tally.TallyC2Y[i])
                    });
                }

                response.Tallies.Add(messageTally);
            }

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetReactionTallies");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }

    public override async Task<NullifierExistsResponse> NullifierExists(
        NullifierExistsRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.Nullifier.Length != 32)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid nullifier length"));

            var exists = await _reactionService.NullifierExistsAsync(request.Nullifier.ToByteArray());

            return new NullifierExistsResponse { Exists = exists };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NullifierExists");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }

    public override async Task<GetReactionBackupResponse> GetReactionBackup(
        GetReactionBackupRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.Nullifier.Length != 32)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid nullifier length"));

            var backup = await _reactionService.GetReactionBackupAsync(request.Nullifier.ToByteArray());

            return new GetReactionBackupResponse
            {
                Exists = backup != null,
                EncryptedEmojiBackup = backup != null
                    ? ByteString.CopyFrom(backup)
                    : ByteString.Empty
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetReactionBackup");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }
}

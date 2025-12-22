using Google.Protobuf;
using Grpc.Core;
using HushNetwork.proto;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using HushNode.Reactions.Storage;

namespace HushNode.Reactions.gRPC;

/// <summary>
/// gRPC service for feed membership operations.
/// Delegates to IMembershipService for Merkle proof generation and commitment management.
/// </summary>
public class MembershipGrpcService : HushMembership.HushMembershipBase
{
    private readonly IMembershipService _membershipService;
    private readonly ILogger<MembershipGrpcService> _logger;

    public MembershipGrpcService(
        IMembershipService membershipService,
        ILogger<MembershipGrpcService> logger)
    {
        _membershipService = membershipService;
        _logger = logger;
    }

    public override async Task<GetMembershipProofResponse> GetMembershipProof(
        GetMembershipProofRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.FeedId.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feed_id length"));

            if (request.UserCommitment.Length != 32)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_commitment length"));

            var feedId = new FeedId(new Guid(request.FeedId.ToByteArray()));
            var result = await _membershipService.GetMembershipProofAsync(
                feedId,
                request.UserCommitment.ToByteArray());

            var response = new GetMembershipProofResponse
            {
                IsMember = result.IsMember,
                TreeDepth = result.TreeDepth,
                RootBlockHeight = result.RootBlockHeight
            };

            if (result.IsMember && result.MerkleRoot != null)
            {
                response.MerkleRoot = ByteString.CopyFrom(result.MerkleRoot);

                if (result.PathElements != null)
                {
                    foreach (var element in result.PathElements)
                    {
                        response.PathElements.Add(ByteString.CopyFrom(element));
                    }
                }

                if (result.PathIndices != null)
                {
                    foreach (var index in result.PathIndices)
                    {
                        response.PathIndices.Add(index == 1);  // Convert 0/1 to bool
                    }
                }
            }

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetMembershipProof");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }

    public override async Task<GetRecentRootsResponse> GetRecentMerkleRoots(
        GetRecentRootsRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.FeedId.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feed_id length"));

            var feedId = new FeedId(new Guid(request.FeedId.ToByteArray()));
            var count = Math.Clamp(request.Count, 1, 10);

            var roots = await _membershipService.GetRecentRootsAsync(feedId, count);

            var response = new GetRecentRootsResponse();
            foreach (var root in roots)
            {
                response.Roots.Add(new MerkleRootInfo
                {
                    Root = ByteString.CopyFrom(root.MerkleRoot),
                    BlockHeight = root.BlockHeight,
                    Timestamp = new DateTimeOffset(root.CreatedAt).ToUnixTimeMilliseconds()
                });
            }

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetRecentMerkleRoots");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }

    public override async Task<RegisterCommitmentResponse> RegisterCommitment(
        RegisterCommitmentRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.FeedId.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feed_id length"));

            if (request.UserCommitment.Length != 32)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_commitment length"));

            var feedId = new FeedId(new Guid(request.FeedId.ToByteArray()));
            var result = await _membershipService.RegisterCommitmentAsync(
                feedId,
                request.UserCommitment.ToByteArray());

            return new RegisterCommitmentResponse
            {
                Success = result.Success,
                AlreadyRegistered = result.AlreadyRegistered,
                NewMerkleRoot = result.MerkleRoot != null
                    ? ByteString.CopyFrom(result.MerkleRoot)
                    : ByteString.Empty
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RegisterCommitment");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }

    public override async Task<IsCommitmentRegisteredResponse> IsCommitmentRegistered(
        IsCommitmentRegisteredRequest request,
        ServerCallContext context)
    {
        try
        {
            if (request.FeedId.Length != 16)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid feed_id length"));

            if (request.UserCommitment.Length != 32)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_commitment length"));

            var feedId = new FeedId(new Guid(request.FeedId.ToByteArray()));
            var isRegistered = await _membershipService.IsCommitmentRegisteredAsync(
                feedId,
                request.UserCommitment.ToByteArray());

            return new IsCommitmentRegisteredResponse { IsRegistered = isRegistered };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in IsCommitmentRegistered");
            throw new RpcException(new Status(StatusCode.Internal, $"Error: {ex.Message}"));
        }
    }
}

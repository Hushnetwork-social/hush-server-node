// FEAT-015 Task 6.1 — GetMyEntitlement gRPC service.
//
// Authenticates the signed actor-bound metadata BEFORE any identity/entitlement lookup,
// then resolves indexed truth through the dependency-safe query port. Expected states are
// typed data; infrastructure unavailability maps to UNAVAILABLE (never no-active). The
// service is strictly read-only and never provisions or writes licence state.

using Grpc.Core;
using HushNetwork.proto;
using HushNode.HushVoting.Licence.Transactions;

namespace HushNode.HushVoting.Licence.gRPC;

public sealed class HushVotingLicenceGrpcService(
    ILicenceEntitlementQueryApplicationService queryApplicationService)
    : HushVotingLicence.HushVotingLicenceBase
{
    private const string MethodName = "GetMyEntitlement";

    private readonly ILicenceEntitlementQueryApplicationService _queryApplicationService =
        queryApplicationService;

    public override async Task<GetMyEntitlementResponse> GetMyEntitlement(
        GetMyEntitlementRequest request,
        ServerCallContext context)
    {
        // Authentication precedes any identity or entitlement lookup.
        var canonicalActor = LicenceQueryRequestAuthValidator.ValidateOrResolveActor(
            MethodName,
            context);

        var result = await _queryApplicationService.GetMyEntitlementAsync(
            canonicalActor,
            context.CancellationToken);

        if (result.State == HushVotingLicenceEntitlementQueryState.Unavailable)
        {
            throw new RpcException(new Status(
                StatusCode.Unavailable,
                result.UnavailableCode ?? "licence_index_unavailable"));
        }

        return LicenceQueryResponseMappings.ToProto(result);
    }
}

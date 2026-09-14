using Grpc.Core;
using Grpc.Core.Interceptors;
using Google.Protobuf;
using System.Collections.Concurrent;
using HushNetwork.proto;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Test-owned server transport faults; never fabricates a successful response.</summary>
internal sealed class HushVotingFaultInterceptor : Interceptor
{
    // Negative protocol qualification after the real authenticated query has
    // returned Active. Closed fault choices cannot manufacture successful admission.
    public enum EntitlementCorruption { UnknownPlan, UnknownFamily, UnknownGovernance, CriticalVersion }
    public EntitlementCorruption? CorruptActiveEntitlement { get; set; }
    public ConcurrentQueue<GetMyEntitlementResponse> CorruptedEntitlements { get; } = new();
    // One-shot negative reply faults run after ordinary server handling and
    // capture of its original response. Never used to fabricate admission.
    public Action<SubmitSignedTransactionReply>? AlterNextSubmissionReply;
    public bool IdentityUnavailable { get; set; }
    public bool EntitlementUnavailable { get; set; }
    public bool CorruptNextTransactionSignature { get; set; }
    public bool DropNextSubmissionResponse { get; set; }
    public int DroppedSubmissionResponses { get; private set; }
    public bool TransportUnavailable { get; set; }
    public bool HoldMempoolDrain { get; set; }
    public int RejectIdentityQueryNumber { get; set; }
    public int StallIdentityQueryNumber { get; set; }
    public TimeSpan IdentityResponseDelay { get; set; }
    public int IdentityQueryCount;
    public ConcurrentQueue<long> IdentityQueryStartedTicks { get; } = new();
    public ConcurrentQueue<bool> IdentityQueriesUnsigned { get; } = new();
    public int RejectedIdentityQueries { get; private set; }
    public ConcurrentQueue<(string SigningAddress, GetIdentityReply Reply)> IdentityLookups { get; } = new();
    public ConcurrentQueue<string> Outcomes { get; } = new();
    public ConcurrentQueue<string> RequestMethods { get; } = new();
    public ConcurrentQueue<string> SubmittedTransactions { get; } = new();
    public ConcurrentQueue<GetMyEntitlementResponse> Entitlements { get; } = new();
    public ConcurrentQueue<SubmitSignedTransactionReply> Submissions { get; } = new();
    private TaskCompletionSource? _identityGate;
    public TaskCompletionSource IdentityQueryArrived { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void HoldIdentityQueries()
    {
        IdentityQueryArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _identityGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void ReleaseIdentityQueries() => Interlocked.Exchange(ref _identityGate, null)?.TrySetResult();

    private TaskCompletionSource? _entitlementGate;
    public TaskCompletionSource EntitlementArrived { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int EntitlementRequests;
    public void HoldEntitlementQueries()
    {
        EntitlementArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _entitlementGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void ReleaseEntitlementQueries() => Interlocked.Exchange(ref _entitlementGate, null)?.TrySetResult();

    private int _holdNextSubmission;
    private TaskCompletionSource? _submissionGate;
    public TaskCompletionSource SubmissionArrived { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void HoldNextSubmission()
    {
        SubmissionArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _submissionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _holdNextSubmission, 1);
    }
    public void ReleaseSubmission() => Interlocked.Exchange(ref _submissionGate, null)?.TrySetResult();

    private TaskCompletionSource? _submissionBatchGate;
    private int _submissionBatchSize;
    private int _submissionBatchCount;
    public TaskCompletionSource SubmissionBatchArrived { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void HoldSubmissionBatch(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
        _submissionBatchSize = count;
        _submissionBatchCount = 0;
        SubmissionArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SubmissionBatchArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _submissionBatchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void ReleaseSubmissionBatch() => Interlocked.Exchange(ref _submissionBatchGate, null)?.TrySetResult();

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var identityQuery = context.Method.EndsWith("/GetIdentity", StringComparison.Ordinal);
        var identityQueryNumber = identityQuery ? Interlocked.Increment(ref IdentityQueryCount) : 0;
        if (identityQuery) IdentityQueryStartedTicks.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());
        if (identityQuery)
        {
            var gate = _identityGate;
            IdentityQueryArrived.TrySetResult();
            if (gate is not null) await gate.Task.WaitAsync(context.CancellationToken);
        }
        if (identityQuery) IdentityQueriesUnsigned.Enqueue(!context.RequestHeaders.Any(header =>
            header.Key.Contains("signature", StringComparison.OrdinalIgnoreCase) || header.Key.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
            || header.Key.Contains("signing", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase)));
        if (identityQuery && IdentityResponseDelay > TimeSpan.Zero)
            await Task.Delay(IdentityResponseDelay, context.CancellationToken);
        if (identityQuery && identityQueryNumber == StallIdentityQueryNumber)
            await Task.Delay(TimeSpan.FromSeconds(30), context.CancellationToken);
        if (identityQuery && identityQueryNumber == RejectIdentityQueryNumber)
        {
            RejectedIdentityQueries++;
            throw new RpcException(new Status(StatusCode.Unavailable, "Controlled HushVoting candidate lookup outage"));
        }
        if (TransportUnavailable || (IdentityUnavailable && identityQuery) || (EntitlementUnavailable && context.Method.EndsWith("/GetMyEntitlement", StringComparison.Ordinal)))
        {
            if (context.Method.EndsWith("/GetIdentity", StringComparison.Ordinal)) RejectedIdentityQueries++;
            throw new RpcException(new Status(StatusCode.Unavailable, "Controlled HushVoting test outage"));
        }
        var method = context.Method.Split('/')[^1];
        RequestMethods.Enqueue(method);
        if (method == "GetMyEntitlement")
        {
            Interlocked.Increment(ref EntitlementRequests);
            var gate = _entitlementGate;
            EntitlementArrived.TrySetResult();
            if (gate is not null) await gate.Task.WaitAsync(context.CancellationToken);
        }
        if (method == "SubmitSignedTransaction" && request is IMessage submitted)
        {
            var field = submitted.Descriptor.Fields.InDeclarationOrder()
                .Single(f => f.JsonName.Equals("signedTransaction", StringComparison.OrdinalIgnoreCase));
            var transactionJson = (string)field.Accessor.GetValue(submitted);
            SubmittedTransactions.Enqueue(transactionJson);
            var batchGate = _submissionBatchGate;
            if (batchGate is not null)
            {
                SubmissionArrived.TrySetResult();
                if (Interlocked.Increment(ref _submissionBatchCount) == _submissionBatchSize)
                    SubmissionBatchArrived.TrySetResult();
                await batchGate.Task.WaitAsync(context.CancellationToken);
            }
            if (Interlocked.Exchange(ref _holdNextSubmission, 0) == 1)
            {
                var gate = _submissionGate;
                SubmissionArrived.TrySetResult();
                if (gate is not null) await gate.Task.WaitAsync(context.CancellationToken);
            }
            if (CorruptNextTransactionSignature)
            {
                CorruptNextTransactionSignature = false;
                var transaction = System.Text.Json.Nodes.JsonNode.Parse(transactionJson)!;
                var signature = Convert.FromBase64String(transaction["UserSignature"]!["Signature"]!.GetValue<string>());
                signature[0] ^= 1;
                transaction["UserSignature"]!["Signature"] = Convert.ToBase64String(signature);
                field.Accessor.SetValue(submitted, transaction.ToJsonString());
            }
        }
        try
        {
            var response = await continuation(request, context);
            if (response is IMessage message)
            {
                if (method == "GetIdentity" && request is IMessage identityRequest)
                {
                    var addressField = identityRequest.Descriptor.Fields.InDeclarationOrder().Single();
                    IdentityLookups.Enqueue(((string)addressField.Accessor.GetValue(identityRequest),
                        JsonParser.Default.Parse<GetIdentityReply>(JsonFormatter.Default.Format(message))));
                }
                if (method == "GetMyEntitlement")
                    Entitlements.Enqueue(JsonParser.Default.Parse<GetMyEntitlementResponse>(JsonFormatter.Default.Format(message)));
                if (method == "SubmitSignedTransaction")
                    Submissions.Enqueue(JsonParser.Default.Parse<SubmitSignedTransactionReply>(JsonFormatter.Default.Format(message)));
                var states = message.Descriptor.Fields.InDeclarationOrder()
                    .Where(f => f.JsonName is "state" or "status" or "unavailableCode" or "successfull")
                    .Select(f => $"{f.JsonName}={f.Accessor.GetValue(message)}");
                Outcomes.Enqueue($"{method}({string.Join(',', states)})");
            }
            if (method == "GetMyEntitlement" && response is IMessage entitlementReply
                && CorruptActiveEntitlement is { } corruption)
            {
                var changed = JsonParser.Default.Parse<GetMyEntitlementResponse>(JsonFormatter.Default.Format(entitlementReply));
                if (changed.State != LicenceEntitlementState.Active || changed.Active is null || changed.DirectFreeTemplate is not null)
                    throw new InvalidOperationException("Entitlement corruption requires a real indexed Active response.");
                switch (corruption)
                {
                    case EntitlementCorruption.UnknownPlan:
                        changed.Active.PlanId = "unsupported-test-plan";
                        break;
                    case EntitlementCorruption.UnknownFamily:
                        changed.Active.PlanFamily = "unsupported-test-family";
                        break;
                    case EntitlementCorruption.UnknownGovernance:
                        changed.Active.AllowedGovernanceOptionIds.Add("unsupported-test-governance");
                        break;
                    case EntitlementCorruption.CriticalVersion:
                        changed.Active.AssignedCatalogueVersion = "unsupported-test-catalogue";
                        break;
                    default:
                        throw new InvalidOperationException("Unknown entitlement corruption selection.");
                }
                CorruptedEntitlements.Enqueue(changed.Clone());
                response = (TResponse)JsonParser.Default.Parse(JsonFormatter.Default.Format(changed), entitlementReply.Descriptor);
            }
            if (method == "SubmitSignedTransaction" && response is IMessage wireReply)
            {
                var alter = Interlocked.Exchange(ref AlterNextSubmissionReply, null);
                if (alter is not null)
                {
                    // Server and test assemblies compile their own protobuf
                    // CLR types; use the actual response descriptor at return.
                    var changed = JsonParser.Default.Parse<SubmitSignedTransactionReply>(JsonFormatter.Default.Format(wireReply));
                    alter(changed);
                    response = (TResponse)JsonParser.Default.Parse(JsonFormatter.Default.Format(changed), wireReply.Descriptor);
                }
            }
            if (method == "SubmitSignedTransaction" && DropNextSubmissionResponse)
            {
                DropNextSubmissionResponse = false;
                DroppedSubmissionResponses++;
                // Run ordinary admission first, then lose only its transport reply.
                throw new RpcException(new Status(StatusCode.Unavailable, "Controlled loss of submission response"));
            }
            return response;
        }
        catch (RpcException error)
        {
            Outcomes.Enqueue($"{method}({error.StatusCode})");
            throw;
        }
    }
}

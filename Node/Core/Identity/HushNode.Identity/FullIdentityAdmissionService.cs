// FEAT-011 Task 3.3 — atomic FullIdentity admission with one-signing-identity
// reservation (embedded single-node model).
//
// Admission order (frozen in transition-fault-matrix S1–S8):
//   1. canonical content validation (typed outcomes only);
//   2. indexed-truth check (exact signing address in storage);
//   3. atomic in-process reservation keyed by the signing identity;
//      - first valid registration      -> ACCEPTED (one mempool item);
//      - exact same transaction retry  -> PENDING (no second mempool item);
//      - different transaction, same signing identity pending -> CONFLICT;
//   4. indexed identity                 -> ALREADY_EXISTS (no admission).
//
// Restart semantics: the reservation registry is in-memory (the embedded node
// is single-process); after a restart the mempool is empty and a resubmitted
// exact transaction is re-admitted once — storage-level dedupe (typed
// unique-violation handling in the indexer) guarantees one final profile.

using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Olimpo.EntityFramework.Persistency;
using System.Collections.Concurrent;

namespace HushNode.Identity;

/// <summary>One reserved admission keyed by the exact signing identity.</summary>
internal sealed record ReservationEntry(string TransactionId, string TransactionDigest);

public sealed class FullIdentityAdmissionService(
    IFullIdentityValidator validator,
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider) : IFullIdentityReservationService, IFullIdentityAdmissionService
{
    private readonly IFullIdentityValidator _validator = validator;
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ConcurrentDictionary<string, ReservationEntry> _reservations = new(StringComparer.Ordinal);

    /// <summary>Full orchestrated admission (validate -> indexed check -> reserve).</summary>
    public async Task<FullIdentityReservationResult> AdmitAsync(
        AbstractTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not SignedTransaction<FullIdentityPayload> fullIdentity)
        {
            return FullIdentityReservationResult.Rejected(FullIdentityValidationCodes.UnsupportedKind);
        }

        return await AdmitAsync(fullIdentity, cancellationToken);
    }

    private async Task<FullIdentityReservationResult> AdmitAsync(
        SignedTransaction<FullIdentityPayload> transaction,
        CancellationToken cancellationToken)
    {
        var validation = _validator.Validate(transaction);
        if (!validation.IsValid)
        {
            return validation.IsEditable
                ? FullIdentityReservationResult.RejectedEditable(validation.ValidationCode!)
                : FullIdentityReservationResult.Rejected(validation.ValidationCode!);
        }

        var signingAddress = transaction.Payload.PublicSigningAddress;
        if (await IsIndexedAsync(signingAddress, cancellationToken))
        {
            return FullIdentityReservationResult.AlreadyExists();
        }

        var digest = ComputeDigest(transaction);
        var entry = new ReservationEntry(transaction.TransactionId.Value.ToString(), digest);
        var added = _reservations.TryAdd(signingAddress, entry);
        if (added)
        {
            return FullIdentityReservationResult.Accepted();
        }

        var existing = _reservations[signingAddress];
        return existing == entry
            ? FullIdentityReservationResult.Pending()
            : FullIdentityReservationResult.Conflict();
    }

    public Task<FullIdentityReservationResult> ReserveAsync(
        string signingAddress,
        string transactionId,
        string transactionDigest,
        CancellationToken cancellationToken)
    {
        var entry = new ReservationEntry(transactionId, transactionDigest);
        if (_reservations.TryAdd(signingAddress, entry))
        {
            return Task.FromResult(FullIdentityReservationResult.Accepted());
        }

        var existing = _reservations[signingAddress];
        return Task.FromResult(existing == entry
            ? FullIdentityReservationResult.Pending()
            : FullIdentityReservationResult.Conflict());
    }

    public Task ReleaseAsync(string signingAddress, CancellationToken cancellationToken)
    {
        _reservations.TryRemove(signingAddress, out _);
        return Task.CompletedTask;
    }

    public Task MarkIndexedAsync(string signingAddress, CancellationToken cancellationToken)
    {
        _reservations.TryRemove(signingAddress, out _);
        return Task.CompletedTask;
    }

    private async Task<bool> IsIndexedAsync(string signingAddress, CancellationToken cancellationToken)
    {
        using var readonlyUnitOfWork = _unitOfWorkProvider.CreateReadOnly();
        return await readonlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .AnyAsync(signingAddress);
    }

    /// <summary>Stable digest over the canonical unsigned JSON (sha-256 hex, lowercase).</summary>
    public static string ComputeDigest(SignedTransaction<FullIdentityPayload> transaction)
    {
        var canonicalJson = new FullIdentityCanonicalSerializer().SerializeCanonicalUnsignedJson(transaction);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Blockchain.Storage.Model;
using HushNode.Elections;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.HushVotingLicensingIntegration;

public sealed record ElectionRolloutReadinessResult(bool Ready, string Code, int BackfilledCaptures = 0);

/// <summary>Coordinated, quiescent startup assessment. Never borrows current rights, seeds an
/// indexing checkpoint, deletes history or repairs an incompatible capture. A missing capture
/// is reconstructed only from checked original execution evidence, in one atomic transaction.</summary>
public sealed class ElectionEntitlementRolloutReadiness(
    Func<DbContext> contextFactory, LicenceCatalogueArchive archive,
    IHushVotingLicenceTransactionValidator licenceValidator, IElectionEnvelopeCryptoService crypto)
{
    public async Task<ElectionRolloutReadinessResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = contextFactory();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var history = new RetainedHistory(db, cancellationToken);
            var failure = await CheckHistoryAsync(db, history, cancellationToken);
            if (failure is not null) return new(false, failure);
            var backfilled = 0;
            // Startup qualification may scan history; ordinary mutations retain indexed bounded reads.
            var subjects = await db.Set<LicenceSubjectEntity>().AsNoTracking().ToListAsync(cancellationToken);
            var verified = new Dictionary<Guid, IReadOnlyList<HistoricalAssignment>>();
            foreach (var subject in subjects)
            {
                var assignments = await VerifyAssignmentsAsync(db, history, subject, cancellationToken);
                if (assignments is null) return new(false, "election_licence_history_unprovable");
                verified.Add(subject.LicenceSubjectId, assignments);
            }
            foreach (var election in await db.Set<ElectionRecord>().AsNoTracking().ToListAsync(cancellationToken))
            {
                var existing = await db.Set<ElectionEntitlementCapture>().AsNoTracking()
                    .SingleOrDefaultAsync(c => c.ElectionId == election.ElectionId, cancellationToken);
                if (!ElectionCapturedEntitlementAuthority.RequiresCapture(election))
                {
                    if (existing is not null) return new(false, "election_capture_incompatible");
                    continue;
                }
                var subject = subjects.SingleOrDefault(s => s.CanonicalPublicSigningAddress == election.OwnerPublicAddress
                    && s.SubjectType == LicencePersistenceVocabulary.SubjectTypeIdentity);
                if (subject is null) return new(false, "election_capture_unprovable");
                var open = await db.Set<ElectionBoundaryArtifactRecord>().AsNoTracking()
                    .SingleOrDefaultAsync(b => b.Id == election.OpenArtifactId, cancellationToken);
                var basis = await db.Set<ElectionEligibilitySnapshotRecord>().AsNoTracking()
                    .SingleOrDefaultAsync(b => b.ElectionId == election.ElectionId && b.SnapshotType == ElectionEligibilitySnapshotType.Open, cancellationToken);
                var proven = await ProveCaptureAsync(db, history, election, open, basis, verified[subject.LicenceSubjectId], cancellationToken);
                if (proven is null) return new(false, "election_capture_unprovable");
                if (existing is not null && existing != proven) return new(false, "election_capture_incompatible");
                if (existing is null) { db.Add(proven); backfilled++; }
            }
            // No partial repair escapes if any later record fails validation.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, "election_entitlement_ready", backfilled);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { return new(false, "election_history_incompatible"); }
        catch (JsonException) { return new(false, "election_history_incompatible"); }
        catch (Exception) { return new(false, "election_readiness_unavailable"); }
    }

    private async Task<string?> CheckHistoryAsync(DbContext db, RetainedHistory history, CancellationToken ct)
    {
        long blockCount = 0, licenceTransactions = 0;
        BlockchainBlock? previous = null;
        for (var page = 0; ; page++)
        {
            var rows = await db.Set<BlockchainBlock>().AsNoTracking().OrderBy(b => b.BlockIndex).Skip(page * 128).Take(128).ToArrayAsync(ct);
            if (rows.Length == 0) break;
            foreach (var row in rows)
            {
                var block = await history.ReadAsync(row);
                if (block is null) return "election_index_checkpoint_incomplete";
                if (previous is not null && (row.BlockIndex.Value != previous.BlockIndex.Value + 1
                    || row.PreviousBlockId != previous.BlockId || previous.NextBlockId != row.BlockId))
                    return "election_history_incompatible";
                if (previous is null && row.BlockId != BlockId.GenesisBlockId) return "election_history_incompatible";
                previous = row;
                blockCount++;
                foreach (var tx in block.Transactions)
                {
                    if (tx.PayloadKind == HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind) licenceTransactions++;
                    var isOpen = tx is ValidatedTransaction<OpenElectionPayload>;
                    if (tx is ValidatedTransaction<EncryptedElectionEnvelopePayload>)
                    {
                        var action = crypto.TryDecryptValidated(tx);
                        if (action is null) return "election_client_semantics_unsupported";
                        isOpen = action.ActionType == EncryptedElectionEnvelopeActionTypes.OpenElection;
                    }
                    if (isOpen && !await db.Set<ElectionBoundaryArtifactRecord>().AnyAsync(b =>
                        b.ArtifactType == ElectionBoundaryArtifactType.Open && b.SourceTransactionId == tx.TransactionId.Value, ct)
                        && !await db.Set<ElectionOpenRejection>().AnyAsync(r => r.TransactionId == tx.TransactionId.Value, ct))
                        return "election_open_outcome_missing";
                }
            }
        }
        var head = await db.Set<BlockchainState>().AsNoTracking().SingleOrDefaultAsync(ct);
        if ((previous is null) != (head is null) || previous is not null &&
            (head!.BlockIndex != previous.BlockIndex || head.CurrentBlockId != previous.BlockId)) return "election_index_checkpoint_incomplete";
        if (await db.Set<ElectionIndexCheckpoint>().LongCountAsync(ct) != blockCount) return "election_index_checkpoint_incomplete";
        if (await db.Set<LicenceAssignmentEntity>().LongCountAsync(ct) != licenceTransactions) return "election_licence_history_unprovable";
        foreach (var rejection in await db.Set<ElectionOpenRejection>().AsNoTracking().ToArrayAsync(ct))
        {
            var slot = await history.SlotAsync(rejection.BlockHeight, rejection.TransactionId);
            if (!rejection.HasSupportedSemantics() || slot is null || slot.Position != rejection.TransactionPosition
                || slot.Block.BlockId.Value != rejection.BlockId || StoredInstant(slot.Block.CreationTimeStamp.Value) != rejection.BlockTimeUtc)
                return "election_open_outcome_incompatible";
        }
        return null;
    }

    private async Task<IReadOnlyList<HistoricalAssignment>?> VerifyAssignmentsAsync(DbContext db, RetainedHistory history,
        LicenceSubjectEntity subject, CancellationToken ct)
    {
        var rows = await db.Set<LicenceAssignmentEntity>().AsNoTracking().Where(a => a.LicenceSubjectId == subject.LicenceSubjectId).ToArrayAsync(ct);
        var ordered = new List<HistoricalAssignment>();
        foreach (var row in rows)
        {
            if (row.OriginatingTransactionId is not Guid tx || row.OriginatingBlockIndex is not long height) return null;
            var slot = await history.SlotAsync(height, tx);
            if (slot is null) return null;
            ordered.Add(new(row, slot));
        }
        ordered.Sort((a, b) => Compare(a.Slot, b.Slot));
        LicenceAssignmentEntity? previous = null;
        foreach (var item in ordered)
        {
            var row = item.Assignment;
            var release = archive.Find(row.AssignedCatalogueVersion, row.AssignedCatalogueDigestSha256);
            if (release is null || item.Slot.Transaction is not ValidatedTransaction<HushVotingLicenceAssignmentPayload> tx) return null;
            var authenticated = await licenceValidator.AuthenticateAsync(new SignedTransaction<HushVotingLicenceAssignmentPayload>(tx, tx.UserSignature), ct);
            if (!authenticated.IsValid || authenticated.ValidatedContent is not HushVotingLicenceSignatoryContext actor
                || actor.CanonicalPublicSigningAddress != subject.CanonicalPublicSigningAddress
                || actor.IdentityCreationBlockIndex != subject.IdentityCreationBlockIndex
                || actor.IdentityCreationBlockIndex > item.Slot.Block.BlockIndex.Value) return null;
            var time = StoredInstant(item.Slot.Block.CreationTimeStamp.Value);
            var decision = HushVotingLicenceTransitionDecisionCore.Decide(release.Catalogue, tx.Payload,
                LicenceBlockIndexWriterDecisions.CurrentlyActiveState(release.Catalogue, previous, time));
            var facts = decision.OperativeFacts;
            if (!decision.IsValid || facts is null || tx.Payload.ObservedCatalogueVersion != release.CatalogueVersion
                || row.PlanId != facts.PlanId.Value || row.PlanFamily != facts.PlanFamily || row.UpgradeRank != facts.UpgradeRank
                || row.EligibleVoterCap != facts.EligibleVoterCap || row.UnlimitedElectionPolicy != facts.UnlimitedElections
                || row.TermKind != facts.TermKind || row.TermYears != facts.TermYears
                || !row.AllowedGovernanceOptionIds.SequenceEqual(facts.GovernanceOptionIds)
                || row.EffectiveFromUtc != time || row.OriginatingBlockTimeStampUtc != time
                || row.ExpiresAtUtc != (facts.Term.IsPerpetual ? null : LicenceEntitlementDecisions.ComputeExpiryInstant(time, facts.Term))) return null;
            previous = row;
        }
        return subject.EntitlementRevision == ordered.Count ? ordered : null;
    }

    private async Task<ElectionEntitlementCapture?> ProveCaptureAsync(DbContext db, RetainedHistory history,
        ElectionRecord election, ElectionBoundaryArtifactRecord? open, ElectionEligibilitySnapshotRecord? basis,
        IReadOnlyList<HistoricalAssignment> assignments, CancellationToken ct)
    {
        if (open?.SourceTransactionId is not Guid tx || open.SourceBlockHeight is not long height || basis is null) return null;
        var slot = await history.SlotAsync(height, tx);
        if (slot is null || open.Metadata.OwnerPublicAddress != election.OwnerPublicAddress
            || open.FrozenEligibleVoterSetHash is null
            || !open.FrozenEligibleVoterSetHash.SequenceEqual(election.EligibilityMutationPolicy == EligibilityMutationPolicy.FrozenAtOpen
                ? basis.ActiveDenominatorSetHash : basis.RosteredVoterSetHash)
            || await db.Set<ElectionRosterEntryRecord>().CountAsync(r => r.ElectionId == election.ElectionId && r.WasPresentAtOpen, ct) != basis.RosteredCount)
            return null;
        var prior = assignments.Where(a => Compare(a.Slot, slot) < 0).ToArray();
        if (prior.Length == 0) return null;
        var row = prior[^1].Assignment;
        var time = StoredInstant(slot.Block.CreationTimeStamp.Value);
        if (!HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(row.EffectiveFromUtc, row.ExpiresAtUtc, time)) return null;
        var terms = new EffectiveLicenceEntitlement(row.LicenceSubjectId, row.LicenceAssignmentId, row.PlanId, row.PlanFamily,
            row.UpgradeRank, row.EligibleVoterCap, row.UnlimitedElectionPolicy, row.TermKind, row.TermYears,
            row.AllowedGovernanceOptionIds, row.Source, row.EffectiveFromUtc, row.ExpiresAtUtc, row.AssignedCatalogueVersion,
            row.AssignedCatalogueDigestSha256, prior.Length, row.OriginatingTransactionId);
        var reason = new ElectionEntitlementAuthorizer(archive).Check(IndexedEntitlementReadResult.Active(terms),
            election.SelectedProfileId, election.BindingStatus, election.GovernanceMode, basis.RosteredCount, out var governance);
        if (reason != ElectionEntitlementReason.None) return null;
        var proposals = await db.Set<ElectionGovernedProposalRecord>().AsNoTracking().Where(p => p.ElectionId == election.ElectionId
            && p.ActionType == ElectionGovernedActionType.Open && p.LatestTransactionId == tx
            && p.ExecutionStatus == ElectionGovernedProposalExecutionStatus.ExecutionSucceeded).ToArrayAsync(ct);
        if (proposals.Length > 1) return null;
        var proposal = proposals.SingleOrDefault();
        if (!MatchesOpen(slot.Transaction, election.ElectionId, proposal?.Id)) return null;
        var capture = new ElectionEntitlementCapture(election.ElectionId, row.LicenceSubjectId, row.OriginatingTransactionId!.Value,
            row.PlanId, row.PlanFamily, row.UpgradeRank, row.EligibleVoterCap, row.UnlimitedElectionPolicy, row.TermKind, row.TermYears,
            row.EffectiveFromUtc, row.ExpiresAtUtc, JsonSerializer.Serialize(row.AllowedGovernanceOptionIds),
            row.AssignedCatalogueVersion, row.AssignedCatalogueDigestSha256, prior.Length, election.SelectedProfileId, governance!,
            basis.RosteredCount, basis.Id, tx, slot.Block.BlockId.Value, height, slot.Position, time, proposal?.Id);
        return ElectionCapturedEntitlementAuthority.Check(election, capture, open, basis) == ElectionEntitlementReason.None ? capture : null;
    }

    private bool MatchesOpen(AbstractTransaction tx, ElectionId electionId, Guid? proposalId)
    {
        if (tx is ValidatedTransaction<OpenElectionPayload> direct) return proposalId is null && direct.Payload.ElectionId == electionId;
        if (tx is ValidatedTransaction<ApproveElectionGovernedProposalPayload> approval)
            return proposalId == approval.Payload.ProposalId && approval.Payload.ElectionId == electionId;
        if (tx is ValidatedTransaction<RetryElectionGovernedProposalExecutionPayload> retry)
            return proposalId == retry.Payload.ProposalId && retry.Payload.ElectionId == electionId;
        if (tx is not ValidatedTransaction<EncryptedElectionEnvelopePayload> envelope || envelope.Payload.ElectionId != electionId) return false;
        var action = crypto.TryDecryptValidated(tx);
        return action?.ActionType switch
        {
            EncryptedElectionEnvelopeActionTypes.OpenElection => proposalId is null,
            EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal => proposalId is not null && action.DeserializeAction<ApproveElectionGovernedProposalActionPayload>()?.ProposalId == proposalId,
            EncryptedElectionEnvelopeActionTypes.RetryGovernedProposalExecution => proposalId is not null && action.DeserializeAction<RetryElectionGovernedProposalExecutionActionPayload>()?.ProposalId == proposalId,
            _ => false,
        };
    }

    // Existing TimestampConverter returns machine-local DateTime even for canonical Z input.
    // Normalize the retained UTC representation explicitly, without changing signed wire bytes.
    public static UnsignedBlock ReadCanonicalBlock(BlockchainBlock row)
    {
        using var document = JsonDocument.Parse(row.BlockJson);
        if (document.RootElement.GetProperty("CreationTimeStamp").GetString()?.EndsWith("Z", StringComparison.Ordinal) != true)
            throw new InvalidDataException("Retained block timestamp is not canonical UTC.");
        var block = JsonSerializer.Deserialize<UnsignedBlock>(row.BlockJson)
            ?? throw new InvalidDataException("Retained block is missing.");
        block = block with { CreationTimeStamp = new HushShared.Blockchain.Model.Timestamp(block.CreationTimeStamp.Value.ToUniversalTime()) };
        if (block.BlockId != row.BlockId || block.BlockIndex != row.BlockIndex
            || block.PreviousBlockId != row.PreviousBlockId || block.NextBlockId != row.NextBlockId
            || block.Transactions.Select(t => t.TransactionId).Distinct().Count() != block.Transactions.Length)
            throw new InvalidDataException("Retained block does not match its index.");
        return block;
    }

    // PostgreSQL timestamptz stores microseconds; Npgsql truncates the final 100ns digit.
    // Reproduce the existing storage representation, not a wall-clock tolerance or guessed time.
    private static DateTime StoredInstant(DateTime utc) => new(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);

    private static int Compare(HistorySlot a, HistorySlot b) => a.Block.BlockIndex.Value != b.Block.BlockIndex.Value
        ? a.Block.BlockIndex.Value.CompareTo(b.Block.BlockIndex.Value) : a.Position.CompareTo(b.Position);
    private sealed record HistoricalAssignment(LicenceAssignmentEntity Assignment, HistorySlot Slot);
    private sealed record HistorySlot(UnsignedBlock Block, int Position, AbstractTransaction Transaction);
    private sealed class RetainedHistory(DbContext db, CancellationToken ct)
    {
        public async Task<UnsignedBlock?> ReadAsync(BlockchainBlock row)
        {
            var checkpoint = await db.Set<ElectionIndexCheckpoint>().AsNoTracking().SingleOrDefaultAsync(c => c.BlockHeight == row.BlockIndex.Value, ct);
            if (checkpoint is null || checkpoint.BlockId != row.BlockId.Value || checkpoint.BlockHash != row.Hash
                || checkpoint.PolicyVersion != ElectionEntitlementCapture.CurrentPolicyVersion
                || checkpoint.HistoryDigestSha256 != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.BlockJson)))) return null;
            return ReadCanonicalBlock(row);
        }

        public async Task<HistorySlot?> SlotAsync(long height, Guid transactionId)
        {
            var row = await db.Set<BlockchainBlock>().AsNoTracking().SingleOrDefaultAsync(b => b.BlockIndex == new BlockIndex(height), ct);
            var block = row is null ? null : await ReadAsync(row);
            if (block is null) return null;
            var index = Array.FindIndex(block.Transactions, t => t.TransactionId.Value == transactionId);
            return index < 0 ? null : new(block, index, block.Transactions[index]);
        }
    }
}

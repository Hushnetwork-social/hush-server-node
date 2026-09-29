using HushNode.Blockchain.Storage.Model;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.BlockModel;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.HushVotingLicensingIntegration;

public sealed class ElectionIndexCompletionRecorder(Func<DbContext> contextFactory) : IBlockIndexCompletionRecorder
{
    public async Task RecordAsync(long blockHeight, Guid blockId, string blockHash, string historyDigestSha256)
    {
        await using var db = contextFactory();
        var retained = await db.Set<BlockchainBlock>().AsNoTracking()
            .SingleOrDefaultAsync(b => b.BlockId == new BlockId(blockId));
        if (retained is null || retained.BlockIndex.Value != blockHeight || retained.Hash != blockHash
            || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(retained.BlockJson))) != historyDigestSha256)
            throw new InvalidOperationException("election_checkpoint_history_mismatch");
        var proposed = new ElectionIndexCheckpoint(blockHeight, blockId, blockHash, historyDigestSha256);
        var existing = await db.Set<ElectionIndexCheckpoint>().FindAsync(blockHeight);
        if (existing is not null)
        {
            if (existing != proposed) throw new InvalidOperationException("election_checkpoint_conflict");
            return;
        }
        db.Add(proposed);
        await db.SaveChangesAsync();
    }
}

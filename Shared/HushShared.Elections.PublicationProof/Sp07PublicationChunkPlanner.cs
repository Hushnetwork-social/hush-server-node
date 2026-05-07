using System.Security.Cryptography;
using System.Text;

namespace HushShared.Elections.PublicationProof;

public sealed record Sp07PublicationChunkPlannerOptions(
    int MaxBallotsPerChunk = 100,
    int MinBallotsPerChunk = 2,
    int MaxChunks = 5,
    int MaxEncryptedSlots = 8)
{
    public void Validate()
    {
        if (MaxBallotsPerChunk < 1)
        {
            throw new Sp07PublicationProofException("SP-07 max ballots per chunk must be positive.");
        }

        if (MinBallotsPerChunk < 1)
        {
            throw new Sp07PublicationProofException("SP-07 min ballots per chunk must be positive.");
        }

        if (MinBallotsPerChunk > MaxBallotsPerChunk)
        {
            throw new Sp07PublicationProofException("SP-07 min ballots per chunk cannot exceed max ballots per chunk.");
        }

        if (MaxChunks < 1)
        {
            throw new Sp07PublicationProofException("SP-07 max chunks must be positive.");
        }

        if (MaxEncryptedSlots < 1)
        {
            throw new Sp07PublicationProofException("SP-07 max encrypted slots must be positive.");
        }
    }
}

public sealed record Sp07PublicationChunkPlan(
    string PlanId,
    int AcceptedBallotCount,
    int EncryptedSlotCount,
    int MaxBallotsPerChunk,
    int MinBallotsPerChunk,
    int MaxChunks,
    string PlanHashSha512,
    IReadOnlyList<Sp07PublicationChunkPlanItem> Chunks);

public sealed record Sp07PublicationChunkPlanItem(
    string ChunkId,
    int ChunkIndex,
    int Offset,
    int Count);

public sealed class Sp07PublicationChunkPlanner(
    Sp07PublicationChunkPlannerOptions? options = null)
{
    private readonly Sp07PublicationChunkPlannerOptions _options =
        options ?? new Sp07PublicationChunkPlannerOptions();

    public Sp07PublicationChunkPlan CreatePlan(
        int acceptedBallotCount,
        int encryptedSlotCount)
    {
        _options.Validate();
        if (acceptedBallotCount < 1)
        {
            throw new Sp07PublicationProofException("SP-07 chunk planning requires at least one accepted ballot.");
        }

        if (encryptedSlotCount < 1)
        {
            throw new Sp07PublicationProofException("SP-07 chunk planning requires at least one encrypted slot.");
        }

        if (encryptedSlotCount > _options.MaxEncryptedSlots)
        {
            throw new Sp07PublicationProofException(
                $"SP-07 high-assurance v1 supports up to {_options.MaxEncryptedSlots} encrypted slots.");
        }

        var chunkCount = ResolveChunkCount(acceptedBallotCount);
        var chunkCounts = BalanceChunkCounts(acceptedBallotCount, chunkCount);
        if (chunkCounts.Any(count => count > _options.MaxBallotsPerChunk))
        {
            throw new Sp07PublicationProofException(
                $"SP-07 chunk planning cannot keep chunks below {_options.MaxBallotsPerChunk} ballots.");
        }

        if (chunkCounts.Count > 1 && chunkCounts.Any(count => count < _options.MinBallotsPerChunk))
        {
            throw new Sp07PublicationProofException(
                $"SP-07 chunk planning would create a chunk below the {_options.MinBallotsPerChunk}-ballot anonymity floor.");
        }

        var planHash = ComputePlanHash(acceptedBallotCount, encryptedSlotCount, chunkCounts);
        var planId = $"sp07-plan-{planHash[..16]}";
        var offset = 0;
        var chunks = chunkCounts
            .Select((count, index) =>
            {
                var item = new Sp07PublicationChunkPlanItem(
                    $"{planId}-chunk-{index + 1:D4}",
                    index,
                    offset,
                    count);
                offset += count;
                return item;
            })
            .ToArray();

        return new Sp07PublicationChunkPlan(
            planId,
            acceptedBallotCount,
            encryptedSlotCount,
            _options.MaxBallotsPerChunk,
            _options.MinBallotsPerChunk,
            _options.MaxChunks,
            planHash,
            chunks);
    }

    private int ResolveChunkCount(int acceptedBallotCount)
    {
        var chunkCount = (int)Math.Ceiling((double)acceptedBallotCount / _options.MaxBallotsPerChunk);
        if (chunkCount > _options.MaxChunks)
        {
            throw new Sp07PublicationProofException(
                $"SP-07 chunk planning requires {chunkCount} chunks, exceeding the configured maximum of {_options.MaxChunks}.");
        }

        while (chunkCount > 1)
        {
            var counts = BalanceChunkCounts(acceptedBallotCount, chunkCount);
            if (counts.All(count => count >= _options.MinBallotsPerChunk))
            {
                return chunkCount;
            }

            chunkCount--;
        }

        return 1;
    }

    private static IReadOnlyList<int> BalanceChunkCounts(
        int acceptedBallotCount,
        int chunkCount)
    {
        var baseCount = acceptedBallotCount / chunkCount;
        var remainder = acceptedBallotCount % chunkCount;
        return Enumerable.Range(0, chunkCount)
            .Select(index => baseCount + (index < remainder ? 1 : 0))
            .ToArray();
    }

    private static string ComputePlanHash(
        int acceptedBallotCount,
        int encryptedSlotCount,
        IReadOnlyList<int> chunkCounts)
    {
        var payload =
            $"HUSH_SP07_CHUNK_PLAN_V1|accepted={acceptedBallotCount}|slots={encryptedSlotCount}|chunks={string.Join(',', chunkCounts)}";
        return Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

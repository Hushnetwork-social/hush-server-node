using System.Collections.Concurrent;
using Olimpo;

namespace HushNode.Elections;

public interface ICloseCountingExecutorKeyRegistry
{
    CloseCountingExecutorKeyMaterial Create(Guid closeCountingJobId, string keyAlgorithm);

    bool TryGet(Guid closeCountingJobId, out CloseCountingExecutorKeyMaterial? material);

    void Destroy(Guid closeCountingJobId);
}

public sealed record CloseCountingExecutorKeyMaterial(
    string PublicKey,
    string PrivateKey,
    string KeyAlgorithm,
    DateTime CreatedAt);

public static class CloseCountingExecutorKeyRegistryConstants
{
    public const string MemoryOnlyEnvelopeMarker = "[memory-only-executor-session-key]";
    public const string DestroyedEnvelopeMarker = "[destroyed-memory-only-executor-session-key]";
}

public sealed class InMemoryCloseCountingExecutorKeyRegistry : ICloseCountingExecutorKeyRegistry
{
    private readonly ConcurrentDictionary<Guid, CloseCountingExecutorKeyMaterial> _keys = new();

    public CloseCountingExecutorKeyMaterial Create(Guid closeCountingJobId, string keyAlgorithm)
    {
        var timestamp = DateTime.UtcNow;
        var executorSessionKeys = new EncryptKeys();
        var material = new CloseCountingExecutorKeyMaterial(
            executorSessionKeys.PublicKey,
            executorSessionKeys.PrivateKey,
            keyAlgorithm.Trim(),
            timestamp);

        if (!_keys.TryAdd(closeCountingJobId, material))
        {
            throw new InvalidOperationException(
                $"Close-counting executor key material already exists for job {closeCountingJobId}.");
        }

        return material;
    }

    public bool TryGet(Guid closeCountingJobId, out CloseCountingExecutorKeyMaterial? material) =>
        _keys.TryGetValue(closeCountingJobId, out material);

    public void Destroy(Guid closeCountingJobId)
    {
        _keys.TryRemove(closeCountingJobId, out _);
    }
}

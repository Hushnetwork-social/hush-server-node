namespace HushNode.Indexing.Interfaces;

/// <summary>Durable completion boundary. A recorder failure prevents the completion event.
/// Invoked only after every transaction handler has completed successfully.</summary>
public interface IBlockIndexCompletionRecorder
{
    Task RecordAsync(long blockHeight, Guid blockId, string blockHash, string historyDigestSha256);
}

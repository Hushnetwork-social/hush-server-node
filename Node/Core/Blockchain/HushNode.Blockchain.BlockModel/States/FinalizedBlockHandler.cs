namespace HushNode.Blockchain.BlockModel.States;

public static class FinalizedBlockHandler
{
    public static (SignedBlock, string) ExtractSignedBlock(this FinalizedBlock finalizedBlock) =>
        (new(
            finalizedBlock.BlockId,
            finalizedBlock.CreationTimeStamp,
            finalizedBlock.BlockIndex,
            finalizedBlock.PreviousBlockId,
            finalizedBlock.NextBlockId,
            finalizedBlock.BlockProducerSignature,
            finalizedBlock.Transactions), 
        finalizedBlock.Hash);
}

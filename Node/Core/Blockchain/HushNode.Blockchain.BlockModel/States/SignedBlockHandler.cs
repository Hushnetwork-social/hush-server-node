namespace HushNode.Blockchain.BlockModel.States;

public static class SignedBlockHandler
{
    public static FinalizedBlock FinalizeIt(this SignedBlock signedBlock)
    {
        return new FinalizedBlock(
            signedBlock, 
            signedBlock.GetHashCode().ToString());
    }

    public static UnsignedBlock ExtractUnsignedBlock(this SignedBlock signedBlock) =>
        UnsignedBlockHandler.CreateNew(
            signedBlock.BlockId,
            signedBlock.BlockIndex,
            signedBlock.CreationTimeStamp,
            signedBlock.PreviousBlockId,
            signedBlock.NextBlockId,
            signedBlock.Transactions);
}

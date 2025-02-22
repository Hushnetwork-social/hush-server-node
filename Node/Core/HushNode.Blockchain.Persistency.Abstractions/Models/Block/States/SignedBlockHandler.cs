namespace HushNode.Blockchain.Persistency.Abstractions.Models.Block.States;

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

    public static bool IsBlockValid(this FinalizedBlock finalizedBlock) => 
        finalizedBlock.CheckBlockHashAndSignature() &&
        finalizedBlock.Transactions
            .All(transaction => transaction.CheckValidatorSignature() || transaction.CheckUserSignature());

    private static bool CheckBlockHashAndSignature(this FinalizedBlock finalizedBlock) =>
        finalizedBlock.IsBlockHashValid() &&
        finalizedBlock.IsBlockSignatureValid();

    public static bool IsBlockHashValid(this FinalizedBlock finalizedBlock)
    {
        var (signedBlock, hash) = finalizedBlock.ExtractSignedBlock();

        return signedBlock.CheckHash(hash);
    }

    public static bool IsBlockSignatureValid(this FinalizedBlock finalizedBlock) => 
        finalizedBlock.ExtractSignedBlock().Item1.CheckSignature();

    public static bool CheckHash(this SignedBlock signedBlock, string hash) => 
        signedBlock.GetHashCode().ToString() == hash;

    
    public static BlockchainBlock ToBlockchainBlock(this FinalizedBlock finalizedBlock) =>
        new(
            finalizedBlock.BlockId,
            finalizedBlock.BlockIndex,
            finalizedBlock.PreviousBlockId,
            finalizedBlock.NextBlockId,
            finalizedBlock.Hash,
            finalizedBlock.ToJson());
        
}

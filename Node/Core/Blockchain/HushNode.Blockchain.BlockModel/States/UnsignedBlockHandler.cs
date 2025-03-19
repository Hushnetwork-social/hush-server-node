using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Blockchain.BlockModel.States;

public static class UnsignedBlockHandler
{
    public static UnsignedBlock CreateGenesis(
        Timestamp creationTimeStamp,
        BlockId nextBlockId) => 
        CreateNew(
            BlockId.GenesisBlockId,
            new BlockIndex(1),
            creationTimeStamp,
            BlockId.Empty,
            nextBlockId);

    public static UnsignedBlock CreateNew(
        BlockId blockId,
        BlockIndex blockIndex, 
        Timestamp creationTimeStamp,
        BlockId previousBlockId, 
        BlockId nextBlockId, 
        AbstractTransaction[] transactions)
    {
        return new UnsignedBlock(
            blockId,
            creationTimeStamp,
            blockIndex,
            previousBlockId,
            nextBlockId,
            transactions);
    }

    public static UnsignedBlock CreateNew(
        BlockId blockId,
        BlockIndex blockIndex, 
        Timestamp creationTimeStamp,
        BlockId previousBlockId, 
        BlockId nextBlockId)
    {
        return new UnsignedBlock(
            blockId,
            creationTimeStamp,
            blockIndex,
            previousBlockId,
            nextBlockId,
            []);
    }

    // public static SignedBlock SignIt(
    //     this UnsignedBlock unsignedBlock, 
    //     CredentialsProfile credentials) => 
    //     new(
    //         unsignedBlock, 
    //         new SignatureInfo(
    //             credentials.PublicSigningAddress, 
    //             unsignedBlock.CreateSignature(credentials.PrivateSigningKey)));

    // public static SignedBlock SignIt(
    //     this UnsignedBlock unsignedBlock, 
    //     SignatureInfo signatureInfo) => 
    //     new(
    //         unsignedBlock, 
    //         signatureInfo);

    // public static SignedBlock SignIt(
    //     this UnsignedBlock unsignedBlock, 
    //     string publickey, 
    //     string privateKey) => 
    //     new(
    //         unsignedBlock, 
    //         new SignatureInfo(publickey, unsignedBlock.CreateSignature(privateKey)));

    // public static FinalizedBlock SignAndFinalizeBlock(
    //     this UnsignedBlock unsignedBlock, 
    //     SignatureInfo blockProducerSignature) => 
    //     unsignedBlock
    //         .SignIt(blockProducerSignature)
    //         .FinalizeIt();

    // public static FinalizedBlock SignAndFinalizeBlock(
    //     this UnsignedBlock unsignedBlock, 
    //     CredentialsProfile credentials) => 
    //     unsignedBlock
    //         .SignIt(credentials)
    //         .FinalizeIt();
}

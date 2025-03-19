using HushNode.Blockchain.BlockModel.States;
using HushNode.Credentials;
using HushShared.Blockchain.Model;
using Olimpo;

namespace HushNode.Blockchain;

public static class UnsignedBlockExtensionMethods
{
    public static SignedBlock SignIt(
        this UnsignedBlock unsignedBlock, 
        CredentialsProfile credentials) => 
        new(
            unsignedBlock, 
            new SignatureInfo(
                credentials.PublicSigningAddress, 
                unsignedBlock.CreateSignature(credentials.PrivateSigningKey)));

    public static SignedBlock SignIt(
        this UnsignedBlock unsignedBlock, 
        SignatureInfo signatureInfo) => 
        new(
            unsignedBlock, 
            signatureInfo);

    public static SignedBlock SignIt(
        this UnsignedBlock unsignedBlock, 
        string publickey, 
        string privateKey) => 
        new(
            unsignedBlock, 
            new SignatureInfo(publickey, unsignedBlock.CreateSignature(privateKey)));

    public static FinalizedBlock SignAndFinalizeBlock(
        this UnsignedBlock unsignedBlock, 
        SignatureInfo blockProducerSignature) => 
        unsignedBlock
            .SignIt(blockProducerSignature)
            .FinalizeIt();

    public static FinalizedBlock SignAndFinalizeBlock(
        this UnsignedBlock unsignedBlock, 
        CredentialsProfile credentials) => 
        unsignedBlock
            .SignIt(credentials)
            .FinalizeIt();

    public static string CreateSignature(this UnsignedBlock unsignedBlock, string privateKey) => 
        DigitalSignature.SignMessage(unsignedBlock.ToJson(), privateKey);
}

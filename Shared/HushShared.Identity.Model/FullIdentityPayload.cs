using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Identity.Model;

public record FullIdentityPayload(
    string IdentityAlias,
    string PublicSigningAddress,
    string PublicEncryptAddress,
    bool IsPublic) : ITransactionPayloadKind;

public static class FullIdentityPayloadHandler
{
    public static Guid FullIdentityPayloadKind { get; } = Guid.Parse("351cd60b-3fdf-48d4-b608-e93c0100f7d0");

    public static UnsignedTransaction<FullIdentityPayload> CreateNew(
        string identityAlias,
        string publicSigningAddress,
        string publicEncryptAddress,
        bool isPublic) => 
        UnsignedTransactionHandler.CreateNew(
            FullIdentityPayloadKind,
            Timestamp.Current,
            new FullIdentityPayload(
                identityAlias, 
                publicSigningAddress, 
                publicEncryptAddress, 
                isPublic));
}
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Identity.Model;

public record UpdateIdentityPayload(string NewAlias) : ITransactionPayloadKind;

public static class UpdateIdentityPayloadHandler
{
    public static Guid UpdateIdentityPayloadKind { get; } = Guid.Parse("a7e3c4b2-1f8d-4e5a-9c6b-2d3e4f5a6b7c");

    public static UnsignedTransaction<UpdateIdentityPayload> CreateNew(string newAlias) =>
        UnsignedTransactionHandler.CreateNew(
            UpdateIdentityPayloadKind,
            Timestamp.Current,
            new UpdateIdentityPayload(newAlias));
}

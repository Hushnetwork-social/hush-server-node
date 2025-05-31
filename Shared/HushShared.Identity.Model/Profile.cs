using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.TransactionModel;

namespace HushShared.Identity.Model;

public record ProfileBase;

public record Profile(
    string Alias,
    string ShortAlias,
    string PublicSigningAddress,
    string PublicEncryptAddress,
    bool IsPublic,
    BlockIndex BlockIndex) : ProfileBase, ITransactionPayloadKind;

public record NonExistingProfile : ProfileBase;

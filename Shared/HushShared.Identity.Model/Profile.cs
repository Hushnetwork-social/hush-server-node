using HushShared.Blockchain.TransactionModel;

namespace HushShared.Identity.Model;

public record ProfileBase;

public record Profile(
    string Alias,
    string PublicSigningAddress,
    string PublicEncryptAddress,
    bool IsPublic) : ProfileBase, ITransactionPayloadKind;

public record NonExistingProfile : ProfileBase;

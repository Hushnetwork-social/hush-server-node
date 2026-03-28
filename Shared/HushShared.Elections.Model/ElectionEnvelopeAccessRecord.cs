using HushShared.Blockchain.Model;

namespace HushShared.Elections.Model;

public record ElectionEnvelopeAccessRecord(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string ActorEncryptedElectionPrivateKey,
    DateTime GrantedAt,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId);

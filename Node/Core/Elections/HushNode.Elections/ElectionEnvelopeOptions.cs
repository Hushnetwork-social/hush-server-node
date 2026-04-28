namespace HushNode.Elections;

public sealed record ElectionEnvelopeOptions(
    bool AllowLegacyNodeEncryptedEnvelopeValidation = true,
    bool AllowLegacyNodeEncryptedParticipantResultMaterial = true)
{
    public static ElectionEnvelopeOptions Default { get; } = new();
}

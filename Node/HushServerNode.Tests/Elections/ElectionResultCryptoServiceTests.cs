using FluentAssertions;
using HushNode.Credentials;
using HushNode.Elections;
using HushNode.Reactions.Crypto;
using Moq;
using Olimpo;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ElectionResultCryptoServiceTests
{
    [Fact]
    public void EncryptForElectionParticipants_WithPublicElectionMaterial_DoesNotUseNodeCredentials()
    {
        var electionEncryptKeys = new EncryptKeys();
        var credentialsProvider = new Mock<ICredentialsProvider>(MockBehavior.Strict);
        var sut = new ElectionResultCryptoService(
            new BabyJubJubCurve(),
            credentialsProvider.Object,
            new ElectionEnvelopeOptions(
                AllowLegacyNodeEncryptedEnvelopeValidation: false,
                AllowLegacyNodeEncryptedParticipantResultMaterial: false));

        var encryptedPayload = sut.EncryptForElectionParticipants(
            "participant-result-payload",
            electionEncryptKeys.PublicKey);

        EncryptKeys.Decrypt(encryptedPayload, electionEncryptKeys.PrivateKey)
            .Should()
            .Be("participant-result-payload");
        credentialsProvider.VerifyNoOtherCalls();
    }

    [Fact]
    public void EncryptForElectionParticipants_WithLegacyNodeEncryptedMaterialDisabled_RejectsWithoutNodeCredentials()
    {
        var nodeEncryptKeys = new EncryptKeys();
        var electionEncryptKeys = new EncryptKeys();
        var legacyNodeEncryptedElectionPrivateKey = EncryptKeys.Encrypt(
            electionEncryptKeys.PrivateKey,
            nodeEncryptKeys.PublicKey);
        var credentialsProvider = new Mock<ICredentialsProvider>(MockBehavior.Strict);
        var sut = new ElectionResultCryptoService(
            new BabyJubJubCurve(),
            credentialsProvider.Object,
            new ElectionEnvelopeOptions(
                AllowLegacyNodeEncryptedEnvelopeValidation: false,
                AllowLegacyNodeEncryptedParticipantResultMaterial: false));

        var act = () => sut.EncryptForElectionParticipants(
            "participant-result-payload",
            legacyNodeEncryptedElectionPrivateKey);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Legacy node-encrypted election private-key material is disabled*");
        credentialsProvider.VerifyNoOtherCalls();
    }
}

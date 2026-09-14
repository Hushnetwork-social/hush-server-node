using FluentAssertions;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.Identity.Tests;

public class ShortDerSignatureTests
{
    [Fact]
    public void ApprovedDerSignatureWithShortScalarVerifiesWithoutChangingTheProducer()
    {
        // Public synthetic fixture: RFC6979 makes this 69-byte case reproducible.
        const string message = "short-der-regression-919";
        const string signature = "3043021f1a0d32d52c40366c23773acf834218c61d8581bebf7f55f3d99140928b129e0220136fad1ccd454a99bf8fac37eacfcf06f8360a912ed3752af8090ae72685585d";
        const string address = "0479be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798483ada7726a3c4655da4fbfc0e1108a8fd17b448a68554199c47d08ffb10d4b8";
        Convert.FromHexString(signature).Length.Should().Be(69);
        Olimpo.DigitalSignature.VerifySignature(message, signature, address).Should().BeTrue();

        var verifier = new FullIdentitySignatureVerifier();
        verifier.Verify(new(message, signature, address)).IsValid.Should().BeTrue();
        verifier.Verify(new(message + " altered", signature, address)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 32)]
    [InlineData(31, 32)]
    [InlineData(32, 32)]
    [InlineData(33, 32)]
    [InlineData(33, 33)]
    public void ClassifierAcceptsCanonicalPositiveIntegersOfDifferentWidths(int rLength, int sLength)
    {
        var der = new byte[6 + rLength + sLength];
        der[0] = 0x30;
        der[1] = (byte)(der.Length - 2);
        der[2] = 2;
        der[3] = (byte)rLength;
        der[4] = 1;
        der[4 + rLength] = 2;
        der[5 + rLength] = (byte)sLength;
        der[6 + rLength] = 1;
        if (rLength == 33) { der[4] = 0; der[5] = 0x80; }
        if (sLength == 33) { der[6 + rLength] = 0; der[7 + rLength] = 0x80; }

        SignatureEncodingClassifier.Classify(Convert.ToHexString(der)).Should().Be(ApprovedSignatureEncoding.Der);
    }

    [Theory]
    [InlineData("300602010102010100")] // trailing data
    [InlineData("3007020101020101")] // sequence length mismatch
    [InlineData("3006020180020101")] // negative integer
    [InlineData("300702020001020101")] // redundant leading zero
    [InlineData("30050200020101")] // empty integer
    public void ClassifierRejectsMalformedDerInsteadOfAcceptingItsLength(string encoded)
    {
        SignatureEncodingClassifier.Classify(encoded).Should().BeNull();
    }
}

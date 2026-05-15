using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyEvidenceScannerTests
{
    [Fact]
    public async Task ScanAsync_WithEncryptedAllowedPayload_ReturnsPendingTrustedScannerHandoff()
    {
        var sut = new ElectionAnomalyEvidenceScanner();

        var result = await sut.ScanAsync(CreateRequest(
            [1, 2, 3, 4],
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            payloadIsEncrypted: true));

        result.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Pending);
        result.ValidationCode.Should().BeNull();
        result.DiagnosticMessage.Should().Contain("encrypted");
    }

    [Fact]
    public async Task ScanAsync_WithUnsupportedExecutableMimeType_ReturnsQuarantined()
    {
        var sut = new ElectionAnomalyEvidenceScanner();

        var result = await sut.ScanAsync(CreateRequest(
            [1, 2, 3, 4],
            "application/x-msdownload",
            payloadIsEncrypted: false));

        result.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Quarantined);
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentMimeTypeInvalid);
    }

    [Fact]
    public async Task ScanAsync_WithEncryptedExecutableSignatureBytes_ReturnsPending()
    {
        var sut = new ElectionAnomalyEvidenceScanner();

        var result = await sut.ScanAsync(CreateRequest(
            [0x4d, 0x5a, 0x90, 0x00],
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            payloadIsEncrypted: true));

        result.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Pending);
        result.ValidationCode.Should().BeNull();
    }

    [Fact]
    public async Task ScanAsync_WithExecutableSignature_ReturnsQuarantined()
    {
        var sut = new ElectionAnomalyEvidenceScanner();

        var result = await sut.ScanAsync(CreateRequest(
            [0x4d, 0x5a, 0x90, 0x00],
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            payloadIsEncrypted: false));

        result.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Quarantined);
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid);
    }

    [Fact]
    public async Task ScanAsync_WithAllowedPlainPayload_ReturnsClear()
    {
        var sut = new ElectionAnomalyEvidenceScanner();

        var result = await sut.ScanAsync(CreateRequest(
            [1, 2, 3, 4],
            ElectionAnomalyEvidenceMimeTypes.TextPlain,
            payloadIsEncrypted: false));

        result.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Clear);
        result.ValidationCode.Should().BeNull();
    }

    private static ElectionAnomalyEvidenceScanRequest CreateRequest(
        byte[] payloadBytes,
        string mimeType,
        bool payloadIsEncrypted) =>
        new(
            ElectionId.NewElectionId,
            Guid.NewGuid(),
            ElectionAnomalyRestrictedPayloadReferences.Create(Guid.NewGuid()),
            payloadBytes,
            mimeType,
            payloadIsEncrypted);
}

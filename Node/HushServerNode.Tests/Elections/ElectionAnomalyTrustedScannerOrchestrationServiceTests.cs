using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Moq;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyTrustedScannerOrchestrationServiceTests
{
    [Fact]
    public async Task ApplyTrustedScannerHandoffAsync_WithClearHashBoundResult_MarksPayloadClear()
    {
        var payload = CreatePayload();
        var updatedPayload = payload with
        {
            ScannerStatusId = ElectionAnomalyEvidenceScannerStatusIds.Clear,
            LastCheckedAt = DateTime.UtcNow,
        };
        var (sut, storage) = CreateService(payload);
        storage
            .Setup(x => x.MarkScannerStatusAsync(
                It.Is<ElectionAnomalyRestrictedPayloadScannerStatusRequest>(request =>
                    request.ElectionId == payload.ElectionId &&
                    request.PayloadReference == payload.PayloadReference &&
                    request.ScannerStatusId == ElectionAnomalyEvidenceScannerStatusIds.Clear),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ElectionAnomalyRestrictedPayloadStatusUpdateResult.Accepted(updatedPayload, 2));

        var result = await sut.ApplyTrustedScannerHandoffAsync(CreateRequest(
            payload,
            ElectionAnomalyEvidenceScannerStatusIds.Clear));

        result.Success.Should().BeTrue();
        result.PayloadRecord!.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Clear);
        result.UpdatedAttachmentManifestCount.Should().Be(2);
        storage.Verify(
            x => x.MarkQuarantinedAsync(It.IsAny<ElectionAnomalyRestrictedPayloadQuarantineRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTrustedScannerHandoffAsync_WithQuarantinedResult_UsesQuarantinePath()
    {
        var payload = CreatePayload();
        var updatedPayload = payload with
        {
            ScannerStatusId = ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
            PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined,
            LastCheckedAt = DateTime.UtcNow,
        };
        var (sut, storage) = CreateService(payload);
        storage
            .Setup(x => x.MarkQuarantinedAsync(
                It.Is<ElectionAnomalyRestrictedPayloadQuarantineRequest>(request =>
                    request.ElectionId == payload.ElectionId &&
                    request.PayloadReference == payload.PayloadReference),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ElectionAnomalyRestrictedPayloadStatusUpdateResult.Accepted(updatedPayload, 1));

        var result = await sut.ApplyTrustedScannerHandoffAsync(CreateRequest(
            payload,
            ElectionAnomalyEvidenceScannerStatusIds.Quarantined));

        result.Success.Should().BeTrue();
        result.PayloadRecord!.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined);
        storage.Verify(
            x => x.MarkScannerStatusAsync(It.IsAny<ElectionAnomalyRestrictedPayloadScannerStatusRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTrustedScannerHandoffAsync_WithHashMismatch_RejectsWithoutStatusUpdate()
    {
        var payload = CreatePayload();
        var (sut, storage) = CreateService(payload);

        var request = CreateRequest(payload, ElectionAnomalyEvidenceScannerStatusIds.Clear) with
        {
            ContentHash = Sha256Reference('c'),
        };

        var result = await sut.ApplyTrustedScannerHandoffAsync(request);

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentHashInvalid);
        VerifyNoStatusUpdate(storage);
    }

    [Fact]
    public async Task ApplyTrustedScannerHandoffAsync_WithPendingStatus_RejectsBeforeLookup()
    {
        var payload = CreatePayload();
        var storage = new Mock<IElectionAnomalyRestrictedPayloadStorageService>(MockBehavior.Strict);
        var sut = new ElectionAnomalyTrustedScannerOrchestrationService(storage.Object);

        var result = await sut.ApplyTrustedScannerHandoffAsync(CreateRequest(
            payload,
            ElectionAnomalyEvidenceScannerStatusIds.Pending));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid);
        storage.Verify(
            x => x.GetMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTrustedScannerHandoffAsync_WithQuarantinedPayloadAndClearResult_RejectsWithoutStatusUpdate()
    {
        var payload = CreatePayload() with
        {
            ScannerStatusId = ElectionAnomalyEvidenceScannerStatusIds.Quarantined,
            PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined,
        };
        var (sut, storage) = CreateService(payload);

        var result = await sut.ApplyTrustedScannerHandoffAsync(CreateRequest(
            payload,
            ElectionAnomalyEvidenceScannerStatusIds.Clear));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid);
        VerifyNoStatusUpdate(storage);
    }

    private static (ElectionAnomalyTrustedScannerOrchestrationService Sut, Mock<IElectionAnomalyRestrictedPayloadStorageService> Storage) CreateService(
        ElectionAnomalyRestrictedPayloadRecord payload)
    {
        var storage = new Mock<IElectionAnomalyRestrictedPayloadStorageService>(MockBehavior.Strict);
        storage
            .Setup(x => x.GetMetadataAsync(payload.PayloadReference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);

        return (new ElectionAnomalyTrustedScannerOrchestrationService(storage.Object), storage);
    }

    private static ElectionAnomalyTrustedScannerHandoffRequest CreateRequest(
        ElectionAnomalyRestrictedPayloadRecord payload,
        string scannerStatusId) =>
        new(
            payload.ElectionId,
            payload.PayloadReference,
            payload.EncryptedPayloadHash,
            payload.ContentHash,
            scannerStatusId,
            "trusted-scanner",
            Guid.NewGuid().ToString("N"));

    private static ElectionAnomalyRestrictedPayloadRecord CreatePayload()
    {
        var payloadId = Guid.NewGuid();

        return new ElectionAnomalyRestrictedPayloadRecord(
            payloadId,
            ElectionId.NewElectionId,
            Guid.NewGuid(),
            ElectionAnomalyRestrictedPayloadReferences.Create(payloadId),
            [1, 2, 3, 4],
            Sha256Reference('a'),
            Sha256Reference('b'),
            4,
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            ElectionAnomalyEvidenceScannerStatusIds.Pending,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            DateTime.UtcNow);
    }

    private static string Sha256Reference(char value) =>
        $"sha256:{new string(value, 64)}";

    private static void VerifyNoStatusUpdate(Mock<IElectionAnomalyRestrictedPayloadStorageService> storage)
    {
        storage.Verify(
            x => x.MarkScannerStatusAsync(It.IsAny<ElectionAnomalyRestrictedPayloadScannerStatusRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        storage.Verify(
            x => x.MarkQuarantinedAsync(It.IsAny<ElectionAnomalyRestrictedPayloadQuarantineRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

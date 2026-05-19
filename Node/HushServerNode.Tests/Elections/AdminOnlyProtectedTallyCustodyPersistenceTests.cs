using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

[Trait("Category", "FEAT-131")]
[Trait("Category", "HV-KMS-CUSTODY")]
public class AdminOnlyProtectedTallyCustodyPersistenceTests
{
    [Fact]
    public void CustodyModel_ShouldDefineLookupAndReconciliationIndexes()
    {
        using var context = CreateContext();

        FindIndex(
                context,
                typeof(ElectionAdminOnlyProtectedTallyEnvelopeRecord),
                "ElectionId",
                "SelectedProfileId")
            .Should()
            .NotBeNull();
        FindIndex(
                context,
                typeof(ElectionAdminOnlyProtectedTallyEnvelopeRecord),
                "CustodyMode",
                "CustodyLifecycleState")
            .Should()
            .NotBeNull();
        FindIndex(context, typeof(ElectionAdminOnlyProtectedTallyEnvelopeRecord), "KmsAlias")
            .Should()
            .NotBeNull();
    }

    [Fact]
    public async Task AdminOnlyCustodyRecord_ShouldRoundTripLifecycleFields()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var recordedAt = new DateTime(2026, 5, 19, 1, 0, 0, DateTimeKind.Utc);
        var envelope = CreatePerElectionEnvelope(
            electionId,
            "admin-prod-1of1",
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenReady,
            recordedAt);

        await repository.SaveAdminOnlyProtectedTallyEnvelopeAsync(envelope);
        await context.SaveChangesAsync();

        var saved = await repository.GetAdminOnlyProtectedTallyEnvelopeAsync(electionId, "admin-prod-1of1");

        saved.Should().NotBeNull();
        saved!.CustodyMode.Should().Be(ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1);
        saved.CustodyProvider.Should().Be("aws-kms");
        saved.CustodyProviderProfile.Should().Be("admin-prod-eu-central-1");
        saved.KmsKeyId.Should().Be("key-admin-prod-1of1");
        saved.KmsAlias.Should().Be("alias/hush-election/admin-prod-1of1");
        saved.KmsRegion.Should().Be("eu-central-1");
        saved.KmsAccountBoundary.Should().Be("aws-account:111122223333");
        saved.KmsTagsVerifiedAt.Should().Be(recordedAt.AddSeconds(2));
        saved.EncryptionContextHash.Should().Be("sha256:context-admin-prod-1of1");
        saved.CustodyLifecycleState.Should().Be(ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenReady);
        saved.CustodyRetryCount.Should().Be(1);
        saved.LastReconciledAt.Should().Be(recordedAt.AddSeconds(3));
        saved.PublicCustodyReferenceHash.Should().Be("sha256:public-admin-prod-1of1");
        saved.SealedEnvelopeHash.Should().Be("sha256:sealed-admin-prod-1of1");
    }

    [Fact]
    public async Task ReconciliationQuery_ShouldReturnPerElectionRowsThatNeedRepair()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var oldReconciledAt = new DateTime(2026, 5, 19, 1, 0, 0, DateTimeKind.Utc);
        var staleBefore = oldReconciledAt.AddHours(1);
        var retry = CreatePerElectionEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-retry",
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired,
            oldReconciledAt);
        var keyDisabled = CreatePerElectionEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-disabled",
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.KeyDisabled,
            oldReconciledAt);
        var deletionScheduled = CreatePerElectionEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-done",
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled,
            oldReconciledAt);
        var legacy = ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            ElectionId.NewElectionId,
            "admin-prod-legacy",
            [0x01],
            "fingerprint-legacy",
            "sealed",
            "scalar-decimal-v1",
            "aws-kms-v1");

        await repository.SaveAdminOnlyProtectedTallyEnvelopeAsync(retry);
        await repository.SaveAdminOnlyProtectedTallyEnvelopeAsync(keyDisabled);
        await repository.SaveAdminOnlyProtectedTallyEnvelopeAsync(deletionScheduled);
        await repository.SaveAdminOnlyProtectedTallyEnvelopeAsync(legacy);
        await context.SaveChangesAsync();

        var reconciliationRows = await repository
            .GetAdminOnlyProtectedTallyEnvelopesForCustodyReconciliationAsync(staleBefore);

        reconciliationRows.Select(x => x.SelectedProfileId)
            .Should()
            .BeEquivalentTo("admin-prod-retry", "admin-prod-disabled");
        reconciliationRows.Should().NotContain(x =>
            x.ResolveCustodyLifecycleState() == ElectionAdminOnlyProtectedTallyCustodyLifecycleState.LegacyStaticKms);
        reconciliationRows.Should().NotContain(x =>
            x.CustodyLifecycleState == ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled);
    }

    private static ElectionAdminOnlyProtectedTallyEnvelopeRecord CreatePerElectionEnvelope(
        ElectionId electionId,
        string selectedProfileId,
        ElectionAdminOnlyProtectedTallyCustodyLifecycleState lifecycleState,
        DateTime recordedAt) =>
        ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
            electionId,
            selectedProfileId,
            [0x01, 0x02],
            $"fingerprint-{selectedProfileId}",
            "sealed",
            "scalar-decimal-v1",
            "aws-kms-per-election-v1",
            sealedByServiceIdentity: "kms-runtime-role",
            createdAt: recordedAt,
            custodyMetadata: new ElectionAdminOnlyProtectedTallyCustodyMetadata(
                CustodyMode: ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
                CustodyProvider: "aws-kms",
                CustodyProviderProfile: "admin-prod-eu-central-1",
                KmsKeyId: $"key-{selectedProfileId}",
                KmsKeyArn: $"arn:aws:kms:eu-central-1:111122223333:key/{selectedProfileId}",
                KmsAlias: $"alias/hush-election/{selectedProfileId}",
                KmsRegion: "eu-central-1",
                KmsAccountBoundary: "aws-account:111122223333",
                KmsTagSetHash: $"sha256:tags-{selectedProfileId}",
                KmsTagsVerifiedAt: recordedAt.AddSeconds(2),
                EncryptionContextVersion: "admin-only-protected-tally-v1",
                EncryptionContextHash: $"sha256:context-{selectedProfileId}",
                CustodyLifecycleState: lifecycleState,
                CustodyLastAction: "reconcile",
                CustodyRetryCount: 1,
                LastReconciledAt: recordedAt.AddSeconds(3),
                KmsKeyCreatedAt: recordedAt.AddSeconds(1),
                DeletionWindowDays: 7,
                PublicCustodyReferenceHash: $"sha256:public-{selectedProfileId}",
                SealedEnvelopeHash: $"sha256:sealed-{selectedProfileId}"));

    private static Microsoft.EntityFrameworkCore.Metadata.IIndex FindIndex(
        ElectionsDbContext context,
        Type entityType,
        params string[] propertyNames)
    {
        var entity = context.Model.FindEntityType(entityType);

        entity.Should().NotBeNull();

        return entity!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static ElectionsRepository CreateRepository(ElectionsDbContext context)
    {
        var repository = new ElectionsRepository();
        repository.SetContext(context);
        return repository;
    }

    private static ElectionsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ElectionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ElectionsDbContext(new ElectionsDbContextConfigurator(), options);
    }
}

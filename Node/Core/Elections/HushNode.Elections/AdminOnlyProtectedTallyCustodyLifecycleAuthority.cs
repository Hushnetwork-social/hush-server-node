using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using HushNode.Reactions.Crypto;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public sealed record AdminOnlyProtectedTallyCustodyOpenResult(
    bool IsSuccess,
    ElectionAdminOnlyProtectedTallyEnvelopeRecord? EnvelopeToPersist,
    ElectionCeremonyBindingSnapshot? Snapshot,
    string Error)
{
    public static AdminOnlyProtectedTallyCustodyOpenResult Success(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        ElectionCeremonyBindingSnapshot snapshot) =>
        new(true, envelope, snapshot, string.Empty);

    public static AdminOnlyProtectedTallyCustodyOpenResult Failure(
        string error,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelopeToPersist = null) =>
        new(false, envelopeToPersist, null, string.IsNullOrWhiteSpace(error)
            ? "Admin-only protected tally custody preparation failed."
            : error.Trim());
}

public sealed record AdminOnlyProtectedTallyCustodyFinalizationCleanupResult(
    bool Handled,
    ElectionAdminOnlyProtectedTallyEnvelopeRecord? EnvelopeToPersist,
    string Error)
{
    public static AdminOnlyProtectedTallyCustodyFinalizationCleanupResult NotHandled { get; } =
        new(false, null, string.Empty);

    public static AdminOnlyProtectedTallyCustodyFinalizationCleanupResult HandledSuccessfully(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        new(true, envelope, string.Empty);

    public static AdminOnlyProtectedTallyCustodyFinalizationCleanupResult HandledWithRetry(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        string error) =>
        new(true, envelope, string.IsNullOrWhiteSpace(error)
            ? "Admin-only protected tally custody cleanup requires retry."
            : error.Trim());
}

public interface IAdminOnlyProtectedTallyCustodyLifecycleAuthority
{
    bool RequiresPerElectionCustody(ElectionRecord election, ElectionCeremonyProfileRecord selectedProfile);

    bool EvaluateOpenReadiness(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        out string error);

    AdminOnlyProtectedTallyCustodyOpenResult PrepareOpenCustody(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? existingEnvelope,
        IBabyJubJub curve,
        DateTime openedAt);

    AdminOnlyProtectedTallyCustodyFinalizationCleanupResult BuildFinalizationCleanup(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        DateTime destroyedAt);
}

public static class AdminOnlyProtectedTallyCustodyLifecycleAuthorityFactory
{
    public static IAdminOnlyProtectedTallyCustodyLifecycleAuthority Create(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions? options = null)
    {
        var resolvedOptions = options ?? AdminOnlyProtectedTallyEnvelopeCryptoOptions.Default;
        return resolvedOptions.NormalizedProvider switch
        {
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKmsPerElection =>
                new AwsKmsPerElectionAdminOnlyProtectedTallyCustodyLifecycleAuthority(
                    resolvedOptions,
                    AdminOnlyProtectedTallyEnvelopeCryptoFactory.CreateAwsKmsClient(resolvedOptions),
                    disposeClient: true),
            _ => NoOpAdminOnlyProtectedTallyCustodyLifecycleAuthority.Instance,
        };
    }
}

public sealed class NoOpAdminOnlyProtectedTallyCustodyLifecycleAuthority :
    IAdminOnlyProtectedTallyCustodyLifecycleAuthority
{
    public static NoOpAdminOnlyProtectedTallyCustodyLifecycleAuthority Instance { get; } = new();

    private NoOpAdminOnlyProtectedTallyCustodyLifecycleAuthority()
    {
    }

    public bool RequiresPerElectionCustody(ElectionRecord election, ElectionCeremonyProfileRecord selectedProfile) =>
        false;

    public bool EvaluateOpenReadiness(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        out string error)
    {
        error = string.Empty;
        return true;
    }

    public AdminOnlyProtectedTallyCustodyOpenResult PrepareOpenCustody(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? existingEnvelope,
        IBabyJubJub curve,
        DateTime openedAt) =>
        AdminOnlyProtectedTallyCustodyOpenResult.Failure(
            "Per-election admin-only protected tally custody is not enabled.");

    public AdminOnlyProtectedTallyCustodyFinalizationCleanupResult BuildFinalizationCleanup(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        DateTime destroyedAt) =>
        AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.NotHandled;
}

public sealed class TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority :
    IAdminOnlyProtectedTallyCustodyLifecycleAuthority
{
    private readonly TransparentTestAdminOnlyProtectedTallyEnvelopeCrypto _crypto = new();
    private readonly int _deletionWindowDays;

    public TransparentTestAdminOnlyProtectedTallyCustodyLifecycleAuthority(
        bool ready = true,
        bool failOpen = false,
        string? openFailureMessage = null,
        int deletionWindowDays = 7)
    {
        Ready = ready;
        FailOpen = failOpen;
        OpenFailureMessage = string.IsNullOrWhiteSpace(openFailureMessage)
            ? "Fake admin-only protected tally custody provider was configured to fail open preparation."
            : openFailureMessage.Trim();
        _deletionWindowDays = Math.Max(1, deletionWindowDays);
    }

    public bool Ready { get; set; }

    public bool FailOpen { get; set; }

    public bool DetectExistingEnvelopeDrift { get; set; }

    public string OpenFailureMessage { get; }

    public int ReadinessCheckCount { get; private set; }

    public int PrepareOpenCount { get; private set; }

    public int CreatedEnvelopeCount { get; private set; }

    public int FinalizationCleanupCount { get; private set; }

    public bool RequiresPerElectionCustody(ElectionRecord election, ElectionCeremonyProfileRecord selectedProfile) =>
        election.GovernanceMode == ElectionGovernanceMode.AdminOnly && !selectedProfile.DevOnly;

    public bool EvaluateOpenReadiness(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        out string error)
    {
        ReadinessCheckCount++;
        if (!Ready)
        {
            error = "Fake admin-only protected tally custody provider is not ready.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public AdminOnlyProtectedTallyCustodyOpenResult PrepareOpenCustody(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? existingEnvelope,
        IBabyJubJub curve,
        DateTime openedAt)
    {
        PrepareOpenCount++;
        if (!Ready)
        {
            return AdminOnlyProtectedTallyCustodyOpenResult.Failure(
                "Fake admin-only protected tally custody provider is not ready.");
        }

        if (TryReuseExistingEnvelope(
                election,
                selectedProfile,
                existingEnvelope,
                curve,
                openedAt,
                out var reuseResult))
        {
            return reuseResult;
        }

        var metadata = BuildMetadata(
            election,
            selectedProfile.ProfileId,
            ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound,
            "open-create",
            openedAt);

        if (!ElectionProtectedTallyBinding.TryCreateAdminOnlyProtectedTallyEnvelope(
                election,
                _crypto,
                curve,
                out var envelope,
                out var snapshot,
                out var error,
                createdAt: openedAt,
                custodyMetadata: metadata))
        {
            return AdminOnlyProtectedTallyCustodyOpenResult.Failure(error);
        }

        CreatedEnvelopeCount++;
        var preparedEnvelope = DecorateEnvelope(
            envelope!,
            openedAt,
            FailOpen
                ? ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired
                : ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound,
            FailOpen ? "open-failed" : "open-create",
            FailOpen ? "FAKE_PROVIDER_FAILURE" : null,
            FailOpen ? OpenFailureMessage : null);

        return FailOpen
            ? AdminOnlyProtectedTallyCustodyOpenResult.Failure(OpenFailureMessage, preparedEnvelope)
            : AdminOnlyProtectedTallyCustodyOpenResult.Success(preparedEnvelope, snapshot!);
    }

    public AdminOnlyProtectedTallyCustodyFinalizationCleanupResult BuildFinalizationCleanup(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        DateTime destroyedAt)
    {
        if (!envelope.HasPerElectionKmsCustody)
        {
            return AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.NotHandled;
        }

        FinalizationCleanupCount++;
        var destroyed = envelope with
        {
            SealedTallyPrivateScalar = AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
            DestroyedAt = destroyedAt,
            LastUpdatedAt = destroyedAt,
            CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled,
            CustodyLastAction = "finalization-cleanup",
            CustodyLastErrorCode = null,
            CustodyLastErrorMessage = null,
            KmsKeyDisabledAt = destroyedAt,
            KmsDeletionScheduledAt = destroyedAt,
            KmsDeletionDate = destroyedAt.AddDays(_deletionWindowDays),
            DeletionWindowDays = _deletionWindowDays,
            CustodyActionServiceIdentity = "test-host",
        };

        return AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.HandledSuccessfully(destroyed);
    }

    private bool TryReuseExistingEnvelope(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? existingEnvelope,
        IBabyJubJub curve,
        DateTime openedAt,
        out AdminOnlyProtectedTallyCustodyOpenResult result)
    {
        result = AdminOnlyProtectedTallyCustodyOpenResult.Failure("No reusable envelope exists.");
        if (existingEnvelope is null ||
            !existingEnvelope.HasPerElectionKmsCustody ||
            existingEnvelope.DestroyedAt.HasValue ||
            string.Equals(
                existingEnvelope.SealedTallyPrivateScalar,
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal) ||
            !string.Equals(existingEnvelope.SelectedProfileId, selectedProfile.ProfileId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!ElectionProtectedTallyBinding.TryBuildAdminOnlyProtectedTallyBindingSnapshot(
                election,
                existingEnvelope,
                curve,
                out var snapshot,
                out _))
        {
            return false;
        }

        if (DetectExistingEnvelopeDrift)
        {
            var driftEnvelope = DecorateEnvelope(
                existingEnvelope,
                openedAt,
                ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired,
                "open-drift-detected",
                "FAKE_CUSTODY_DRIFT_DETECTED",
                "Fake admin-only protected tally custody provider detected drift in the existing custody row.");
            result = AdminOnlyProtectedTallyCustodyOpenResult.Failure(
                "Fake admin-only protected tally custody provider detected drift in the existing custody row.",
                driftEnvelope);
            return true;
        }

        var reusedEnvelope = DecorateEnvelope(
            existingEnvelope,
            openedAt,
            FailOpen
                ? ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired
                : ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound,
            FailOpen ? "open-retry-failed" : "open-reuse",
            FailOpen ? "FAKE_PROVIDER_FAILURE" : null,
            FailOpen ? OpenFailureMessage : null);

        result = FailOpen
            ? AdminOnlyProtectedTallyCustodyOpenResult.Failure(OpenFailureMessage, reusedEnvelope)
            : AdminOnlyProtectedTallyCustodyOpenResult.Success(reusedEnvelope, snapshot!);
        return true;
    }

    private ElectionAdminOnlyProtectedTallyCustodyMetadata BuildMetadata(
        ElectionRecord election,
        string selectedProfileId,
        ElectionAdminOnlyProtectedTallyCustodyLifecycleState lifecycleState,
        string action,
        DateTime recordedAt)
    {
        var keySuffix = AdminOnlyProtectedTallyKmsCustodyContract.ComputeShortHash(
            $"{election.ElectionId}:{selectedProfileId}");
        var alias = $"alias/hush-voting/admin-only/test/{keySuffix}";
        var context = AdminOnlyProtectedTallyKmsCustodyContract.BuildEncryptionContext(
            election.ElectionId,
            selectedProfileId);

        return new ElectionAdminOnlyProtectedTallyCustodyMetadata(
            CustodyMode: ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
            CustodyProvider: "fake-kms",
            CustodyProviderProfile: "transparent-test",
            KmsKeyId: $"fake-key-{keySuffix}",
            KmsKeyArn: $"arn:aws:kms:fake-region:000000000000:key/fake-key-{keySuffix}",
            KmsAlias: alias,
            KmsRegion: "fake-region",
            KmsAccountBoundary: "aws-account:000000000000",
            KmsTagSetHash: AdminOnlyProtectedTallyKmsCustodyContract.ComputeCanonicalHash(
                AdminOnlyProtectedTallyKmsCustodyContract.BuildTags(election, selectedProfileId)
                    .ToDictionary(x => x.TagKey, x => x.TagValue, StringComparer.Ordinal)),
            KmsTagsVerifiedAt: recordedAt,
            EncryptionContextVersion: AdminOnlyProtectedTallyKmsCustodyContract.EncryptionContextVersion,
            EncryptionContextHash: AdminOnlyProtectedTallyKmsCustodyContract.ComputeCanonicalHash(context),
            CustodyLifecycleState: lifecycleState,
            CustodyLastAction: action,
            KmsKeyCreatedAt: recordedAt,
            DeletionWindowDays: _deletionWindowDays,
            CustodyActionServiceIdentity: "test-host",
            PublicCustodyReferenceHash: AdminOnlyProtectedTallyKmsCustodyContract.ComputePublicCustodyReferenceHash(
                election.ElectionId,
                alias,
                $"fake-key-{keySuffix}"));
    }

    private static ElectionAdminOnlyProtectedTallyEnvelopeRecord DecorateEnvelope(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        DateTime recordedAt,
        ElectionAdminOnlyProtectedTallyCustodyLifecycleState lifecycleState,
        string action,
        string? errorCode,
        string? errorMessage) =>
        envelope with
        {
            CustodyLifecycleState = lifecycleState,
            CustodyLastAction = action,
            CustodyLastErrorCode = errorCode,
            CustodyLastErrorMessage = errorMessage,
            CustodyRetryCount = errorCode is null
                ? envelope.CustodyRetryCount
                : envelope.CustodyRetryCount + 1,
            CustodyNextRetryAt = errorCode is null ? null : recordedAt.AddMinutes(5),
            LastUpdatedAt = recordedAt,
            SealedEnvelopeHash = AdminOnlyProtectedTallyKmsCustodyContract.ComputeTextHash(
                envelope.SealedTallyPrivateScalar),
        };
}

public sealed class AwsKmsPerElectionAdminOnlyProtectedTallyCustodyLifecycleAuthority :
    IAdminOnlyProtectedTallyCustodyLifecycleAuthority,
    IDisposable
{
    private readonly AdminOnlyProtectedTallyEnvelopeCryptoOptions _options;
    private readonly IAmazonKeyManagementService _kmsClient;
    private readonly bool _disposeClient;

    public AwsKmsPerElectionAdminOnlyProtectedTallyCustodyLifecycleAuthority(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions options,
        IAmazonKeyManagementService kmsClient,
        bool disposeClient = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kmsClient = kmsClient ?? throw new ArgumentNullException(nameof(kmsClient));
        _disposeClient = disposeClient;
    }

    public bool RequiresPerElectionCustody(ElectionRecord election, ElectionCeremonyProfileRecord selectedProfile) =>
        election.GovernanceMode == ElectionGovernanceMode.AdminOnly &&
        !selectedProfile.DevOnly &&
        string.Equals(
            _options.NormalizedProvider,
            AdminOnlyProtectedTallyEnvelopeCryptoOptions.ProviderAwsKmsPerElection,
            StringComparison.Ordinal);

    public bool EvaluateOpenReadiness(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        out string error)
    {
        if (!RequiresPerElectionCustody(election, selectedProfile))
        {
            error = string.Empty;
            return true;
        }

        error = string.Empty;
        return true;
    }

    public AdminOnlyProtectedTallyCustodyOpenResult PrepareOpenCustody(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? existingEnvelope,
        IBabyJubJub curve,
        DateTime openedAt)
    {
        if (TryReuseExistingEnvelope(
                election,
                selectedProfile,
                existingEnvelope,
                curve,
                openedAt,
                out var reuseResult))
        {
            return reuseResult;
        }

        try
        {
            var keyDescriptor = EnsurePerElectionKey(election, selectedProfile.ProfileId, openedAt);
            var crypto = new AwsKmsAdminOnlyProtectedTallyEnvelopeCrypto(
                _options with { AwsKmsKeyId = keyDescriptor.KeyId },
                _kmsClient);
            var metadata = BuildMetadata(
                election,
                selectedProfile.ProfileId,
                keyDescriptor,
                ElectionAdminOnlyProtectedTallyCustodyLifecycleState.KeyCreated,
                "open-create-key",
                openedAt);

            if (!ElectionProtectedTallyBinding.TryCreateAdminOnlyProtectedTallyEnvelope(
                    election,
                    crypto,
                    curve,
                    out var envelope,
                    out var snapshot,
                    out var error,
                    createdAt: openedAt,
                    custodyMetadata: metadata))
            {
                return AdminOnlyProtectedTallyCustodyOpenResult.Failure(error);
            }

            var createdEnvelope = envelope!;
            if (!ElectionProtectedTallyBinding.TryUnsealAdminOnlyProtectedTallyScalar(
                    election,
                    createdEnvelope,
                    crypto,
                    curve,
                    out _,
                    out error))
            {
                return AdminOnlyProtectedTallyCustodyOpenResult.Failure(
                    $"Admin-only protected tally custody decrypt authority proof failed: {error}",
                    createdEnvelope with
                    {
                        CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired,
                        CustodyLastAction = "open-decrypt-proof",
                        CustodyLastErrorCode = "DECRYPT_AUTHORITY_PROOF_FAILED",
                        CustodyLastErrorMessage = error,
                        CustodyRetryCount = createdEnvelope.CustodyRetryCount + 1,
                        CustodyNextRetryAt = openedAt.AddMinutes(5),
                        LastUpdatedAt = openedAt,
                    });
            }

            var openBoundEnvelope = createdEnvelope with
            {
                CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound,
                CustodyLastAction = "open-bound",
                CustodyLastErrorCode = null,
                CustodyLastErrorMessage = null,
                CustodyNextRetryAt = null,
                LastUpdatedAt = openedAt,
                SealedEnvelopeHash = AdminOnlyProtectedTallyKmsCustodyContract.ComputeTextHash(
                    createdEnvelope.SealedTallyPrivateScalar),
            };

            return AdminOnlyProtectedTallyCustodyOpenResult.Success(openBoundEnvelope, snapshot!);
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException or InvalidOperationException)
        {
            return AdminOnlyProtectedTallyCustodyOpenResult.Failure(
                $"AWS KMS per-election admin-only protected tally custody failed: {ex.Message}");
        }
    }

    public AdminOnlyProtectedTallyCustodyFinalizationCleanupResult BuildFinalizationCleanup(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        DateTime destroyedAt)
    {
        if (!envelope.HasPerElectionKmsCustody)
        {
            return AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.NotHandled;
        }

        var destroyedEnvelope = envelope with
        {
            SealedTallyPrivateScalar = AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
            DestroyedAt = destroyedAt,
            LastUpdatedAt = destroyedAt,
            CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ScalarDestroyed,
            CustodyLastAction = "finalization-scalar-destroyed",
            CustodyLastErrorCode = null,
            CustodyLastErrorMessage = null,
            CustodyActionServiceIdentity = ResolveServiceIdentity(envelope.KmsKeyId),
        };

        try
        {
            if (string.IsNullOrWhiteSpace(envelope.KmsKeyId))
            {
                return AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.HandledWithRetry(
                    destroyedEnvelope with
                    {
                        CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired,
                        CustodyLastAction = "finalization-kms-missing-key-id",
                        CustodyLastErrorCode = "KMS_KEY_ID_MISSING",
                        CustodyLastErrorMessage = "Per-election KMS cleanup cannot disable or schedule deletion without the key id.",
                        CustodyRetryCount = destroyedEnvelope.CustodyRetryCount + 1,
                        CustodyNextRetryAt = destroyedAt.AddMinutes(5),
                    },
                    "Per-election KMS cleanup cannot disable or schedule deletion without the key id.");
            }

            _kmsClient.DisableKeyAsync(new DisableKeyRequest
            {
                KeyId = envelope.KmsKeyId,
            }).GetAwaiter().GetResult();

            var disabledEnvelope = destroyedEnvelope with
            {
                CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.KeyDisabled,
                CustodyLastAction = "finalization-kms-disable",
                KmsKeyDisabledAt = destroyedAt,
            };

            var deletionWindowDays = ResolveDeletionWindowDays(envelope);
            var deletionResponse = _kmsClient.ScheduleKeyDeletionAsync(new ScheduleKeyDeletionRequest
            {
                KeyId = envelope.KmsKeyId,
                PendingWindowInDays = deletionWindowDays,
            }).GetAwaiter().GetResult();

            var deletionScheduledEnvelope = disabledEnvelope with
            {
                CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled,
                CustodyLastAction = "finalization-kms-schedule-deletion",
                KmsDeletionScheduledAt = destroyedAt,
                KmsDeletionDate = deletionResponse.DeletionDate == default
                    ? destroyedAt.AddDays(deletionWindowDays)
                    : deletionResponse.DeletionDate,
                DeletionWindowDays = deletionWindowDays,
            };

            return AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.HandledSuccessfully(
                deletionScheduledEnvelope);
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException or InvalidOperationException)
        {
            var retryEnvelope = destroyedEnvelope with
            {
                CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired,
                CustodyLastAction = "finalization-kms-cleanup-retry",
                CustodyLastErrorCode = "KMS_FINALIZATION_CLEANUP_FAILED",
                CustodyLastErrorMessage = ex.Message,
                CustodyRetryCount = destroyedEnvelope.CustodyRetryCount + 1,
                CustodyNextRetryAt = destroyedAt.AddMinutes(5),
            };

            return AdminOnlyProtectedTallyCustodyFinalizationCleanupResult.HandledWithRetry(
                retryEnvelope,
                $"AWS KMS per-election cleanup requires retry: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _kmsClient.Dispose();
        }
    }

    private bool TryReuseExistingEnvelope(
        ElectionRecord election,
        ElectionCeremonyProfileRecord selectedProfile,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? existingEnvelope,
        IBabyJubJub curve,
        DateTime openedAt,
        out AdminOnlyProtectedTallyCustodyOpenResult result)
    {
        result = AdminOnlyProtectedTallyCustodyOpenResult.Failure("No reusable envelope exists.");
        if (existingEnvelope is null ||
            !existingEnvelope.HasPerElectionKmsCustody ||
            existingEnvelope.DestroyedAt.HasValue ||
            string.Equals(
                existingEnvelope.SealedTallyPrivateScalar,
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal) ||
            !string.Equals(existingEnvelope.SelectedProfileId, selectedProfile.ProfileId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!ElectionProtectedTallyBinding.TryBuildAdminOnlyProtectedTallyBindingSnapshot(
                election,
                existingEnvelope,
                curve,
                out var snapshot,
                out _))
        {
            return false;
        }

        if (!TryValidateExistingEnvelopeMetadata(
                election,
                selectedProfile.ProfileId,
                existingEnvelope,
                openedAt,
                out result))
        {
            return true;
        }

        var crypto = new AwsKmsPerElectionAdminOnlyProtectedTallyEnvelopeCrypto(_options, _kmsClient);
        if (!ElectionProtectedTallyBinding.TryUnsealAdminOnlyProtectedTallyScalar(
                election,
                existingEnvelope,
                crypto,
                curve,
                out _,
                out var error))
        {
            result = AdminOnlyProtectedTallyCustodyOpenResult.Failure(
                $"Existing per-election admin-only protected tally custody cannot be reused: {error}",
                existingEnvelope with
                {
                    CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired,
                    CustodyLastAction = "open-reuse-decrypt-proof",
                    CustodyLastErrorCode = "EXISTING_CUSTODY_REUSE_FAILED",
                    CustodyLastErrorMessage = error,
                    CustodyRetryCount = existingEnvelope.CustodyRetryCount + 1,
                    CustodyNextRetryAt = openedAt.AddMinutes(5),
                    LastUpdatedAt = openedAt,
                });
            return true;
        }

        result = AdminOnlyProtectedTallyCustodyOpenResult.Success(
            existingEnvelope with
            {
                CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenBound,
                CustodyLastAction = "open-reuse",
                CustodyLastErrorCode = null,
                CustodyLastErrorMessage = null,
                CustodyNextRetryAt = null,
                LastUpdatedAt = openedAt,
            },
            snapshot!);
        return true;
    }

    private static bool TryValidateExistingEnvelopeMetadata(
        ElectionRecord election,
        string selectedProfileId,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord existingEnvelope,
        DateTime openedAt,
        out AdminOnlyProtectedTallyCustodyOpenResult result)
    {
        result = AdminOnlyProtectedTallyCustodyOpenResult.Failure("Existing custody metadata is valid.");
        var expectedAlias = BuildAlias(election.ElectionId, selectedProfileId);
        if (!string.Equals(existingEnvelope.KmsAlias, expectedAlias, StringComparison.Ordinal))
        {
            result = BuildExistingMetadataFailure(
                existingEnvelope,
                openedAt,
                "KMS_ALIAS_MISMATCH",
                "Existing per-election KMS custody alias does not match the election/profile alias contract.");
            return false;
        }

        var expectedTagSetHash = AdminOnlyProtectedTallyKmsCustodyContract.ComputeCanonicalHash(
            AdminOnlyProtectedTallyKmsCustodyContract.BuildTags(election, selectedProfileId)
                .ToDictionary(x => x.TagKey, x => x.TagValue, StringComparer.Ordinal));
        if (!string.Equals(existingEnvelope.KmsTagSetHash, expectedTagSetHash, StringComparison.Ordinal))
        {
            result = BuildExistingMetadataFailure(
                existingEnvelope,
                openedAt,
                "KMS_TAG_SET_MISMATCH",
                "Existing per-election KMS custody tag-set hash does not match the election/profile tag contract.");
            return false;
        }

        var expectedContextHash = AdminOnlyProtectedTallyKmsCustodyContract.ComputeCanonicalHash(
            AdminOnlyProtectedTallyKmsCustodyContract.BuildEncryptionContext(election.ElectionId, selectedProfileId));
        if (!string.Equals(existingEnvelope.EncryptionContextHash, expectedContextHash, StringComparison.Ordinal))
        {
            result = BuildExistingMetadataFailure(
                existingEnvelope,
                openedAt,
                "KMS_ENCRYPTION_CONTEXT_MISMATCH",
                "Existing per-election KMS custody encryption context hash does not match the election/profile context contract.");
            return false;
        }

        return true;
    }

    private static AdminOnlyProtectedTallyCustodyOpenResult BuildExistingMetadataFailure(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord existingEnvelope,
        DateTime openedAt,
        string errorCode,
        string errorMessage) =>
        AdminOnlyProtectedTallyCustodyOpenResult.Failure(
            errorMessage,
            existingEnvelope with
            {
                CustodyLifecycleState = ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired,
                CustodyLastAction = "open-existing-metadata-validation",
                CustodyLastErrorCode = errorCode,
                CustodyLastErrorMessage = errorMessage,
                CustodyRetryCount = existingEnvelope.CustodyRetryCount + 1,
                CustodyNextRetryAt = null,
                LastUpdatedAt = openedAt,
            });

    private PerElectionKmsKeyDescriptor EnsurePerElectionKey(
        ElectionRecord election,
        string selectedProfileId,
        DateTime openedAt)
    {
        var alias = BuildAlias(election.ElectionId, selectedProfileId);
        var tags = AdminOnlyProtectedTallyKmsCustodyContract.BuildTags(election, selectedProfileId);
        var createResponse = _kmsClient.CreateKeyAsync(new CreateKeyRequest
        {
            Description = $"HushVoting admin-only protected tally key for election {election.ElectionId}",
            Tags = tags,
        }).GetAwaiter().GetResult();

        if (createResponse.KeyMetadata is null ||
            string.IsNullOrWhiteSpace(createResponse.KeyMetadata.KeyId))
        {
            throw new InvalidOperationException("AWS KMS did not return a key id for the per-election custody key.");
        }

        _kmsClient.CreateAliasAsync(new CreateAliasRequest
        {
            AliasName = alias,
            TargetKeyId = createResponse.KeyMetadata.KeyId,
        }).GetAwaiter().GetResult();

        var keyArn = createResponse.KeyMetadata.Arn?.Trim();
        var region = ResolveRegion(keyArn);
        var accountBoundary = ResolveAccountBoundary(keyArn);
        return new PerElectionKmsKeyDescriptor(
            createResponse.KeyMetadata.KeyId.Trim(),
            keyArn,
            alias,
            region,
            accountBoundary,
            createResponse.KeyMetadata.CreationDate ?? openedAt,
            AdminOnlyProtectedTallyKmsCustodyContract.ComputeCanonicalHash(
                tags.ToDictionary(x => x.TagKey, x => x.TagValue, StringComparer.Ordinal)));
    }

    private ElectionAdminOnlyProtectedTallyCustodyMetadata BuildMetadata(
        ElectionRecord election,
        string selectedProfileId,
        PerElectionKmsKeyDescriptor keyDescriptor,
        ElectionAdminOnlyProtectedTallyCustodyLifecycleState lifecycleState,
        string action,
        DateTime recordedAt)
    {
        var context = AdminOnlyProtectedTallyKmsCustodyContract.BuildEncryptionContext(
            election.ElectionId,
            selectedProfileId);

        return new ElectionAdminOnlyProtectedTallyCustodyMetadata(
            CustodyMode: ElectionAdminOnlyProtectedTallyCustodyModes.AwsKmsPerElectionEnvelopeV1,
            CustodyProvider: "aws-kms",
            CustodyProviderProfile: _options.CustodyProviderProfile,
            KmsKeyId: keyDescriptor.KeyId,
            KmsKeyArn: keyDescriptor.KeyArn,
            KmsAlias: keyDescriptor.Alias,
            KmsRegion: keyDescriptor.Region,
            KmsAccountBoundary: keyDescriptor.AccountBoundary,
            KmsTagSetHash: keyDescriptor.TagSetHash,
            KmsTagsVerifiedAt: recordedAt,
            EncryptionContextVersion: AdminOnlyProtectedTallyKmsCustodyContract.EncryptionContextVersion,
            EncryptionContextHash: AdminOnlyProtectedTallyKmsCustodyContract.ComputeCanonicalHash(context),
            CustodyLifecycleState: lifecycleState,
            CustodyLastAction: action,
            KmsKeyCreatedAt: keyDescriptor.CreatedAt,
            DeletionWindowDays: NormalizeDeletionWindowDays(_options.AwsKmsDeletionWindowDays),
            CustodyActionServiceIdentity: ResolveServiceIdentity(keyDescriptor.KeyId),
            PublicCustodyReferenceHash: AdminOnlyProtectedTallyKmsCustodyContract.ComputePublicCustodyReferenceHash(
                election.ElectionId,
                keyDescriptor.Alias,
                keyDescriptor.KeyId));
    }

    private int ResolveDeletionWindowDays(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        envelope.DeletionWindowDays
        ?? NormalizeDeletionWindowDays(_options.AwsKmsDeletionWindowDays)
        ?? 30;

    private static int? NormalizeDeletionWindowDays(int? value) =>
        value is >= 7 and <= 30 ? value.Value : null;

    private string? ResolveRegion(string? keyArn)
    {
        if (!string.IsNullOrWhiteSpace(_options.AwsKmsRegion))
        {
            return _options.AwsKmsRegion.Trim();
        }

        var parts = keyArn?.Split(':');
        return parts is { Length: > 3 } && !string.IsNullOrWhiteSpace(parts[3])
            ? parts[3].Trim()
            : null;
    }

    private static string? ResolveAccountBoundary(string? keyArn)
    {
        var parts = keyArn?.Split(':');
        return parts is { Length: > 4 } && !string.IsNullOrWhiteSpace(parts[4])
            ? $"aws-account:{parts[4].Trim()}"
            : null;
    }

    private string? ResolveServiceIdentity(string? keyId) =>
        _options.AwsKmsServiceIdentityLabel?.Trim()
        ?? (string.IsNullOrWhiteSpace(keyId) ? null : $"aws-kms:{keyId.Trim()}");

    private static string BuildAlias(ElectionId electionId, string selectedProfileId)
    {
        var suffix = AdminOnlyProtectedTallyKmsCustodyContract.ComputeShortHash(
            $"{electionId}:{selectedProfileId}");
        var profile = SanitizeAliasSegment(selectedProfileId);
        return $"alias/hush-voting/admin-only/{profile}/{suffix}";
    }

    private static string SanitizeAliasSegment(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? "profile"
            : normalized.Length <= 48
                ? normalized
                : normalized[..48];
    }

    private sealed record PerElectionKmsKeyDescriptor(
        string KeyId,
        string? KeyArn,
        string Alias,
        string? Region,
        string? AccountBoundary,
        DateTime CreatedAt,
        string TagSetHash);
}

public sealed class AwsKmsPerElectionAdminOnlyProtectedTallyEnvelopeCrypto :
    IAdminOnlyProtectedTallyEnvelopeCrypto,
    IDisposable
{
    private readonly AdminOnlyProtectedTallyEnvelopeCryptoOptions _options;
    private readonly IAmazonKeyManagementService _kmsClient;
    private readonly bool _disposeClient;

    public AwsKmsPerElectionAdminOnlyProtectedTallyEnvelopeCrypto(
        AdminOnlyProtectedTallyEnvelopeCryptoOptions options,
        IAmazonKeyManagementService kmsClient,
        bool disposeClient = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kmsClient = kmsClient ?? throw new ArgumentNullException(nameof(kmsClient));
        _disposeClient = disposeClient;
    }

    public string SealAlgorithm => "aws-kms-v1";

    public string? SealedByServiceIdentity =>
        _options.AwsKmsServiceIdentityLabel?.Trim();

    public bool IsAvailable(out string error)
    {
        error = string.Empty;
        return true;
    }

    public string SealPrivateScalar(
        string privateScalar,
        ElectionId electionId,
        string selectedProfileId) =>
        throw new InvalidOperationException(
            "Per-election AWS KMS admin-only protected tally sealing must be performed by the custody lifecycle authority.");

    public string? TryUnsealPrivateScalar(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.HasPerElectionKmsCustody)
        {
            error = "The admin-only protected tally envelope is not marked as per-election AWS KMS custody.";
            return null;
        }

        if (!string.Equals(envelope.SealAlgorithm, SealAlgorithm, StringComparison.Ordinal))
        {
            error = $"Seal algorithm mismatch. Expected {SealAlgorithm} but found {envelope.SealAlgorithm}.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(envelope.SealedTallyPrivateScalar) ||
            string.Equals(
                envelope.SealedTallyPrivateScalar,
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
                StringComparison.Ordinal))
        {
            error = "The per-election AWS KMS admin-only protected tally envelope no longer contains a sealed private scalar.";
            return null;
        }

        try
        {
            var ciphertext = Convert.FromBase64String(envelope.SealedTallyPrivateScalar);
            var request = new DecryptRequest
            {
                CiphertextBlob = new MemoryStream(ciphertext),
                EncryptionContext = AdminOnlyProtectedTallyKmsCustodyContract.BuildEncryptionContext(
                    envelope.ElectionId,
                    envelope.SelectedProfileId),
            };

            if (!string.IsNullOrWhiteSpace(envelope.KmsKeyId))
            {
                request.KeyId = envelope.KmsKeyId;
            }

            var response = _kmsClient.DecryptAsync(request).GetAwaiter().GetResult();
            if (response.Plaintext is null || response.Plaintext.Length == 0)
            {
                error = "AWS KMS returned an empty decrypted per-election tally envelope.";
                return null;
            }

            error = string.Empty;
            return Encoding.UTF8.GetString(response.Plaintext.ToArray());
        }
        catch (FormatException)
        {
            error = "The per-election AWS KMS admin-only protected tally envelope payload is not valid base64.";
            return null;
        }
        catch (Exception ex) when (ex is AmazonKeyManagementServiceException or AmazonServiceException)
        {
            error = $"AWS KMS failed to unseal the per-election admin-only protected tally envelope: {ex.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _kmsClient.Dispose();
        }
    }
}

internal static class AdminOnlyProtectedTallyKmsCustodyContract
{
    public const string EncryptionContextVersion = "admin-only-protected-tally-kms-context-v1";
    private const string ContextPurpose = "hush:elections:admin-only-protected-tally-scalar:v1";

    public static Dictionary<string, string> BuildEncryptionContext(
        ElectionId electionId,
        string selectedProfileId) =>
        new(StringComparer.Ordinal)
        {
            ["hush-purpose"] = ContextPurpose,
            ["election-id"] = electionId.ToString(),
            ["selected-profile-id"] = selectedProfileId.Trim(),
            ["scalar-encoding"] = AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
        };

    public static List<Tag> BuildTags(ElectionRecord election, string selectedProfileId) =>
    [
        new Tag { TagKey = "hush:component", TagValue = "hush-voting" },
        new Tag { TagKey = "hush:purpose", TagValue = "admin-only-protected-tally" },
        new Tag { TagKey = "hush:election-id", TagValue = election.ElectionId.ToString() },
        new Tag { TagKey = "hush:selected-profile-id", TagValue = selectedProfileId.Trim() },
    ];

    public static string ComputeTextHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    public static string ComputeShortHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(digest)[..24].ToLowerInvariant();
    }

    public static string ComputeCanonicalHash(IReadOnlyDictionary<string, string> values)
    {
        var canonical = string.Join(
            "\n",
            values
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{x.Key.Trim()}={x.Value.Trim()}")));
        return ComputeTextHash(canonical);
    }

    public static string ComputePublicCustodyReferenceHash(
        ElectionId electionId,
        string kmsAlias,
        string kmsKeyId) =>
        ComputeTextHash($"{electionId}|{kmsAlias.Trim()}|{kmsKeyId.Trim()}");
}

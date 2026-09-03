using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 Task 3.8 real-PostgreSQL failure-injection TwinTests: bounded retry counts for both
/// operations, concurrency exhaustion, unknown failures never retried, ambiguous-commit
/// reconciliation-read failure, and real SQLSTATE 23514 classification as persistence-invariant.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingFailureInjectionTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private static readonly string DigestA = new('A', 64);
    private static readonly DateTime FixedNowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingFailureInjectionTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class FixedTimeProvider(DateTime fixedUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(DateTime.SpecifyKind(fixedUtc, DateTimeKind.Utc));
    }

    private static LicenceReleaseInstallSpec Spec() => new(V1, DigestA, V1Schema, "hush-server-node-test", "feat013-failure-twin");

    private async Task<string> NewDatabaseAsync(long watermark = 10_000)
    {
        var databaseName = $"feat013_fail_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);

        await using var context = _fixture.CreateContext(databaseName);
        var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
            context,
            Spec(),
            _ => Task.FromResult(watermark),
            CancellationToken.None);
        state.Ready.Should().BeTrue();

        return databaseName;
    }

    private static async Task<AuthenticatedIdentitySubject> NewIdentityAsync(
        LicensingPostgresFixture fixture,
        string databaseName)
    {
        var created = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            $"identity-{Guid.NewGuid():N}",
            20_000,
            out var subject,
            out var error);
        created.Should().BeTrue(error);
        return subject!;
    }

    private static async Task<AuthenticatedIdentitySubject> SeedDirectFreeAsync(
        LicensingPostgresFixture fixture,
        string databaseName)
    {
        var identity = await NewIdentityAsync(fixture, databaseName);
        var result = await ResolveAsync(fixture, databaseName, identity, null);
        result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
        return identity;
    }

    private static async Task<LicenceResolutionResult> ResolveAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject identity,
        LicenceFailureInjection? injection) =>
        await LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
            () => fixture.CreateContext(databaseName),
            LicenceServiceConfiguration.CreateDefault(DigestA),
            identity,
            new FixedTimeProvider(FixedNowUtc),
            telemetry: null,
            CancellationToken.None,
            injection);

    private static async Task<LicenceActivationResult> ActivateAsync(
        LicensingPostgresFixture fixture,
        string databaseName,
        AuthenticatedIdentitySubject identity,
        LicenceActivationCommand command,
        LicenceFailureInjection? injection) =>
        await LicenceEntitlementCoordinator.ActivateHigherPlanAsync(
            () => fixture.CreateContext(databaseName),
            LicenceServiceConfiguration.CreateDefault(DigestA),
            identity,
            command,
            new FixedTimeProvider(FixedNowUtc),
            telemetry: null,
            CancellationToken.None,
            injection);

    private static LicenceActivationCommand Command(string expectedPlan, long revision, string target) =>
        LicenceActivationCommand.TryCreate(
            Guid.NewGuid(), expectedPlan, revision, target, null, out var command, out var error)
            ? command!
            : throw new InvalidOperationException(error);

    private static LicenceFailureInjection TransientOnFirstAttempts(int failCount)
    {
        var calls = 0;
        return new LicenceFailureInjection
        {
            BeforeAttemptAsync = (_, _) =>
            {
                calls++;
                if (calls <= failCount)
                {
                    throw new LicenceTransientConflictException(
                        "simulated recognized race", new InvalidOperationException("inner"));
                }

                return Task.CompletedTask;
            },
            AttemptCounter = () => calls,
        };
    }

    [Fact]
    public async Task Resolution_retries_two_recognized_races_and_succeeds_on_the_third_attempt()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(_fixture, databaseName);
            var injection = TransientOnFirstAttempts(failCount: 2);

            var result = await ResolveAsync(_fixture, databaseName, identity, injection);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(LicenceResolutionOutcome.ProvisionedDefault);
            injection.AttemptCounter!().Should().Be(3);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Resolution_exhausts_after_three_recognized_retries_with_concurrency_exhausted()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(_fixture, databaseName);
            var injection = TransientOnFirstAttempts(failCount: int.MaxValue);

            var result = await ResolveAsync(_fixture, databaseName, identity, injection);

            result.IsSuccess.Should().BeFalse();
            result.StableErrorCode.Should().Be(LicenceEntitlementFailureCodes.ConcurrencyExhausted);
            injection.AttemptCounter!().Should().Be(4); // initial attempt + three retries, then exhausted
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Activation_honors_the_same_bounded_retry_policy()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await SeedDirectFreeAsync(_fixture, databaseName);
            var injection = TransientOnFirstAttempts(failCount: 2);

            var result = await ActivateAsync(
                _fixture, databaseName, identity,
                Command(HushVotingLicencePlanId.DirectFree.Value, 1, HushVotingLicencePlanId.Veritas500.Value),
                injection);

            result.Outcome.Should().Be(LicenceActivationOutcome.Activated);
            injection.AttemptCounter!().Should().Be(3);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Unknown_failure_is_never_retried_and_propagates()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(_fixture, databaseName);
            var calls = 0;
            var injection = new LicenceFailureInjection
            {
                BeforeAttemptAsync = (_, _) =>
                {
                    calls++;
                    throw new InvalidOperationException("unexpected bug (not a recognized race)");
                },
            };

            var act = async () => await ResolveAsync(_fixture, databaseName, identity, injection);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*not a recognized race*");
            calls.Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Reconcile_read_failure_after_ambiguous_commit_returns_storage_unavailable()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var identity = await NewIdentityAsync(_fixture, databaseName);
            var factoryCalls = 0;

            // The first context (the attempt) is healthy; the reconcile's fresh context is
            // unreachable, so authority cannot be established after the ambiguous commit.
            DbContext ContextFactory()
            {
                factoryCalls++;
                return factoryCalls == 1
                    ? _fixture.CreateContext(databaseName)
                    : throw new InvalidOperationException("simulated reconcile read outage");
            }

            var injection = new LicenceFailureInjection
            {
                AfterCommitAsync = (attempt, _) => attempt == 1
                    ? throw new InvalidOperationException("simulated lost response after commit")
                    : Task.CompletedTask,
            };

            var result = await LicenceEntitlementCoordinator.ResolveOrProvisionAsync(
                ContextFactory,
                LicenceServiceConfiguration.CreateDefault(DigestA),
                identity,
                new FixedTimeProvider(FixedNowUtc),
                telemetry: null,
                CancellationToken.None,
                injection);

            result.IsSuccess.Should().BeFalse();
            result.StableErrorCode.Should().Be(LicenceEntitlementFailureCodes.StorageUnavailable);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Real_sqlstate_23514_is_classified_as_persistence_invariant_not_transient()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var db = _fixture.CreateContext(databaseName);

            var subjectRow = new LicenceSubjectEntity
            {
                LicenceSubjectId = Guid.CreateVersion7(),
                SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
                CanonicalPublicSigningAddress = "invariant-probe-identity",
                IdentityCreationBlockIndex = 20_000,
                CreatedAtUtc = FixedNowUtc,
                EntitlementRevision = 0,
            };
            db.Add(subjectRow);

            // Violates CK_LicenceAssignment_LifecycleChangedPair: an active assignment carrying a
            // lifecycle-changed timestamp/reason is invalid (23514 check_violation).
            db.Add(new LicenceAssignmentEntity
            {
                LicenceAssignmentId = Guid.CreateVersion7(),
                LicenceSubjectId = subjectRow.LicenceSubjectId,
                PlanId = HushVotingLicencePlanId.DirectFree.Value,
                AssignedCatalogueVersion = V1,
                AssignedCatalogueDigestSha256 = DigestA,
                LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
                Source = LicencePersistenceVocabulary.SourceDefaultFree,
                EffectiveFromUtc = FixedNowUtc,
                ExpiresAtUtc = null,
                LifecycleChangedAtUtc = FixedNowUtc,
                LifecycleReason = LicenceEntitlementDecisions.ReasonAnnualExpiry,
                PlanFamily = LicencePersistenceVocabulary.PlanFamilyDirect,
                UpgradeRank = 0,
                EligibleVoterCap = 100,
                UnlimitedElectionPolicy = true,
                TermKind = LicencePersistenceVocabulary.TermPerpetual,
                TermYears = 0,
                AllowedGovernanceOptionIds = Array.Empty<string>(),
            });

            var act = async () => await db.SaveChangesAsync();
            var exception = await act.Should().ThrowAsync<DbUpdateException>();

            LicencePostgresFailureClassifier.IsRecognizedTransient(exception.Which).Should().BeFalse();
            LicencePostgresFailureClassifier.IsPersistenceInvariantViolation(exception.Which).Should().BeTrue();
            LicencePostgresFailureClassifier.IsStorageUnavailable(exception.Which).Should().BeFalse();
            LicencePostgresFailureClassifier.ClassifyDbUpdate(exception.Which)
                .Should().Be(LicencePostgresFailureClassifier.ExceptionClassifyResult.PersistenceInvariantViolation);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }
}

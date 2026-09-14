using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// FEAT-013 Task 3.8 unit coverage for the bounded execution policy: first/third/fourth-attempt
/// outcomes, recognized-race retry exhaustion, ambiguous-commit reconciliation (committed,
/// authoritative absence, unreadable state), unknown failures never retried, and bounded backoff.
/// Pure delegate-driven tests; no database required.
/// </summary>
public sealed class LicenceBoundedExecutorUnitTests
{
    private static readonly LicenceTransientConflictException Transient = new(
        "recognized race", new InvalidOperationException("inner"));

    private static readonly LicenceAmbiguousCommitException Ambiguous = new(
        "unknown commit", new InvalidOperationException("inner"));

    private static int TransientThrower(int attempts, int attemptsToFail) =>
        attemptsToFail > 0 && attempts <= attemptsToFail ? throw Transient : 42;

    [Fact]
    public async Task First_attempt_success_never_calls_reconcile_or_retries()
    {
        var attempts = 0;
        var reconciles = 0;

        var result = await LicenceBoundedExecutor.ExecuteAsync<string>(
            _ => { attempts++; return Task.FromResult("ok"); },
            _ => { reconciles++; return Task.FromResult<string?>("discovered"); },
            "resolve",
            telemetry: null,
            CancellationToken.None);

        result.Should().Be("ok");
        attempts.Should().Be(1);
        reconciles.Should().Be(0);
    }

    [Fact]
    public async Task Success_on_the_third_attempt_after_two_recognized_transients()
    {
        var attempts = 0;
        var reconciles = 0;

        var result = await LicenceBoundedExecutor.ExecuteAsync<int?>(
            _ =>
            {
                attempts++;
                return Task.FromResult<int?>(TransientThrower(attempts, attemptsToFail: 2));
            },
            _ => Task.FromResult<int?>(null),
            "resolve",
            telemetry: null,
            CancellationToken.None);

        result.Should().Be(42);
        attempts.Should().Be(3);
        reconciles.Should().Be(0);
    }

    [Fact]
    public async Task Success_on_the_fourth_attempt_after_three_recognized_transients()
    {
        var attempts = 0;

        var result = await LicenceBoundedExecutor.ExecuteAsync<int?>(
            _ =>
            {
                attempts++;
                return Task.FromResult<int?>(TransientThrower(attempts, attemptsToFail: 3));
            },
            _ => Task.FromResult<int?>(null),
            "activate",
            telemetry: null,
            CancellationToken.None);

        result.Should().Be(42);
        attempts.Should().Be(4);
    }

    [Fact]
    public async Task Four_transient_failures_exhaust_the_policy_with_concurrency_exhausted()
    {
        var attempts = 0;

        Func<CancellationToken, Task<int?>> attempt = _ =>
        {
            attempts++;
            throw Transient;
        };

        var act = async () => await LicenceBoundedExecutor.ExecuteAsync<int?>(
            attempt,
            _ => Task.FromResult<int?>(null),
            "activate",
            telemetry: null,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<LicenceExecutionExhaustedException>();
        exception.And.StableCode.Should().Be(LicenceEntitlementFailureCodes.ConcurrencyExhausted);
        attempts.Should().Be(4); // initial attempt + exactly three retries
    }

    [Fact]
    public async Task Ambiguous_commit_with_discovered_state_returns_the_committed_result_once()
    {
        var attempts = 0;
        var reconciles = 0;

        var result = await LicenceBoundedExecutor.ExecuteAsync<string>(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw Ambiguous;
                }

                return Task.FromResult("redo");
            },
            _ =>
            {
                reconciles++;
                return Task.FromResult<string?>("committed");
            },
            "resolve",
            telemetry: null,
            CancellationToken.None);

        result.Should().Be("committed");
        attempts.Should().Be(1);
        reconciles.Should().Be(1);
    }

    [Fact]
    public async Task Ambiguous_commit_with_authoritative_absence_redoes_the_attempt()
    {
        var attempts = 0;
        var reconciles = 0;

        var result = await LicenceBoundedExecutor.ExecuteAsync<string>(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw Ambiguous;
                }

                return Task.FromResult("provisioned");
            },
            _ =>
            {
                reconciles++;
                return Task.FromResult<string?>(null); // authoritative absence
            },
            "resolve",
            telemetry: null,
            CancellationToken.None);

        result.Should().Be("provisioned");
        attempts.Should().Be(2);
        reconciles.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_ambiguous_absence_reports_storage_unavailable_within_the_bound()
    {
        var attempts = 0;
        var reconciles = 0;

        Func<CancellationToken, Task<string>> attempt = _ =>
        {
            attempts++;
            throw Ambiguous;
        };

        var act = async () => await LicenceBoundedExecutor.ExecuteAsync<string>(
            attempt,
            _ =>
            {
                reconciles++;
                return Task.FromResult<string?>(null);
            },
            "activate",
            telemetry: null,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<LicenceExecutionExhaustedException>();
        exception.And.StableCode.Should().Be(LicenceEntitlementFailureCodes.StorageUnavailable);
        attempts.Should().Be(LicenceBoundedExecutor.MaxAmbiguousAbsenceRedos + 1);
        reconciles.Should().Be(LicenceBoundedExecutor.MaxAmbiguousAbsenceRedos + 1);
    }

    [Fact]
    public async Task Ambiguous_commit_with_unreadable_state_reports_storage_unavailable()
    {
        var attempts = 0;

        Func<CancellationToken, Task<string>> attempt = _ =>
        {
            attempts++;
            throw Ambiguous;
        };

        var act = async () => await LicenceBoundedExecutor.ExecuteAsync<string>(
            attempt,
            _ => throw new InvalidOperationException("simulated reconcile read outage"),
            "resolve",
            telemetry: null,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<LicenceExecutionExhaustedException>();
        exception.And.StableCode.Should().Be(LicenceEntitlementFailureCodes.StorageUnavailable);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Unknown_failures_are_never_retried()
    {
        var attempts = 0;

        Func<CancellationToken, Task<string>> attempt = _ =>
        {
            attempts++;
            throw new InvalidOperationException("unexpected bug");
        };

        var act = async () => await LicenceBoundedExecutor.ExecuteAsync<string>(
            attempt,
            _ => Task.FromResult<string?>(null),
            "resolve",
            telemetry: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public void Backoff_is_bounded_and_grows_slowly()
    {
        LicenceBoundedExecutor.BackoffForRetry(1).TotalMilliseconds.Should().BeLessThan(25);
        LicenceBoundedExecutor.BackoffForRetry(3).TotalMilliseconds.Should().BeLessThan(50);
        LicenceBoundedExecutor.BackoffForRetry(0).TotalMilliseconds.Should().BeLessThan(25);
        LicenceBoundedExecutor.MaxTransientRetries.Should().Be(3);
        LicenceBoundedExecutor.MaxAmbiguousAbsenceRedos.Should().Be(3);
    }
}

using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

[Trait("Category", "FEAT-131")]
[Trait("Category", "HV-KMS-CUSTODY")]
public sealed class AdminOnlyProtectedTallyLiveAwsKmsSmokeGuardTests
{
    [Fact]
    public void LiveAwsKmsSmokeGuard_WithoutPositiveGuard_SkipsWithoutAwsActions()
    {
        var plan = LiveAwsKmsSmokePlan.Evaluate(new Dictionary<string, string?>(StringComparer.Ordinal));

        plan.Status.Should().Be(LiveAwsKmsSmokePlanStatus.Skipped);
        plan.ReasonCode.Should().Be("live_aws_kms_guard_not_enabled");
        plan.ShouldCreateDisposableKey.Should().BeFalse();
        plan.ShouldScheduleDeletionInCleanup.Should().BeFalse();
    }

    [Fact]
    public void LiveAwsKmsSmokeGuard_WhenEnabledWithoutCredentials_FailsFastBeforeResources()
    {
        var plan = LiveAwsKmsSmokePlan.Evaluate(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HUSH_ENABLE_LIVE_AWS_KMS_TESTS"] = "true",
            ["AWS_REGION"] = "eu-central-1",
        });

        plan.Status.Should().Be(LiveAwsKmsSmokePlanStatus.FailedFast);
        plan.ReasonCode.Should().Be("live_aws_kms_credentials_missing");
        plan.ShouldCreateDisposableKey.Should().BeFalse();
        plan.ShouldScheduleDeletionInCleanup.Should().BeFalse();
    }

    [Fact]
    public void LiveAwsKmsSmokeGuard_WithCredentials_BuildsDisposableTaggedCleanupPlanWithoutSecretDiagnostics()
    {
        var plan = LiveAwsKmsSmokePlan.Evaluate(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HUSH_ENABLE_LIVE_AWS_KMS_TESTS"] = "true",
            ["AWS_REGION"] = "eu-central-1",
            ["AWS_ACCESS_KEY_ID"] = "AKIA1234567890ABCDEF",
            ["AWS_SECRET_ACCESS_KEY"] = "secret-value-that-must-not-appear",
        });

        plan.Status.Should().Be(LiveAwsKmsSmokePlanStatus.Ready);
        plan.ShouldCreateDisposableKey.Should().BeTrue();
        plan.ShouldScheduleDeletionInCleanup.Should().BeTrue();
        plan.DeletionWindowDays.Should().Be(7);
        plan.Tags.Should().Contain(
            x => x.Key == "hush:purpose" &&
                 x.Value == "admin-only-protected-tally-live-smoke");
        plan.Diagnostics.Values.Should().NotContain("AKIA1234567890ABCDEF");
        plan.Diagnostics.Values.Should().NotContain("secret-value-that-must-not-appear");
        plan.Diagnostics.Should().ContainKey("credential_source");
    }

    private enum LiveAwsKmsSmokePlanStatus
    {
        Skipped,
        FailedFast,
        Ready,
    }

    private sealed record LiveAwsKmsSmokePlan(
        LiveAwsKmsSmokePlanStatus Status,
        string ReasonCode,
        bool ShouldCreateDisposableKey,
        bool ShouldScheduleDeletionInCleanup,
        int? DeletionWindowDays,
        IReadOnlyDictionary<string, string> Tags,
        IReadOnlyDictionary<string, string> Diagnostics)
    {
        public static LiveAwsKmsSmokePlan Evaluate(IReadOnlyDictionary<string, string?> environment)
        {
            if (!environment.TryGetValue("HUSH_ENABLE_LIVE_AWS_KMS_TESTS", out var guard) ||
                !string.Equals(guard, "true", StringComparison.OrdinalIgnoreCase))
            {
                return Skip("live_aws_kms_guard_not_enabled");
            }

            if (!environment.TryGetValue("AWS_REGION", out var region) ||
                string.IsNullOrWhiteSpace(region))
            {
                return FailedFast("live_aws_kms_region_missing");
            }

            var hasAccessKey = environment.TryGetValue("AWS_ACCESS_KEY_ID", out var accessKey) &&
                               !string.IsNullOrWhiteSpace(accessKey);
            var hasSecretKey = environment.TryGetValue("AWS_SECRET_ACCESS_KEY", out var secretKey) &&
                               !string.IsNullOrWhiteSpace(secretKey);
            var hasProfile = environment.TryGetValue("AWS_PROFILE", out var profile) &&
                             !string.IsNullOrWhiteSpace(profile);
            if ((!hasAccessKey || !hasSecretKey) && !hasProfile)
            {
                return FailedFast("live_aws_kms_credentials_missing");
            }

            return new LiveAwsKmsSmokePlan(
                LiveAwsKmsSmokePlanStatus.Ready,
                "live_aws_kms_ready",
                ShouldCreateDisposableKey: true,
                ShouldScheduleDeletionInCleanup: true,
                DeletionWindowDays: 7,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["hush:component"] = "hush-voting",
                    ["hush:purpose"] = "admin-only-protected-tally-live-smoke",
                    ["hush:scope"] = "disposable-local-smoke",
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["guard"] = "enabled",
                    ["region"] = region.Trim(),
                    ["credential_source"] = hasProfile ? "profile" : "environment",
                });
        }

        private static LiveAwsKmsSmokePlan Skip(string reasonCode) =>
            new(
                LiveAwsKmsSmokePlanStatus.Skipped,
                reasonCode,
                ShouldCreateDisposableKey: false,
                ShouldScheduleDeletionInCleanup: false,
                DeletionWindowDays: null,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal));

        private static LiveAwsKmsSmokePlan FailedFast(string reasonCode) =>
            new(
                LiveAwsKmsSmokePlanStatus.FailedFast,
                reasonCode,
                ShouldCreateDisposableKey: false,
                ShouldScheduleDeletionInCleanup: false,
                DeletionWindowDays: null,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["guard"] = "enabled",
                    ["failure"] = reasonCode,
                });
    }
}

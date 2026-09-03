using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

public class LicenceLedgerCoordinatorUnitTests
{
    [Theory]
    [InlineData("hushvoting-licence-catalogue/v1.0.0", "hushvoting-licence-catalogue/v1.0.0", false)]
    [InlineData("hushvoting-licence-catalogue/v1.1.0", "hushvoting-licence-catalogue/v1.0.0", true)]
    [InlineData("hushvoting-licence-catalogue/v1.0.0", "hushvoting-licence-catalogue/v1.1.0", false)]
    [InlineData("hushvoting-licence-catalogue/v2.0.0", "hushvoting-licence-catalogue/v1.9.9", true)]
    [InlineData("hushvoting-licence-catalogue/v1.0.0", "hushvoting-licence-catalogue/v1.0.1", false)]
    public void IsNewer_compares_release_contract_versions_deterministically(string left, string right, bool expected)
    {
        LicenceCatalogueLedgerCoordinator.IsNewer(left, right).Should().Be(expected);
    }

    [Fact]
    public void IsNewer_returns_null_for_unparseable_versions_instead_of_guessing()
    {
        LicenceCatalogueLedgerCoordinator.IsNewer("not-a-version", "hushvoting-licence-catalogue/v1.0.0")
            .Should().BeNull();
        LicenceCatalogueLedgerCoordinator.IsNewer("hushvoting-licence-catalogue/v1.0.0", "garbage")
            .Should().BeNull();
    }

    [Fact]
    public void Failure_codes_are_stable_and_distinct()
    {
        LicenceCatalogueLedgerCoordinator.FailureCatalogueMismatch.Should().Be("catalogue_incompatible");
        LicenceCatalogueLedgerCoordinator.FailureRolloutWatermarkUnavailable.Should().Be("rollout_watermark_unavailable");
    }
}

using System.Reflection;
using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

public sealed class HushVotingLicencePlanIdTests
{
    [Theory]
    [InlineData(HushVotingLicencePlanId.DirectFreeValue)]
    [InlineData(HushVotingLicencePlanId.Veritas500Value)]
    [InlineData(HushVotingLicencePlanId.Veritas2000Value)]
    [InlineData(HushVotingLicencePlanId.Veritas10000Value)]
    [InlineData(HushVotingLicencePlanId.EnterpriseValue)]
    public void KnownPlanIds_ParseFromExternal(string value)
    {
        var id = HushVotingLicencePlanId.FromExternal(value);

        id.IsKnown.Should().BeTrue();
        id.Value.Should().Be(value);
    }

    [Fact]
    public void KnownPlanIds_ContainsExactlyTheFiveV1Plans_InCanonicalOrder()
    {
        HushVotingLicencePlanId.Known.Select(static x => x.Value).Should().Equal(
            HushVotingLicencePlanId.DirectFreeValue,
            HushVotingLicencePlanId.Veritas500Value,
            HushVotingLicencePlanId.Veritas2000Value,
            HushVotingLicencePlanId.Veritas10000Value,
            HushVotingLicencePlanId.EnterpriseValue);
    }

    [Theory]
    [InlineData("hushvoting.direct.paid")]
    [InlineData("hushvoting.veritas.100")]
    [InlineData("hushvoting.veritas.250")]
    [InlineData("hushvoting.veritas.5000")]
    [InlineData("HushVoting! Direct Free")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownPlanId_IsPreservedAsUnsupported_NeverCoerced(string value)
    {
        var id = HushVotingLicencePlanId.FromExternal(value);

        id.IsKnown.Should().BeFalse();
        // Unknown must never become Direct Free or any known value.
        HushVotingLicencePlanId.Known.Should().NotContain(id);
    }

    [Theory]
    [InlineData("hushvoting.direct.free", "hushvoting.direct.free")]
    [InlineData("hushvoting.veritas.500", "hushvoting.veritas.500")]
    public void PlanId_ComparisonIsOrdinalAndCultureIndependent(string a, string b)
    {
        var left = HushVotingLicencePlanId.FromExternal(a);
        var right = HushVotingLicencePlanId.FromExternal(b);

        (left == right).Should().BeTrue();
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void PlanId_CaseIsSignificant_Ordinal()
    {
        var lower = HushVotingLicencePlanId.FromExternal("hushvoting.direct.free");
        var upper = HushVotingLicencePlanId.FromExternal("HUSHVOTING.DIRECT.FREE");

        lower.IsKnown.Should().BeTrue();
        upper.IsKnown.Should().BeFalse();
        (lower == upper).Should().BeFalse();
    }

    [Fact]
    public void PlanId_TryGetKnown_ReturnsNullForOversizedOrUnknown()
    {
        HushVotingLicencePlanId.TryGetKnown(new string('x', HushVotingLicencePlanId.MaxUtf8Bytes + 1))
            .Should().BeNull();
        HushVotingLicencePlanId.TryGetKnown("not-a-plan").Should().BeNull();
        HushVotingLicencePlanId.TryGetKnown(HushVotingLicencePlanId.Veritas2000Value)
            .Should().Be(HushVotingLicencePlanId.Veritas2000);
    }
}

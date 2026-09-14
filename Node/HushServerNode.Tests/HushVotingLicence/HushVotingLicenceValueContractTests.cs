using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

public sealed class HushVotingGovernanceOptionIdTests
{
    [Theory]
    [InlineData(HushVotingGovernanceOptionId.NoCustomerTrusteesValue)]
    [InlineData(HushVotingGovernanceOptionId.Trustees3Of5Value)]
    [InlineData(HushVotingGovernanceOptionId.Trustees7Of10Value)]
    [InlineData(HushVotingGovernanceOptionId.Trustees8Of13Value)]
    public void KnownOptionIds_ParseFromExternal(string value)
    {
        var id = HushVotingGovernanceOptionId.FromExternal(value);

        id.IsKnown.Should().BeTrue();
        id.Value.Should().Be(value);
    }

    [Fact]
    public void KnownOptionIds_ContainsExactlyTheFourClosedOptions()
    {
        HushVotingGovernanceOptionId.Known.Select(static x => x.Value).Should().Equal(
            HushVotingGovernanceOptionId.NoCustomerTrusteesValue,
            HushVotingGovernanceOptionId.Trustees3Of5Value,
            HushVotingGovernanceOptionId.Trustees7Of10Value,
            HushVotingGovernanceOptionId.Trustees8Of13Value);
    }

    [Theory]
    [InlineData("trustees-2of3")]
    [InlineData("trustees-1of1")]
    [InlineData("trustees-enterprise-3of5")]
    [InlineData("")]
    public void UnknownOptionId_IsPreservedAsUnsupported_NeverCoerced(string value)
    {
        var id = HushVotingGovernanceOptionId.FromExternal(value);

        id.IsKnown.Should().BeFalse();
        HushVotingGovernanceOptionId.Known.Should().NotContain(id);
    }

    [Fact]
    public void OptionId_ComparisonIsOrdinal()
    {
        var a = HushVotingGovernanceOptionId.FromExternal(HushVotingGovernanceOptionId.Trustees3Of5Value);
        var b = HushVotingGovernanceOptionId.FromExternal(HushVotingGovernanceOptionId.Trustees3Of5Value);

        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}

public sealed class HushVotingLicenceTermTests
{
    [Fact]
    public void Perpetual_HasSafeDescription()
    {
        HushVotingLicenceTerm.Perpetual.IsPerpetual.Should().BeTrue();
        HushVotingLicenceTerm.Perpetual.SafeDescription.Should().Be("Perpetual");
    }

    [Fact]
    public void CalendarYearTerm_IsOneCalendarYear_Not365Days()
    {
        var term = HushVotingLicenceTerm.OneCalendarYear;

        term.IsOneCalendarYear.Should().BeTrue();
        term.Kind.Should().Be(HushVotingLicenceTermKind.CalendarYears);
        term.Years.Should().Be(1);
        term.SafeDescription.Should().Be("One calendar year");
        term.SafeDescription.Should().NotContain("365");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalendarYears_RejectsNonPositive(int years)
    {
        var act = () => HushVotingLicenceTerm.CalendarYears(years);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalendarYears_RejectsFixedDayCountSemantics()
    {
        var act = () => HushVotingLicenceTerm.CalendarYears(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*at least 1 year*");
    }
}

public sealed class HushVotingLicenceEnumNameTests
{
    [Theory]
    [InlineData("Direct", HushVotingLicenceFamily.Direct)]
    [InlineData("Veritas", HushVotingLicenceFamily.Veritas)]
    [InlineData("Enterprise", HushVotingLicenceFamily.Enterprise)]
    public void Family_WireRoundTrip(string wire, HushVotingLicenceFamily family)
    {
        HushVotingLicenceEnumNames.FamilyToWire(family).Should().Be(wire);
        HushVotingLicenceEnumNames.TryParseFamily(wire).Should().Be(family);
    }

    [Theory]
    [InlineData("Default", HushVotingLicenceAvailability.Default)]
    [InlineData("AutomaticUpgrade", HushVotingLicenceAvailability.AutomaticUpgrade)]
    [InlineData("Unavailable", HushVotingLicenceAvailability.Unavailable)]
    public void Availability_WireRoundTrip(string wire, HushVotingLicenceAvailability value)
    {
        HushVotingLicenceEnumNames.AvailabilityToWire(value).Should().Be(wire);
        HushVotingLicenceEnumNames.TryParseAvailability(wire).Should().Be(value);
    }

    [Theory]
    [InlineData("NonBinding", HushVotingBindingStatus.NonBinding)]
    [InlineData("Binding", HushVotingBindingStatus.Binding)]
    public void BindingStatus_WireRoundTrip(string wire, HushVotingBindingStatus value)
    {
        HushVotingLicenceEnumNames.BindingStatusToWire(value).Should().Be(wire);
        HushVotingLicenceEnumNames.TryParseBindingStatus(wire).Should().Be(value);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("unknown")]
    [InlineData("")]
    public void UnknownWireValues_AreUnsupported(string wire)
    {
        HushVotingLicenceEnumNames.TryParseFamily(wire).Should().BeNull();
        HushVotingLicenceEnumNames.TryParseAvailability(wire).Should().BeNull();
    }
}

public sealed class HushVotingLicenceCatalogueVersionTests
{
    [Fact]
    public void V1Version_HasExactValue()
    {
        HushVotingLicenceCatalogueVersion.V1.IsKnown.Should().BeTrue();
        HushVotingLicenceCatalogueVersion.V1.Value.Should()
            .Be("hushvoting-licence-catalogue/v1.0.0");
        HushVotingLicenceCatalogueVersion.V1SchemaId.Should()
            .Be("hushvoting-licence-catalogue/v1");
    }

    [Fact]
    public void UnknownVersion_IsPreservedAsUnsupported_NeverCoerced()
    {
        var unknown = HushVotingLicenceCatalogueVersion.FromExternal("hushvoting-licence-catalogue/v2.0.0");

        unknown.IsKnown.Should().BeFalse();
        (unknown == HushVotingLicenceCatalogueVersion.V1).Should().BeFalse();
    }

    [Fact]
    public void Version_ComparisonIsOrdinal()
    {
        var a = HushVotingLicenceCatalogueVersion.FromExternal(HushVotingLicenceCatalogueVersion.V1Value);

        (a == HushVotingLicenceCatalogueVersion.V1).Should().BeTrue();
        a.GetHashCode().Should().Be(HushVotingLicenceCatalogueVersion.V1.GetHashCode());
    }
}

public sealed class HushVotingLicenceValidationResultTests
{
    [Fact]
    public void ValidResult_HasNoFailures()
    {
        HushVotingLicenceCatalogueValidationResult.Valid.IsValid.Should().BeTrue();
        HushVotingLicenceCatalogueValidationResult.Valid.Failures.Should().BeEmpty();
    }

    [Fact]
    public void FromFailures_OrdersDeterministicallyByCodeThenPath()
    {
        var result = HushVotingLicenceCatalogueValidationResult.FromFailures(
        [
            new HushVotingLicenceValidationFailure("LIC_CAT_RANK_INVALID", "/plans/2/rank", "bad rank"),
            new HushVotingLicenceValidationFailure("LIC_CAT_COPY_UNSAFE", "/plans/0/description", "unsafe"),
            new HushVotingLicenceValidationFailure("LIC_CAT_COPY_UNSAFE", "/plans/1/description", "unsafe"),
        ]);

        result.IsValid.Should().BeFalse();
        result.Failures.Select(static f => f.Code).Should().Equal(
            "LIC_CAT_COPY_UNSAFE",
            "LIC_CAT_COPY_UNSAFE",
            "LIC_CAT_RANK_INVALID");
        result.Failures[0].FieldPath.Should().Be("/plans/0/description");
        result.Failures[1].FieldPath.Should().Be("/plans/1/description");
    }

    [Fact]
    public void FromFailures_ReturnsValidSingleton_WhenEmpty()
    {
        HushVotingLicenceCatalogueValidationResult.FromFailures(Array.Empty<HushVotingLicenceValidationFailure>())
            .Should().BeSameAs(HushVotingLicenceCatalogueValidationResult.Valid);
    }

    [Fact]
    public void StableCodes_AreExactlyTheRequiredV1Set()
    {
        var codes = new[]
        {
            HushVotingLicenceValidationCodes.LicCatFileMissing,
            HushVotingLicenceValidationCodes.LicCatSchemaInvalid,
            HushVotingLicenceValidationCodes.LicCatVersionMismatch,
            HushVotingLicenceValidationCodes.LicCatDigestMismatch,
            HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
            HushVotingLicenceValidationCodes.LicCatDefaultInvalid,
            HushVotingLicenceValidationCodes.LicCatRankInvalid,
            HushVotingLicenceValidationCodes.LicCatTermInvalid,
            HushVotingLicenceValidationCodes.LicCatLimitInvalid,
            HushVotingLicenceValidationCodes.LicCatGovernanceInvalid,
            HushVotingLicenceValidationCodes.LicCatProfileMissing,
            HushVotingLicenceValidationCodes.LicCatProfileMismatch,
            HushVotingLicenceValidationCodes.LicCatCopyUnsafe,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().Contain("LIC_CAT_FILE_MISSING");
        codes.Should().Contain("LIC_CAT_SCHEMA_INVALID");
        codes.Should().Contain("LIC_CAT_VERSION_MISMATCH");
        codes.Should().Contain("LIC_CAT_DIGEST_MISMATCH");
        codes.Should().Contain("LIC_CAT_PLAN_SET_INVALID");
        codes.Should().Contain("LIC_CAT_DEFAULT_INVALID");
        codes.Should().Contain("LIC_CAT_RANK_INVALID");
        codes.Should().Contain("LIC_CAT_TERM_INVALID");
        codes.Should().Contain("LIC_CAT_LIMIT_INVALID");
        codes.Should().Contain("LIC_CAT_GOVERNANCE_INVALID");
        codes.Should().Contain("LIC_CAT_PROFILE_MISSING");
        codes.Should().Contain("LIC_CAT_PROFILE_MISMATCH");
        codes.Should().Contain("LIC_CAT_COPY_UNSAFE");
    }
}

using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

/// <summary>FEAT-018 T018-3-01 / G05. Real canonical signature authentication without present-day rights.</summary>
public sealed class LicenceHistoricalAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_authentication_checks_signed_bytes_without_current_licence_queries(bool tamperSignature)
    {
        var validator = new HushVotingLicenceTransactionValidator(new HushVotingLicenceCanonicalSerializer(),
            new HushVotingLicenceSignatureVerifier(), new HistoricalIdentityOnly());
        var signed = HushVotingLicenceTestData.BuildSigned(
            signatureOverride: tamperSignature ? "invalid" : null);

        var result = await validator.AuthenticateAsync(signed, CancellationToken.None);

        result.IsValid.Should().Be(!tamperSignature);
        if (!tamperSignature) result.ValidatedContent.Should().BeOfType<HushVotingLicenceSignatoryContext>();
        else result.ValidatedContent.Should().BeNull();
    }

    [Fact]
    public void Retained_release_resolution_never_falls_back_to_another_version_or_digest()
    {
        var release = LicenceServiceConfiguration.CreateDefault();
        var archive = new LicenceCatalogueArchive([release]);

        archive.Find(release.CatalogueVersion, release.ReleaseDigestSha256).Should().BeSameAs(release);
        archive.Find("missing-release").Should().BeNull();
        archive.Find(release.CatalogueVersion, new string('B', 64)).Should().BeNull();
        Action replaceSameVersion = () => new LicenceCatalogueArchive([release, LicenceServiceConfiguration.CreateDefault(new string('B', 64))]);
        replaceSameVersion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Higher_target_is_compared_to_the_assigned_rank()
    {
        var catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var state = new HushVotingLicenceCurrentState.Active(HushVotingLicencePlanId.DirectFree,
            HushVotingLicenceTestData.BaselineTransactionId, catalogue.Version.Value,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null,
            AssignedUpgradeRank: catalogue.FindPlan(HushVotingLicencePlanId.Veritas2000)!.UpgradeRank + 1);

        var decision = HushVotingLicenceTransitionDecisionCore.Decide(catalogue, HushVotingLicenceTestData.UpgradePayload(), state);

        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushVotingLicenceValidationCodes.TransitionNotHigher);
    }

    private sealed class HistoricalIdentityOnly : IHushVotingLicenceValidationContextSource
    {
        public Task<HushVotingLicenceSignatoryContext?> ResolveIdentityAsync(string address, CancellationToken cancellationToken) =>
            Task.FromResult<HushVotingLicenceSignatoryContext?>(new(address, 1));
        public Task<HushVotingLicenceCatalogue> GetCurrentCatalogueAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Historical authentication must not query the current release.");
        public Task<HushVotingLicenceCurrentState> ResolveCurrentStateAsync(HushVotingLicenceSignatoryContext identity, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Historical authentication must not query today's rights.");
    }
}

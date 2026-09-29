using FluentAssertions;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushServerNode.HushVotingLicensingIntegration;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>FEAT-018 T018-3-01 / G03: failed authority is not verified absence.</summary>
public sealed class LicenceUnavailableAuthorityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_index_cannot_authorize_a_baseline(bool invalidSubject)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILicenceIndexedProjectionReader>(new UnavailableReader());
        using var provider = services.BuildServiceProvider();
        var source = new HostLicenceValidationContextSource(provider);
        var state = await source.ResolveCurrentStateAsync(
            new HushVotingLicenceSignatoryContext(invalidSubject ? " " : "abcdef", 1),
            CancellationToken.None);
        var decision = HushVotingLicenceTransitionDecisionCore.Decide(
            HushVotingLicenceCatalogueV1.CreateCatalogue(),
            new HushVotingLicenceAssignmentPayload(HushVotingLicenceTransitionIntent.BaselineFree,
                HushVotingLicencePlanId.DirectFree.Value, HushVotingLicenceCatalogueVersion.V1Value), state);

        state.Should().NotBeOfType<HushVotingLicenceCurrentState.NoActive>();
        decision.IsValid.Should().BeFalse();
        decision.ValidationCode.Should().Be(HushNode.HushVoting.Licence.Transactions.HushVotingLicenceValidationCodes.IndexAuthorityUnavailable);
        decision.OperativeFacts.Should().BeNull();
    }

    private sealed class UnavailableReader : ILicenceIndexedProjectionReader
    {
        public Task<IndexedEntitlementReadResult> ResolveEffectiveAsync(
            AuthenticatedIdentitySubject subject, DateTime evaluationUtc, CancellationToken cancellationToken) =>
            Task.FromResult(IndexedEntitlementReadResult.Unavailable("licence_index_unavailable", "Unavailable"));
    }
}

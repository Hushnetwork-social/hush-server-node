using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.Indexing.Interfaces;
using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo.KeyDerivation;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-ENTITLEMENT-ENFORCEMENT-TWIN")]
internal sealed class EntitlementEnforcementTwinSteps(HushVotingScenario scenario)
{
    private DerivedKeys _owner = null!;
    private ElectionId _id;
    private ElectionDraftSpecification _draft = null!;
    private ElectionCommandResult[] _results = [];
    private int _expectedCount;
    private ElectionRosterLinkBoundary? _firstLink;
    private IElectionLifecycleService Service => scenario.Node.Services.GetRequiredService<IElectionLifecycleService>();

    // A trusted service-boundary fixture, not a forged client-supplied timestamp. Signed
    // dispatcher coverage is separately owned by the Open/replay scenarios.
    private static async Task<ElectionCommandResult> Execute(Func<Guid, Task<ElectionCommandResult>> action)
    {
        var tx = Guid.NewGuid();
        using var frame = BlockTransactionExecutionScope.Enter(new(tx, new(100, DateTime.UtcNow, Guid.NewGuid(), 0)));
        return await action(tx);
    }

    private async Task<T> Read<T>(Func<ElectionsDbContext, Task<T>> work)
    {
        using var scope = scenario.Node.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ElectionsDbContext>());
    }

    private Task<ElectionRosterEntryRecord[]> Roster() => Read(db => db.ElectionRosterEntries.AsNoTracking()
        .Where(e => e.ElectionId == _id).OrderBy(e => e.OrganizationVoterId).ToArrayAsync());

    private Task<ElectionCommandResult> Import(int count, string prefix = "v", ElectionRosterImportMode mode = ElectionRosterImportMode.Add,
        string? actor = null) => Execute(tx => Service.ImportRosterAsync(new(_id, actor ?? _owner.SigningPublicKey,
            Enumerable.Range(0, count).Select(i => new ElectionRosterImportItem(prefix + i,
                ElectionRosterContactType.Email, prefix + i + "@example.org")).ToArray(), SourceTransactionId: tx, Mode: mode)));

    private Task<ElectionCommandResult> Link(string voterId) => Execute(tx => Service.ClaimRosterEntryAsync(
        new(_id, _owner.SigningPublicKey, voterId, ElectionEligibilityContracts.TemporaryVerificationCode, SourceTransactionId: tx)));

    private Task<ElectionCommandResult> Update(ElectionDraftSpecification draft) => Execute(tx => Service.UpdateDraftAsync(
        new(_id, _owner.SigningPublicKey, "Owned entitlement Twin", draft, SourceTransactionId: tx)));

    [Given("an owned Draft with the indexed (.*) licence")]
    public async Task Prepare(string plan)
    {
        scenario.Page.Should().BeNull();
        var words = MnemonicGenerator.GenerateMnemonic();
        _owner = HushVotingTestIdentity.DeriveP01(words);
        await HushVotingArtifactClient.RegisterAsync(words, _owner.SigningPrivateKey, _owner.EncryptPrivateKey,
            _owner.SigningPublicKey, _owner.EncryptPublicKey);
        await HushVotingServerIdentity.RegisterAsync(scenario, _owner, "Entitlement owner");
        var baseline = await HushVotingServerLicence.SubmitAsync(scenario, _owner, new(HushVotingLicenceTransitionIntent.BaselineFree,
            HushVotingLicencePlanId.DirectFree.Value, HushVotingLicenceCatalogueVersion.V1Value));
        if (plan != HushVotingLicencePlanId.DirectFree.Value)
            await HushVotingServerLicence.SubmitAsync(scenario, _owner, new(HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
                plan, HushVotingLicenceCatalogueVersion.V1Value, baseline, HushVotingLicencePlanId.DirectFree.Value));
        _draft = new("Entitlement Draft", null, null, ElectionClass.OrganizationalRemoteVoting, ElectionBindingStatus.Binding,
            "admin-prod-1of1", ElectionGovernanceMode.AdminOnly, ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice, VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType.OrganizationImportedRoster, EligibilityMutationPolicy.FrozenAtOpen,
            new OutcomeRuleDefinition(OutcomeRuleKind.SingleWinner, "single_winner", 1, true, true, false, "tie_unresolved", "highest_non_blank_votes"),
            [new ApprovedClientApplicationRecord("hushvoting", "1.0.0")], "omega-v1.0.0",
            ReportingPolicy.DefaultPhaseOnePackage, ReviewWindowPolicy.NoReviewWindow,
            [new ElectionOptionDefinition("one", "One", null, 1, false), new ElectionOptionDefinition("two", "Two", null, 2, false)]);
        var created = await Execute(tx => Service.CreateDraftAsync(new(_owner.SigningPublicKey, _owner.SigningPublicKey,
            "Entitlement Twin", _draft, SourceTransactionId: tx)));
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        _id = created.Election!.ElectionId;
    }

    [When("its canonical roster reaches (.*) voters and one more is requested")]
    public async Task Cap(int count)
    {
        (await Import(count)).IsSuccess.Should().BeTrue();
        _expectedCount = count;
        _results = [await Import(1, "extra")];
    }

    [Then("the cap-sized roster remains unchanged after the typed rejection")]
    public async Task CapPreserved()
    {
        _results[0].EntitlementReason.Should().Be(ElectionEntitlementReason.LimitExceeded);
        var roster = await Roster();
        roster.Should().HaveCount(_expectedCount);
        roster.Any(r => r.OrganizationVoterId.StartsWith("extra", StringComparison.Ordinal)).Should().BeFalse();
    }

    [When("its unlinked roster is replaced and a voter then links")]
    public async Task ReplaceThenLink()
    {
        (await Import(2)).IsSuccess.Should().BeTrue();
        (await Import(1, "replacement", ElectionRosterImportMode.Replace)).IsSuccess.Should().BeTrue();
        var roster = await Roster();
        roster.Should().ContainSingle();
        roster[0].OrganizationVoterId.Should().Be("replacement0");
        (await Link("replacement0")).IsSuccess.Should().BeTrue();
        _firstLink = await Read(db => db.ElectionRosterLinkBoundaries.AsNoTracking().SingleAsync(e => e.ElectionId == _id));
    }

    [Then("replacement after a fresh context preserves that roster and its first-link evidence")]
    public async Task LinkedReplacementBlocked()
    {
        var before = await Roster();
        var result = await Import(1, "other", ElectionRosterImportMode.Replace);
        result.EntitlementReason.Should().Be(ElectionEntitlementReason.RosterReplacementAfterLink);
        (await Roster()).Should().BeEquivalentTo(before);
        (await Read(db => db.ElectionRosterLinkBoundaries.AsNoTracking().SingleAsync(e => e.ElectionId == _id))).Should().Be(_firstLink);
    }

    [When("two additions compete for the final roster slot")]
    public async Task RaceAdditions()
    {
        (await Import(99)).IsSuccess.Should().BeTrue();
        _results = await Task.WhenAll(Import(1, "left"), Import(1, "right"));
    }

    [Then("one addition commits and the other has the typed cap rejection")]
    public async Task RaceCap()
    {
        _results.Count(r => r.IsSuccess).Should().Be(1);
        _results.Single(r => !r.IsSuccess).EntitlementReason.Should().Be(ElectionEntitlementReason.LimitExceeded);
        (await Roster()).Should().HaveCount(100);
    }

    [When("identity linking races with replacement by a different roster")]
    public async Task RaceReplacement()
    {
        (await Import(1)).IsSuccess.Should().BeTrue();
        _results = await Task.WhenAll(Link("v0"), Import(1, "new", ElectionRosterImportMode.Replace));
    }

    [Then("only a consistent linked-old or unlinked-new roster survives")]
    public async Task ConsistentRace()
    {
        _results.Count(r => r.IsSuccess).Should().Be(1);
        var row = (await Roster()).Single();
        if (_results[0].IsSuccess)
        {
            row.OrganizationVoterId.Should().Be("v0"); row.IsLinked.Should().BeTrue();
            _results[1].EntitlementReason.Should().Be(ElectionEntitlementReason.RosterReplacementAfterLink);
            (await Read(db => db.ElectionRosterLinkBoundaries.CountAsync(e => e.ElectionId == _id))).Should().Be(1);
        }
        else
        {
            row.OrganizationVoterId.Should().Be("new0"); row.IsLinked.Should().BeFalse();
            _results[0].ErrorCode.Should().Be(ElectionCommandErrorCode.NotFound);
            (await Read(db => db.ElectionRosterLinkBoundaries.CountAsync(e => e.ElectionId == _id))).Should().Be(0);
        }
    }

    [When("an import duplicates an existing canonical voter identifier")]
    public async Task Duplicate()
    {
        (await Import(1)).IsSuccess.Should().BeTrue();
        _results = [await Execute(tx => Service.ImportRosterAsync(new(_id, _owner.SigningPublicKey,
            [new(" v0 ", ElectionRosterContactType.Email, "new@example.org")], SourceTransactionId: tx)))];
    }

    [Then("duplicate rejection retains the original roster and records rejection evidence")]
    public async Task DuplicatePreserved()
    {
        _results[0].ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        (await Roster()).Should().ContainSingle();
        (await Read(db => db.ElectionRosterImportEvidences.CountAsync(e => e.ElectionId == _id))).Should().Be(2);
    }

    [When("a different actor attempts import and the owner chooses trustee governance")]
    public async Task Unauthorized()
    {
        _results = [await Import(1, actor: "different-actor"), await Update(_draft with
            { SelectedProfileId = "dkg-prod-3of5", GovernanceMode = ElectionGovernanceMode.TrusteeThreshold, RequiredApprovalCount = 3 })];
    }

    [Then("actor and profile rejections preserve the draft")]
    public async Task UnauthorizedPreserved()
    {
        _results[0].ErrorCode.Should().Be(ElectionCommandErrorCode.Forbidden);
        _results[1].EntitlementReason.Should().Be(ElectionEntitlementReason.ProfileNotAllowed);
        (await Roster()).Should().BeEmpty();
        (await Read(db => db.Elections.SingleAsync(e => e.ElectionId == _id))).SelectedProfileId.Should().Be(_draft.SelectedProfileId);
    }

    [When("the owner selects trustee governance and issues its first invitation")]
    public async Task Invite()
    {
        _draft = _draft with { SelectedProfileId = "dkg-prod-3of5", GovernanceMode = ElectionGovernanceMode.TrusteeThreshold, RequiredApprovalCount = 3 };
        (await Update(_draft)).IsSuccess.Should().BeTrue();
        (await Execute(tx => Service.InviteTrusteeAsync(new(_id, _owner.SigningPublicKey,
            "fixture-trustee", "Fixture trustee", SourceTransactionId: tx)))).IsSuccess.Should().BeTrue();
    }

    [Then("switching to another licensed trustee profile is rejected without deleting evidence")]
    public async Task GovernanceLocked()
    {
        var result = await Update(_draft with { SelectedProfileId = "dkg-prod-7of10", RequiredApprovalCount = 7 });
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
        (await Read(db => db.ElectionTrusteeInvitations.CountAsync(e => e.ElectionId == _id))).Should().Be(1);
        (await Read(db => db.Elections.SingleAsync(e => e.ElectionId == _id))).SelectedProfileId.Should().Be("dkg-prod-3of5");
    }
}

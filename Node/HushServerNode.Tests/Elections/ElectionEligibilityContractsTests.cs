using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionEligibilityContractsTests
{
    [Fact]
    public void AnalyzeRosterImportEntries_WithEquivalentHeaderIndependentItems_ProducesDeterministicCanonicalHash()
    {
        var electionId = ElectionId.NewElectionId;
        var importedAt = DateTime.UtcNow;
        var first = ElectionEligibilityContracts.AnalyzeRosterImportEntries(
            electionId,
            [
                new ElectionRosterImportItem("member-b", ElectionRosterContactType.Email, "Bob@Example.COM"),
                new ElectionRosterImportItem("member-a", ElectionRosterContactType.Phone, "+15550001001", IsInitiallyActive: false),
            ],
            existingRosterEntries: Array.Empty<ElectionRosterEntryRecord>(),
            rosterImportVersion: 1,
            importedByActor: "owner-address",
            importedAt: importedAt);
        var second = ElectionEligibilityContracts.AnalyzeRosterImportEntries(
            electionId,
            [
                new ElectionRosterImportItem("member-a", ElectionRosterContactType.Phone, "+15550001001", IsInitiallyActive: false),
                new ElectionRosterImportItem("member-b", ElectionRosterContactType.Email, "Bob@example.com"),
            ],
            existingRosterEntries: Array.Empty<ElectionRosterEntryRecord>(),
            rosterImportVersion: 1,
            importedByActor: "owner-address",
            importedAt: importedAt);

        first.ValidationErrors.Should().BeEmpty();
        second.ValidationErrors.Should().BeEmpty();
        first.Evidence.RosterCanonicalHash.Should().Be(second.Evidence.RosterCanonicalHash);
        first.Evidence.RosterSourceFileHash.Should().NotBe(second.Evidence.RosterSourceFileHash);
    }

    [Fact]
    public void AnalyzeRosterImportEntries_WithDuplicateIds_RejectsAllDuplicateRows()
    {
        var analysis = ElectionEligibilityContracts.AnalyzeRosterImportEntries(
            ElectionId.NewElectionId,
            [
                new ElectionRosterImportItem("member-1", ElectionRosterContactType.Email, "a@example.test"),
                new ElectionRosterImportItem("member-1", ElectionRosterContactType.Email, "b@example.test"),
                new ElectionRosterImportItem("member-2", ElectionRosterContactType.Email, "c@example.test"),
            ],
            existingRosterEntries: Array.Empty<ElectionRosterEntryRecord>(),
            rosterImportVersion: 1,
            importedByActor: "owner-address",
            importedAt: DateTime.UtcNow);

        analysis.AcceptedRosterEntries.Should().BeEmpty();
        analysis.ValidationErrors.Should().HaveCount(2);
        analysis.Evidence.DuplicateIdRejectionCount.Should().Be(2);
        analysis.Evidence.RejectedRows.Select(x => x.SourceRowNumber).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void AnalyzeRosterImportEntries_WithDuplicateContact_AddsRestrictedWarningWithoutRejectingRows()
    {
        var analysis = ElectionEligibilityContracts.AnalyzeRosterImportEntries(
            ElectionId.NewElectionId,
            [
                new ElectionRosterImportItem("member-1", ElectionRosterContactType.Email, "Shared@Example.test"),
                new ElectionRosterImportItem("member-2", ElectionRosterContactType.Email, "shared@example.test"),
            ],
            existingRosterEntries: Array.Empty<ElectionRosterEntryRecord>(),
            rosterImportVersion: 1,
            importedByActor: "owner-address",
            importedAt: DateTime.UtcNow);

        analysis.ValidationErrors.Should().BeEmpty();
        analysis.AcceptedRosterEntries.Should().HaveCount(2);
        analysis.Evidence.DuplicateContactWarnings.Should().ContainSingle();
        analysis.Evidence.DuplicateContactWarnings[0].OrganizationVoterIds.Should().BeEquivalentTo("member-1", "member-2");
    }

    [Fact]
    public void AnalyzeRosterImportEntries_WithNonE164Phone_RejectsRow()
    {
        var analysis = ElectionEligibilityContracts.AnalyzeRosterImportEntries(
            ElectionId.NewElectionId,
            [
                new ElectionRosterImportItem("member-1", ElectionRosterContactType.Phone, "555-0101"),
            ],
            existingRosterEntries: Array.Empty<ElectionRosterEntryRecord>(),
            rosterImportVersion: 1,
            importedByActor: "owner-address",
            importedAt: DateTime.UtcNow);

        analysis.AcceptedRosterEntries.Should().BeEmpty();
        analysis.Evidence.InvalidRowRejectionCount.Should().Be(1);
        analysis.Evidence.RejectedRows.Should().ContainSingle(x => x.ReasonCode == "invalid_phone");
    }
}

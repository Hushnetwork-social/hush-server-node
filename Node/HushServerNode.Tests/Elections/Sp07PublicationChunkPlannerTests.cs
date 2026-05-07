using FluentAssertions;
using HushShared.Elections.PublicationProof;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class Sp07PublicationChunkPlannerTests
{
    [Fact]
    public void CreatePlan_ForSmallElection_ShouldUseOneChunk()
    {
        var planner = new Sp07PublicationChunkPlanner(new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: 100,
            MinBallotsPerChunk: 10,
            MaxChunks: 5,
            MaxEncryptedSlots: 8));

        var plan = planner.CreatePlan(40, 8);

        plan.Chunks.Should().ContainSingle();
        plan.Chunks[0].Offset.Should().Be(0);
        plan.Chunks[0].Count.Should().Be(40);
        plan.PlanHashSha512.Should().HaveLength(128);
        plan.PlanId.Should().StartWith("sp07-plan-");
    }

    [Fact]
    public void CreatePlan_ForLargerElection_ShouldBalanceChunksUnderTheConfiguredMaximum()
    {
        var planner = new Sp07PublicationChunkPlanner(new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: 100,
            MinBallotsPerChunk: 10,
            MaxChunks: 5,
            MaxEncryptedSlots: 8));

        var plan = planner.CreatePlan(250, 8);

        plan.Chunks.Select(chunk => chunk.Count).Should().Equal(84, 83, 83);
        plan.Chunks.Select(chunk => chunk.Offset).Should().Equal(0, 84, 167);
        plan.Chunks.Select(chunk => chunk.ChunkIndex).Should().Equal(0, 1, 2);
        plan.Chunks.Should().OnlyContain(chunk => chunk.Count <= 100);
        plan.Chunks.Should().OnlyContain(chunk => chunk.Count >= 10);
    }

    [Fact]
    public void CreatePlan_WhenChunkCountExceedsConfiguredMaximum_ShouldFailClosed()
    {
        var planner = new Sp07PublicationChunkPlanner(new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: 100,
            MinBallotsPerChunk: 10,
            MaxChunks: 5,
            MaxEncryptedSlots: 8));

        var act = () => planner.CreatePlan(501, 8);

        act.Should().Throw<Sp07PublicationProofException>()
            .WithMessage("*exceeding the configured maximum of 5*");
    }

    [Fact]
    public void CreatePlan_WhenPrivacyFloorConflictsWithPerformanceCeiling_ShouldFailClosed()
    {
        var planner = new Sp07PublicationChunkPlanner(new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: 100,
            MinBallotsPerChunk: 80,
            MaxChunks: 5,
            MaxEncryptedSlots: 8));

        var act = () => planner.CreatePlan(101, 8);

        act.Should().Throw<Sp07PublicationProofException>()
            .WithMessage("*cannot keep chunks below 100 ballots*");
    }

    [Fact]
    public void CreatePlan_WhenEncryptedSlotCountExceedsEnvelope_ShouldFailClosed()
    {
        var planner = new Sp07PublicationChunkPlanner(new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: 100,
            MinBallotsPerChunk: 10,
            MaxChunks: 5,
            MaxEncryptedSlots: 8));

        var act = () => planner.CreatePlan(50, 9);

        act.Should().Throw<Sp07PublicationProofException>()
            .WithMessage("*supports up to 8 encrypted slots*");
    }
}

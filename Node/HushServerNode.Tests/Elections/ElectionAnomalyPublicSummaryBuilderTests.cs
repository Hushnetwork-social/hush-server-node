using System.Text.Json;
using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyPublicSummaryBuilderTests
{
    private static readonly DateTime GeneratedAt = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_WithNoAnomalies_EmitsExactZeroSummary()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(electionId);

        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            GeneratedAt));

        summary.SchemaId.Should().Be(ElectionAnomalyPublicSummarySchemaIds.Current);
        summary.SuppressionPolicyId.Should().Be(ElectionAnomalyPublicSummarySuppressionPolicyIds.Current);
        summary.ElectionId.Should().Be(electionId.ToString());
        summary.SourceManifestHash.Should().StartWith("sha256:");
        summary.RestrictedManifestHash.Should().Be(summary.SourceManifestHash);
        summary.TotalThreadCount.Should().Be(0);
        summary.TotalThreadCountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Exact);
        summary.VisibleBuckets.Should().BeEmpty();
        summary.SuppressionReasonIds.Should().BeEmpty();
        summary.GeneratedAt.Should().Be(GeneratedAt);
    }

    [Fact]
    public void Build_WithSafeCategoryCount_PublishesExactBucket()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            (Guid.Parse("00000000-0000-0000-0000-000000000001"), ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern),
            (Guid.Parse("00000000-0000-0000-0000-000000000002"), ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern),
            (Guid.Parse("00000000-0000-0000-0000-000000000003"), ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern));

        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: null,
            GeneratedAt));

        summary.TotalThreadCount.Should().Be(3);
        summary.TotalThreadCountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Exact);
        summary.SuppressedThreadCount.Should().Be(0);
        summary.AggregatedBucketCount.Should().Be(0);
        summary.SuppressionReasonIds.Should().BeEmpty();
        summary.VisibleBuckets.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new PublicAnomalySummaryBucket(
                ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
                ElectionAnomalyPublicSummaryCountModeIds.Exact,
                3,
                Array.Empty<string>(),
                [ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern]));
    }

    [Fact]
    public void Build_WithLowCountCategories_AggregatesWhenAggregateIsSafe()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            (Guid.Parse("00000000-0000-0000-0000-000000000001"), ElectionAnomalyCategoryIds.AccessOrAuthenticationAnomaly),
            (Guid.Parse("00000000-0000-0000-0000-000000000002"), ElectionAnomalyCategoryIds.BallotCastingOrReceiptAnomaly),
            (Guid.Parse("00000000-0000-0000-0000-000000000003"), ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern));

        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: null,
            GeneratedAt));

        summary.TotalThreadCount.Should().Be(3);
        summary.AggregatedBucketCount.Should().Be(3);
        summary.SuppressedThreadCount.Should().Be(0);
        summary.SuppressionReasonIds.Should().Contain(ElectionAnomalyPublicSummarySuppressionReasonIds.LowCountCategory);
        var bucket = summary.VisibleBuckets.Should().ContainSingle().Which;
        bucket.CategoryId.Should().Be(ElectionAnomalyCategoryIds.OtherProcessAnomaly);
        bucket.CountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Aggregated);
        bucket.PublicCount.Should().Be(3);
        bucket.SuppressionReasonIds.Should().ContainSingle()
            .Which.Should().Be(ElectionAnomalyPublicSummarySuppressionReasonIds.LowCountCategory);
        bucket.SourceCategoryIds.Should().BeEquivalentTo(
            [
                ElectionAnomalyCategoryIds.AccessOrAuthenticationAnomaly,
                ElectionAnomalyCategoryIds.BallotCastingOrReceiptAnomaly,
                ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Build_WithIdentifyingRoleContext_SuppressesExactCategoryAndDoesNotPublishPrivateContext()
    {
        var electionId = ElectionId.NewElectionId;
        var trusteeThreadId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var voterThreadId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var ownerThreadId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var manifest = CreateManifest(
            electionId,
            (trusteeThreadId, ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern),
            (voterThreadId, ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern),
            (ownerThreadId, ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern));

        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: null,
            GeneratedAt,
            [
                new ElectionAnomalyPublicSummaryPrivateThreadContext(
                    trusteeThreadId,
                    ElectionAnomalyActorRoleContextIds.Trustee),
                new ElectionAnomalyPublicSummaryPrivateThreadContext(
                    voterThreadId,
                    ElectionAnomalyActorRoleContextIds.Voter),
                new ElectionAnomalyPublicSummaryPrivateThreadContext(
                    ownerThreadId,
                    ElectionAnomalyActorRoleContextIds.ElectionOwner),
            ]));

        summary.TotalThreadCount.Should().Be(3);
        summary.SuppressedThreadCount.Should().Be(3);
        summary.SuppressionReasonIds.Should().Contain(
            ElectionAnomalyPublicSummarySuppressionReasonIds.IdentifyingRoleCategory);
        summary.SuppressionReasonIds.Should().Contain(
            ElectionAnomalyPublicSummarySuppressionReasonIds.AggregationNotSafe);
        var bucket = summary.VisibleBuckets.Should().ContainSingle().Which;
        bucket.CategoryId.Should().Be(ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern);
        bucket.CountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Suppressed);
        bucket.PublicCount.Should().BeNull();

        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain(ElectionAnomalyActorRoleContextIds.Trustee);
        json.Should().NotContain(ElectionAnomalyActorRoleContextIds.Voter);
        json.Should().NotContain(ElectionAnomalyActorRoleContextIds.ElectionOwner);
    }

    [Fact]
    public void Build_WithSmallElectionTotal_SuppressesTotalThreadCount()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            (Guid.Parse("00000000-0000-0000-0000-000000000001"), ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly));

        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: null,
            GeneratedAt));

        summary.TotalThreadCount.Should().BeNull();
        summary.TotalThreadCountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Suppressed);
        summary.SuppressedThreadCount.Should().Be(1);
        summary.SuppressionReasonIds.Should().Contain(
            ElectionAnomalyPublicSummarySuppressionReasonIds.SmallElectionIdentifiability);
        summary.VisibleBuckets.Should().ContainSingle().Which.PublicCount.Should().BeNull();
    }

    [Fact]
    public void Build_WithRestrictedEvidenceOnlyContext_SuppressesBucketEvenWhenCountIsOtherwiseSafe()
    {
        var electionId = ElectionId.NewElectionId;
        var threadIds = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
        };
        var manifest = CreateManifest(
            electionId,
            (threadIds[0], ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern),
            (threadIds[1], ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern),
            (threadIds[2], ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern));

        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: null,
            GeneratedAt,
            threadIds.Select(x => new ElectionAnomalyPublicSummaryPrivateThreadContext(
                    x,
                    ForceRestrictedEvidenceOnly: true))
                .ToArray()));

        summary.SuppressedThreadCount.Should().Be(3);
        summary.SuppressionReasonIds.Should().Contain(
            ElectionAnomalyPublicSummarySuppressionReasonIds.RestrictedEvidenceOnly);
        summary.SuppressionReasonIds.Should().Contain(
            ElectionAnomalyPublicSummarySuppressionReasonIds.AggregationNotSafe);
        var bucket = summary.VisibleBuckets.Should().ContainSingle().Which;
        bucket.CountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Suppressed);
        bucket.PublicCount.Should().BeNull();
    }

    [Fact]
    public void Scan_WithForbiddenFieldsAndRepresentativeValues_FailsWithMatches()
    {
        var content = """
            {
              "submitterPersonScopeId": "private-scope",
              "publicSummary": "visible",
              "example": "HUSH-PRIVATE-ADDRESS"
            }
            """;

        var result = ElectionAnomalyPublicArtifactPrivacyScanner.Scan(
            content,
            ["HUSH-PRIVATE-ADDRESS"]);

        result.Passed.Should().BeFalse();
        result.StatusId.Should().Be(ElectionAnomalyPublicArtifactScanStatusIds.Failed);
        result.MatchedFieldNames.Should().Contain("submitterPersonScopeId");
        result.MatchedValues.Should().Contain("HUSH-PRIVATE-ADDRESS");
    }

    [Fact]
    public void Scan_WithPublicSummaryOnly_Passes()
    {
        var content = """
            {
              "schemaId": "public-anomaly-summary-v1",
              "suppressionPolicyId": "anomaly-public-summary-v1",
              "visibleBuckets": []
            }
            """;

        var result = ElectionAnomalyPublicArtifactPrivacyScanner.Scan(content);

        result.Passed.Should().BeTrue();
        result.StatusId.Should().Be(ElectionAnomalyPublicArtifactScanStatusIds.Passed);
        result.MatchedFieldNames.Should().BeEmpty();
        result.MatchedValues.Should().BeEmpty();
    }

    private static AnomalyIntakeManifest CreateManifest(
        ElectionId electionId,
        params (Guid ThreadId, string CategoryId)[] threads) =>
        new(
            ElectionAnomalyManifestCanonicalizationIds.Current,
            electionId.ToString(),
            ElectionAnomalyEvidenceManifestScopeIds.Package,
            ElectionAnomalyPackageReadinessStatusIds.Ready,
            Array.Empty<string>(),
            threads
                .Select((thread, index) => CreateManifestThread(thread.ThreadId, thread.CategoryId, index))
                .ToArray());

    private static AnomalyIntakeManifestThread CreateManifestThread(
        Guid threadId,
        string categoryId,
        int index)
    {
        var recordedAt = GeneratedAt.AddMinutes(index);
        return new AnomalyIntakeManifestThread(
            threadId,
            categoryId,
            ElectionAnomalyCaseStateIds.UnderReview,
            $"sha256:thread-{index}",
            GovernedDecisionRef: null,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            recordedAt,
            recordedAt,
            Attachments: Array.Empty<AnomalyIntakeManifestAttachment>(),
            Redactions: Array.Empty<AnomalyIntakeManifestRedaction>(),
            RecipientStatuses:
            [
                new AnomalyIntakeManifestRecipientStatus(
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    ElectionAnomalyRecipientWrapStatusIds.Available),
            ]);
    }
}

using System.Collections.Immutable;
using FluentAssertions;
using HushServerNode.Testing.Elections;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ControlledElectionArtifactSearchTests
{
    [Fact]
    public void InspectWorkspaceForForbiddenArtifacts_WhenOnlyPublicKeyAndSharesExist_ShouldReportClean()
    {
        // Arrange
        using var temp = new TemporaryWorkspaceRoot();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            CreateThresholdDefinition(),
            sessionId: "session-artifacts",
            targetTallyId: "tally-artifacts",
            seed: 2001);
        temp.CreateSafeExport(thresholdSetup);
        var forbiddenKeyMaterial = ControlledElectionHarness.ReconstructSecretScalarFromShares(
                ImmutableArray.Create(thresholdSetup.Shares[0], thresholdSetup.Shares[2], thresholdSetup.Shares[4]))
            .ToString();

        // Act
        var result = ControlledElectionArtifactInspector.InspectWorkspaceForForbiddenArtifacts(
            temp.RootPath,
            ImmutableArray.Create(forbiddenKeyMaterial));

        // Assert
        result.IsClean.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void InspectWorkspaceForForbiddenArtifacts_WhenFullKeyArtifactExists_ShouldReportDirty()
    {
        // Arrange
        using var temp = new TemporaryWorkspaceRoot();
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            CreateThresholdDefinition(),
            sessionId: "session-artifacts-dirty",
            targetTallyId: "tally-artifacts-dirty",
            seed: 2002);
        temp.CreateSafeExport(thresholdSetup);
        var forbiddenKeyMaterial = ControlledElectionHarness.ReconstructSecretScalarFromShares(
                ImmutableArray.Create(thresholdSetup.Shares[0], thresholdSetup.Shares[1], thresholdSetup.Shares[3]))
            .ToString();
        temp.CreateLeakedFullKeyArtifact(forbiddenKeyMaterial);

        // Act
        var result = ControlledElectionArtifactInspector.InspectWorkspaceForForbiddenArtifacts(
            temp.RootPath,
            ImmutableArray.Create(forbiddenKeyMaterial));

        // Assert
        result.IsClean.Should().BeFalse();
        result.Notes.Should().Contain("suspicious full-key material");
        result.Findings.Should().Contain(finding => finding.Contains("full-private-key.txt"));
        result.Findings.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    private static ControlledElectionThresholdDefinition CreateThresholdDefinition() =>
        new(
            ElectionId: "election-artifacts",
            TrusteeIds: ImmutableArray.Create("trustee-a", "trustee-b", "trustee-c", "trustee-d", "trustee-e"),
            Threshold: 3);

    private sealed class TemporaryWorkspaceRoot : IDisposable
    {
        public TemporaryWorkspaceRoot()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"feat107-elections-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void CreateSafeExport(ControlledElectionThresholdSetup thresholdSetup)
        {
            var exportDirectory = Path.Combine(RootPath, "exports", thresholdSetup.ThresholdDefinition.ElectionId);
            Directory.CreateDirectory(exportDirectory);

            File.WriteAllText(Path.Combine(exportDirectory, "public-key.json"), thresholdSetup.PublicKey.ToString() ?? string.Empty);

            foreach (var share in thresholdSetup.Shares)
            {
                File.WriteAllText(
                    Path.Combine(exportDirectory, $"share-{share.TrusteeId}.json"),
                    $$"""
                    {
                      "electionId": "{{share.ElectionId}}",
                      "sessionId": "{{share.SessionId}}",
                      "targetTallyId": "{{share.TargetTallyId}}",
                      "trusteeId": "{{share.TrusteeId}}",
                      "shareIndex": {{share.ShareIndex}},
                      "shareMaterial": "{{share.ShareMaterial}}"
                    }
                    """);
            }
        }

        public void CreateLeakedFullKeyArtifact(string fullKeyMaterial)
        {
            var leakDirectory = Path.Combine(RootPath, "leaks");
            Directory.CreateDirectory(leakDirectory);
            File.WriteAllText(
                Path.Combine(leakDirectory, "full-private-key.txt"),
                $"FULL ELECTION PRIVATE KEY\n{fullKeyMaterial}");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

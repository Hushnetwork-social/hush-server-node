using System.Text.Json;
using FluentAssertions;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp08ContractTests
{
    [Fact]
    public void ReleaseIntegrityProfileIds_ShouldExposeCanonicalV1Selection()
    {
        ElectionSp08ProfileIds.ReleaseManifestSchema.Should().Be("HushVotingReleaseManifest-v1");
        ElectionSp08ProfileIds.ReleaseManifestFileName.Should().Be("HushVotingReleaseManifest-v1.json");
        ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder.Should().Be("development_placeholder");
        ElectionSp08ProfileIds.EvidenceModeOfficial.Should().Be("official_sp08");
        ElectionSp08ProfileIds.ReleaseIntegrityCheckCodes.Should().Equal(
            "REL-000",
            "REL-001",
            "REL-002",
            "REL-003",
            "REL-004",
            "REL-005",
            "REL-006",
            "REL-007",
            "REL-008");
    }

    [Fact]
    public void Sp08PackageFileNames_ShouldExposePublicReleaseArtifacts()
    {
        VerificationPackageFileNames.Sp08ReleaseIntegrity.Should()
            .Be("artifacts/election-record/release-integrity.json");
        VerificationPackageFileNames.Sp08ReleaseManifest.Should()
            .Be("artifacts/election-record/release-manifest.json");
        VerificationPackageFileNames.Sp08ReleaseIntegrityVerifierOutput.Should()
            .Be("artifacts/election-record/release-integrity-verifier-output.json");
    }

    [Fact]
    public void ReleaseManifestHash_ShouldBeStableForSameContentRegardlessOfComponentOrder()
    {
        var first = CreateOfficialManifest([
            CreateOfficialComponent(ElectionSp08ProfileIds.WebClientComponent, "sha256:web"),
            CreateOfficialComponent(ElectionSp08ProfileIds.ServerComponent, "sha256:server"),
        ]);
        var second = CreateOfficialManifest([
            CreateOfficialComponent(ElectionSp08ProfileIds.ServerComponent, "sha256:server"),
            CreateOfficialComponent(ElectionSp08ProfileIds.WebClientComponent, "sha256:web"),
        ]);

        var firstHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(first);
        var secondHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(second);

        firstHash.Should().Be(secondHash);
        firstHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ReleaseManifestGenerator_ShouldCanonicalizeOfficialManifestAndExposeStableHash()
    {
        var input = CreateOfficialGeneratorManifest();

        var generated = ElectionSp08ReleaseManifestGenerator.Generate(input);
        var serialized = ElectionSp08ReleaseManifestGenerator.SerializeCanonical(input);
        var generatedAgain = JsonSerializer.Deserialize<ElectionSp08ReleaseManifestArtifactRecord>(
            serialized,
            VerificationJson.Options)!;

        generated.Components.Select(x => x.ComponentId).Should().Equal(
            ElectionSp08ProfileIds.AuditPackageExporterComponent,
            ElectionSp08ProfileIds.ProtocolPackageComponent,
            ElectionSp08ProfileIds.ServerComponent,
            ElectionSp08ProfileIds.Sp07ProofWorkerComponent,
            ElectionSp08ProfileIds.StandaloneVerifierComponent,
            ElectionSp08ProfileIds.WebClientComponent);
        ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(generated)
            .Should()
            .Be(ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(generatedAgain));
    }

    [Fact]
    public void ReleaseManifestGenerator_ShouldRejectOfficialMutableReferences()
    {
        var manifest = CreateOfficialGeneratorManifest() with
        {
            Components =
            [
                CreateOfficialComponent(ElectionSp08ProfileIds.ServerComponent, "sha256:server") with
                {
                    ImmutableReference = "latest",
                },
                .. CreateOfficialGeneratorManifest().Components.Skip(1),
            ],
        };

        var errors = ElectionSp08ReleaseManifestGenerator.Validate(manifest);

        errors.Should().Contain(x => x.Contains("mutable or local reference", StringComparison.Ordinal));
        var act = () => ElectionSp08ReleaseManifestGenerator.Generate(manifest);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReleaseManifestGenerator_ShouldRejectOfficialComponentWithoutWorkflowRun()
    {
        var manifest = CreateOfficialGeneratorManifest();
        var missingWorkflowRun = manifest with
        {
            Components =
            [
                manifest.Components[0] with { BuildWorkflowRunId = null },
                .. manifest.Components.Skip(1),
            ],
        };

        var errors = ElectionSp08ReleaseManifestGenerator.Validate(missingWorkflowRun);

        errors.Should().Contain(x => x.Contains("build workflow run id", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseManifestGenerator_ShouldRejectOfficialCircuitKeyHashesWithoutSha256Prefix()
    {
        var manifest = CreateOfficialGeneratorManifest();
        var missingHashPrefix = manifest with
        {
            CircuitAndKeys =
            [
                manifest.CircuitAndKeys[0] with
                {
                    CircuitHash = "circuit-hash",
                    ProvingKeyHash = "proving-key-hash",
                    VerifyingKeyHash = "verifying-key-hash",
                },
            ],
        };

        var errors = ElectionSp08ReleaseManifestGenerator.Validate(missingHashPrefix);

        errors.Should().Contain(x => x.Contains("circuit hash must be sha256-prefixed", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("proving-key hash must be sha256-prefixed", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("verifying-key hash must be sha256-prefixed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseManifestGenerator_ShouldRejectPlaceholderWithoutNotForClaims()
    {
        var manifest = CreateOfficialManifest([CreateOfficialComponent(ElectionSp08ProfileIds.ServerComponent, "sha256:server")]) with
        {
            EvidenceMode = ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
            NotForReleaseIntegrityClaims = false,
        };

        ElectionSp08ReleaseManifestGenerator.Validate(manifest).Should()
            .Contain("development_placeholder must set not_for_release_integrity_claims");
    }

    [Fact]
    public void PlaceholderEvidence_ShouldNotSatisfyHighAssurance()
    {
        ElectionSp08ReleaseIntegrityRules.IsEvidenceModeAllowedForProfile(
                VerificationProfileIds.HighAssuranceV1,
                ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
                notForReleaseIntegrityClaims: true)
            .Should()
            .BeFalse();

        ElectionSp08ReleaseIntegrityRules.IsEvidenceModeAllowedForProfile(
                VerificationProfileIds.DevelopmentCurrentV1,
                ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
                notForReleaseIntegrityClaims: true)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("ghcr.io/hush/server:latest")]
    [InlineData("refs/heads/main")]
    [InlineData("file:///tmp/local-build")]
    [InlineData("https://example.test/download/server.tar.gz")]
    public void MutableOrLocalReferences_ShouldBeRejected(string artifactReference)
    {
        ElectionSp08ReleaseIntegrityRules.IsMutableOrLocalReference(artifactReference).Should().BeTrue();
        ElectionSp08ReleaseIntegrityRules.IsImmutableReference(artifactReference).Should().BeFalse();
    }

    [Theory]
    [InlineData("ghcr.io/hush/server@sha256:abc123")]
    [InlineData("sha256:abc123")]
    [InlineData("release-2026.05.11+sha256:abc123")]
    public void ImmutableReferences_ShouldBeAcceptedByContractHelper(string artifactReference)
    {
        ElectionSp08ReleaseIntegrityRules.IsMutableOrLocalReference(artifactReference).Should().BeFalse();
        ElectionSp08ReleaseIntegrityRules.IsImmutableReference(artifactReference).Should().BeTrue();
    }

    [Fact]
    public void PublicSp08Artifacts_ShouldExcludePrivateHostAndDeviceMaterial()
    {
        var manifest = CreateOfficialManifest([
            CreateOfficialComponent(ElectionSp08ProfileIds.ServerComponent, "sha256:server"),
            CreateOfficialComponent(ElectionSp08ProfileIds.MobileAppComponent, "sha256:mobile") with
            {
                DistributionReference = "app-store-build-42",
                SigningFingerprint = "sha256:signing",
            },
        ]);
        var hash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(manifest);
        var integrity = new ElectionSp08ReleaseIntegrityArtifactRecord(
            ElectionId: Guid.NewGuid().ToString("D"),
            ProfileId: VerificationProfileIds.HighAssuranceV1,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            NotForReleaseIntegrityClaims: false,
            BlocksHighAssurance: false,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: hash,
            ProtocolPackageManifestName: "ProtocolOmegaPackageManifest.json",
            ProtocolPackageManifestHash: "sha256:protocol-package",
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityEvidenceValid,
            Components: manifest.Components,
            LifecycleBindings: manifest.LifecycleBindings,
            PublicPrivacyBoundary:
            [
                "no_private_host_state",
                "no_per_voter_device_identifier",
                "no_raw_attestation_token",
                "no_ip_address",
            ]);

        var json = JsonSerializer.Serialize(new { manifest, integrity }, VerificationJson.Options);

        json.Should().Contain("official_sp08");
        json.Should().Contain("releaseManifestHash");
        json.Should().NotContain("hostPrivateState");
        json.Should().NotContain("deviceIdentifier");
        json.Should().NotContain("installationId");
        json.Should().NotContain("rawAttestationToken");
        json.Should().NotContain("ipAddress");
        json.Should().NotContain("voterId");
    }

    [Theory]
    [InlineData("hostPrivateState")]
    [InlineData("deviceIdentifier")]
    [InlineData("installationId")]
    [InlineData("rawAttestationToken")]
    [InlineData("ipAddress")]
    public void PublicPrivacyBoundary_ShouldRejectSp08RestrictedFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage(fieldName).Should().BeTrue();
    }

    [Fact]
    public void VerificationResultCodes_ShouldExposeStableReleaseIntegrityCodes()
    {
        var codes = new[]
        {
            VerificationResultCodes.ReleaseIntegrityEvidenceValid,
            VerificationResultCodes.ReleaseIntegrityManifestMissing,
            VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed,
            VerificationResultCodes.ReleaseIntegrityMutableArtifactReference,
            VerificationResultCodes.ReleaseIntegrityComponentHashMismatch,
            VerificationResultCodes.ReleaseIntegrityCircuitOrPackageHashMismatch,
            VerificationResultCodes.ReleaseIntegrityLifecycleMismatch,
            VerificationResultCodes.ReleaseIntegrityMobileEvidenceIncomplete,
            VerificationResultCodes.ReleaseIntegrityPackageManifestMissingFiles,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(x => x.Should().StartWith("release_integrity_"));
    }

    private static ElectionSp08ReleaseManifestArtifactRecord CreateOfficialManifest(
        IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> components) =>
        new(
            Schema: ElectionSp08ProfileIds.ReleaseManifestSchema,
            ManifestId: "release-manifest-2026-05-11",
            ReleaseId: "release-2026.05.11",
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            NotForReleaseIntegrityClaims: false,
            GeneratedAt: DateTime.UnixEpoch,
            SourceAuthority: "github-actions",
            SourceCommit: "0123456789abcdef0123456789abcdef01234567",
            SourceTag: "hush-voting-2026.05.11",
            components,
            CircuitAndKeys:
            [
                new ElectionSp08CircuitKeyArtifactRecord(
                    CircuitId: "protocol-omega-publication-proof-v1",
                    CircuitHash: "sha256:circuit",
                    ProvingKeyHash: "sha256:proving-key",
                    VerifyingKeyHash: "sha256:verifying-key",
                    ProtocolPackageManifestHash: "sha256:protocol-package"),
            ],
            LifecycleBindings:
            [
                new ElectionSp08LifecycleReleaseBindingRecord(
                    LifecycleStage: ElectionSp08ProfileIds.OpenLifecycleStage,
                    ExpectedReleaseId: "release-2026.05.11",
                    ObservedReleaseId: "release-2026.05.11",
                    ExpectedArtifactDigest: "sha256:server",
                    ObservedArtifactDigest: "sha256:server",
                    MatchesSealedPolicy: true),
            ],
            PublicPrivacyBoundary:
            [
                "no_private_host_state",
                "no_per_voter_device_identifier",
                "no_raw_attestation_token",
                "no_ip_address",
            ]);

    private static ElectionSp08ReleaseManifestArtifactRecord CreateOfficialGeneratorManifest() =>
        CreateOfficialManifest([
            CreateOfficialComponent(ElectionSp08ProfileIds.WebClientComponent, "sha256:web"),
            CreateOfficialComponent(ElectionSp08ProfileIds.ServerComponent, "sha256:server"),
            CreateOfficialComponent(ElectionSp08ProfileIds.StandaloneVerifierComponent, "sha256:verifier"),
            CreateOfficialComponent(ElectionSp08ProfileIds.Sp07ProofWorkerComponent, "sha256:worker"),
            CreateOfficialComponent(ElectionSp08ProfileIds.ProtocolPackageComponent, "sha256:protocol"),
            CreateOfficialComponent(ElectionSp08ProfileIds.AuditPackageExporterComponent, "sha256:exporter"),
        ]) with
        {
            LifecycleBindings =
            [
                CreateLifecycleBinding(ElectionSp08ProfileIds.OpenLifecycleStage, "sha256:server"),
                CreateLifecycleBinding(ElectionSp08ProfileIds.CloseLifecycleStage, "sha256:server"),
                CreateLifecycleBinding(ElectionSp08ProfileIds.ProofWorkerLifecycleStage, "sha256:worker"),
                CreateLifecycleBinding(ElectionSp08ProfileIds.ExporterLifecycleStage, "sha256:exporter"),
                CreateLifecycleBinding(ElectionSp08ProfileIds.ClientReleaseSetLifecycleStage, "sha256:web"),
            ],
        };

    private static ElectionSp08LifecycleReleaseBindingRecord CreateLifecycleBinding(
        string lifecycleStage,
        string digest) =>
        new(
            lifecycleStage,
            ExpectedReleaseId: "release-2026.05.11",
            ObservedReleaseId: "release-2026.05.11",
            ExpectedArtifactDigest: digest,
            ObservedArtifactDigest: digest,
            MatchesSealedPolicy: true);

    private static ElectionSp08ReleaseComponentArtifactRecord CreateOfficialComponent(
        string componentId,
        string digest) =>
        new(
            ComponentId: componentId,
            ComponentType: componentId,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            ArtifactName: $"{componentId}.artifact",
            ArtifactDigest: digest,
            SourceCommit: "0123456789abcdef0123456789abcdef01234567",
            SourceTag: "hush-voting-2026.05.11",
            ImmutableReference: $"{componentId}@{digest}",
            BuildWorkflowRunId: "123456789",
            DistributionReference: null,
            SigningFingerprint: null,
            IsPlaceholder: false);
}

using FluentAssertions;
using HushNode.Elections.HushVotingLicence;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Interfaces;
using HushServerNode.HushVotingLicensingIntegration;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// FEAT-013 Phase 6 architecture and host-composition tests: module dependency direction, exactly
/// one licensing configurator, release-metadata/config composition against the real release file,
/// the trusted-subject host boundary, and host assembly provenance.
/// </summary>
public sealed class HushVotingLicensingHostIntegrationTests
{
    // ------------------------------------------------------------------ dependency direction

    [Fact]
    public void Licensing_storage_module_never_depends_on_host_elections_caching_or_transport()
    {
        var moduleAssembly = typeof(LicenceEntitlementCoordinator).Assembly;
        var referenced = moduleAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        referenced.Should().Contain("HushNode.Interfaces");
        referenced.Should().Contain("HushShared.HushVoting.Licensing.Model");
        referenced.Should().Contain("Npgsql");
        referenced.Should().Contain("Microsoft.EntityFrameworkCore");

        referenced.Should().NotContain("HushServerNode");
        referenced.Where(name => name is not null
            && name.Contains("HushNode.Elections", StringComparison.Ordinal)).Should().BeEmpty();
        referenced.Should().NotContain("HushNode.Caching");
        referenced.Should().NotContain("HushNode.Blockchain");
        referenced.Should().NotContain("HushNode.Reactions");
        referenced.Should().NotContain("HushNode.Feeds");
        referenced.Should().NotContain("StackExchange.Redis");
        referenced.Where(name => name is not null
            && name.Contains("Grpc", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ single configurator

    [Fact]
    public void Module_registration_adds_exactly_one_licensing_configurator()
    {
        var services = new ServiceCollection();
        LicensingStorageHostBuild.RegisterHushVotingLicensingStorageServices(
            services, new HostBuilderContext(new Dictionary<object, object>()));

        using var provider = services.BuildServiceProvider();
        var configurators = provider.GetServices<IDbContextConfigurator>().ToArray();

        configurators.Should().HaveCount(1);
        configurators.Single().Should().BeOfType<LicensingDbContextConfigurator>();
    }

    [Fact]
    public void Fresh_host_context_contains_the_licensing_model_once()
    {
        var services = new ServiceCollection();
        LicensingStorageHostBuild.RegisterHushVotingLicensingStorageServices(
            services, new HostBuilderContext(new Dictionary<object, object>()));
        services.AddDbContext<HushNodeDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=unused;Username=u;Password=p"));

        using var provider = services.BuildServiceProvider();
        using var context = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(provider);

        context.Model.FindEntityType(typeof(LicenceSubjectEntity)).Should().NotBeNull();
        context.Model.FindEntityType(typeof(LicenceAssignmentEntity)).Should().NotBeNull();
        context.Model.FindEntityType(typeof(LicenceTransitionEventEntity)).Should().NotBeNull();
        context.Model.FindEntityType(typeof(LicenceActivationOperationEntity)).Should().NotBeNull();
        context.Model.FindEntityType(typeof(LicenceCatalogueReleaseEntity)).Should().NotBeNull();
    }

    // ------------------------------------------------------------------ release metadata + config

    [Fact]
    public void Release_metadata_reader_reads_the_authoritative_v1_release_file()
    {
        var contentRoot = LocateLicenceCatalogueContentRoot();
        var options = new HushVotingLicenceOptions();

        var metadata = HushVotingLicenceReleaseMetadataReader.ReadFromContentRoot(contentRoot, options);

        metadata.IsValid.Should().BeTrue(metadata.SafeError);
        metadata.CatalogueVersion.Should().Be(HushVotingLicenceCatalogueVersion.V1.Value);
        metadata.SchemaId.Should().Be(HushVotingLicenceCatalogueVersion.V1SchemaId);
        metadata.DigestSha256.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void Release_metadata_reader_fails_closed_on_missing_files()
    {
        var metadata = HushVotingLicenceReleaseMetadataReader.ReadFromContentRoot(
            "/nonexistent-content-root", new HushVotingLicenceOptions());

        metadata.IsValid.Should().BeFalse();
        metadata.SafeError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Host_composition_builds_the_licence_service_configuration_from_snapshot_and_release_metadata()
    {
        var services = new ServiceCollection();
        var snapshot = new HushVotingLicenceSnapshot(HushVotingLicenceCatalogueV1.CreateCatalogue());
        services.AddSingleton(snapshot);
        services.AddSingleton<IOptions<HushVotingLicenceOptions>>(
            new OptionsWrapper<HushVotingLicenceOptions>(new HushVotingLicenceOptions()));

        using var provider = services.BuildServiceProvider();
        var configuration = HushVotingLicensingIntegrationHostBuild.BuildLicenceServiceConfiguration(
            provider, LocateLicenceCatalogueContentRoot());

        configuration.CatalogueVersion.Should().Be(HushVotingLicenceCatalogueVersion.V1.Value);
        configuration.ReleaseDigestSha256.Should().MatchRegex("^[0-9A-F]{64}$");
        configuration.Catalogue.FindPlan(HushVotingLicencePlanId.DirectFree).Should().NotBeNull();
    }

    // ------------------------------------------------------------------ trusted subject boundary

    [Fact]
    public void Host_subject_boundary_normalizes_address_and_keeps_creation_block()
    {
        var subject = HushVotingLicensingIntegrationHostBuild.FromAuthenticatedIdentity(
            "  AbC123  ", 4_200_000, out var error);

        error.Should().BeNull();
        subject.Should().NotBeNull();
        subject!.SubjectType.Should().Be(LicencePersistenceVocabulary.SubjectTypeIdentity);
        subject.CanonicalPublicSigningAddress.Should().Be("abc123");
        subject.IdentityCreationBlockIndex.Should().Be(4_200_000);
    }

    [Fact]
    public void Host_subject_boundary_rejects_raw_or_invalid_values_with_stable_codes()
    {
        HushVotingLicensingIntegrationHostBuild.FromAuthenticatedIdentity("   ", 1, out var error1)
            .Should().BeNull();
        error1.Should().Be(AuthenticatedIdentitySubject.ErrorInvalidAddress);

        HushVotingLicensingIntegrationHostBuild.FromAuthenticatedIdentity("valid", -1, out var error2)
            .Should().BeNull();
        error2.Should().Be(AuthenticatedIdentitySubject.ErrorNegativeCreationBlock);
    }

    // ------------------------------------------------------------------ host registration surface

    [Fact]
    public void Host_integration_registration_adds_exactly_the_licensing_authority_set()
    {
        var services = new ServiceCollection();
        HushVotingLicensingIntegrationHostBuild.AddHushVotingLicensingIntegrationServices(services);

        services.Should().ContainSingle(d => d.ServiceType == typeof(LicenceServiceConfiguration));
        services.Should().ContainSingle(d => d.ServiceType == typeof(LicenceTelemetry));
        services.Should().ContainSingle(d => d.ServiceType == typeof(LicenceEntitlementService));
        services.Should().ContainSingle(d => d.ServiceType == typeof(HushVotingLicenceRolloutReadinessBootstrapper));
        services.Where(d => d.ServiceType == typeof(Olimpo.IBootstrapper)).Should().ContainSingle();
        services.Should().NotContain(d => d.ServiceType == typeof(LicensingDbContextConfigurator));
    }

    // ------------------------------------------------------------------ helpers

    private static string LocateLicenceCatalogueContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(
                directory.FullName,
                "Node",
                "HushServerNode",
                "licence-catalogues",
                "hushvoting-v1.0.0",
                "approved-licence-catalogue.release.json");
            if (File.Exists(marker))
            {
                // The host reads the catalogue relative to its content root (bin). For tests we
                // point at the repository layout: content root = repo root, options path then
                // resolves to Node/HushServerNode/licence-catalogues/...
                return Path.Combine(directory.FullName, "Node", "HushServerNode");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the licence-catalogues content root from test output.");
    }
}

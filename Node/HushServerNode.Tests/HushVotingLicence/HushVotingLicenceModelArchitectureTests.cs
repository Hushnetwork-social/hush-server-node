using System.Reflection;
using FluentAssertions;
using HushShared.HushVoting.Licensing.Model;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

public sealed class HushVotingLicenceModelArchitectureTests
{
    private static readonly Assembly ModelAssembly = typeof(HushVotingLicencePlanId).Assembly;

    [Fact]
    public void ModelAssembly_HasZeroProjectOrPackageReferences()
    {
        // A dependency-free model project's referenced assemblies resolve only to the shared
        // framework. Any project/package reference would surface as an extra assembly name.
        var references = ModelAssembly.GetReferencedAssemblies()
            .Select(static a => a.Name)
            .Where(static n => n is not null)
            .Select(static n => n!)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();

        var forbidden = references
            .Where(static name =>
                name.StartsWith("Hush", StringComparison.Ordinal) ||
                name.StartsWith("Olimpo", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                name.StartsWith("Npgsql", StringComparison.Ordinal) ||
                name.StartsWith("Grpc", StringComparison.Ordinal) ||
                name.StartsWith("StackExchange.Redis", StringComparison.Ordinal))
            .ToArray();

        forbidden.Should().BeEmpty(
            "the licensing model must be dependency-free (no host, EF, Npgsql, gRPC, Redis, or Olimpo reference)");
    }

    [Fact]
    public void ModelAssembly_ForbiddenNamespaces_AreAbsent()
    {
        var namespaceNames = typeof(HushVotingLicencePlanId).Assembly
            .GetTypes()
            .Select(static t => t.Namespace ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        namespaceNames.Should().OnlyContain(
            static ns => ns.Length == 0 ||
                ns.StartsWith("HushShared.HushVoting.Licensing.Model", StringComparison.Ordinal),
            "the model must not import host, storage, transport, or presentation namespaces");
    }

    [Fact]
    public void ModelAssembly_ContainsTheRequiredPublicLicensingContracts()
    {
        var publicTypes = ModelAssembly.GetExportedTypes().Select(static t => t.Name).ToHashSet(StringComparer.Ordinal);

        publicTypes.Should().Contain("HushVotingLicencePlanId");
        publicTypes.Should().Contain("HushVotingGovernanceOptionId");
        publicTypes.Should().Contain("HushVotingGovernanceOption");
        publicTypes.Should().Contain("HushVotingProfileCompatibilityEntry");
        publicTypes.Should().Contain("HushVotingLicenceCatalogueValidationResult");
        publicTypes.Should().Contain("HushVotingLicenceValidationFailure");
        publicTypes.Should().Contain("HushVotingLicenceValidationCodes");
    }
}

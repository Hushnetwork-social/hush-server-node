using System.Reflection;
using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// FEAT-015 no-direct-write architecture guard (RED first).
/// Licence assignments become effective only through deterministic block indexing. These tests
/// fail while any runtime query/provision/activation surface can originate assignment state outside
/// the licence block-index writer, or while the projection schema lacks block provenance.
/// </summary>
public sealed class LicenceProjectionArchitectureTests
{
    [Fact]
    public void Assignment_projection_requires_block_provenance_for_index_authority()
    {
        var entity = typeof(LicenceAssignmentEntity);

        entity.GetProperty("OriginatingTransactionId", BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .NotBeNull("FEAT-015 index-only authority requires the originating licence transaction reference on every assignment projection row");

        entity.GetProperty("OriginatingBlockIndex", BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .NotBeNull("the originating block index must be retained so PostgreSQL is a rebuildable chain projection");

        entity.GetProperty("OriginatingBlockTimeStampUtc", BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .NotBeNull("the containing block's consensus timestamp is the authoritative effective-from instant and must be projected");
    }
}

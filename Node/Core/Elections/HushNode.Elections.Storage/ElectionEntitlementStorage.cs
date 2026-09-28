using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HushNode.Elections.Storage;

public static class ElectionEntitlementStorageConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        var capture = builder.Entity<ElectionEntitlementCapture>();
        capture.ToTable("ElectionEntitlementCapture", "Elections");
        capture.HasKey(x => x.ElectionId);
        capture.Property(x => x.ElectionId).HasConversion(x => x.ToString(), x => ElectionIdHandler.CreateFromString(x)).HasColumnType("varchar(40)");
        capture.HasOne<ElectionRecord>().WithOne().HasForeignKey<ElectionEntitlementCapture>(x => x.ElectionId).OnDelete(DeleteBehavior.Restrict);
        capture.HasIndex(x => x.OpenTransactionId).IsUnique();
        capture.Property(x => x.AllowedGovernanceOptionIdsJson).HasColumnType("text");
        foreach (var p in capture.Metadata.GetProperties())
        {
            if (p.ClrType == typeof(string) && p.Name != nameof(ElectionEntitlementCapture.AllowedGovernanceOptionIdsJson)) p.SetMaxLength(256);
            p.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        var link = builder.Entity<ElectionRosterLinkBoundary>();
        link.ToTable("ElectionRosterLinkBoundary", "Elections");
        link.HasKey(x => x.ElectionId);
        link.Property(x => x.ElectionId).HasConversion(x => x.ToString(), x => ElectionIdHandler.CreateFromString(x)).HasColumnType("varchar(40)");
        link.HasOne<ElectionRecord>().WithOne().HasForeignKey<ElectionRosterLinkBoundary>(x => x.ElectionId).OnDelete(DeleteBehavior.Restrict);
        foreach (var p in link.Metadata.GetProperties()) p.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public enum ElectionEvidenceWriteOutcome { Added, IdenticalReplay, Conflict, Unsupported }

/// <summary>
/// Uses the caller's election write context/transaction; never opens another connection or commits.
/// Callers hold the election lock. Database constraints also reject concurrent/foreign writes.
/// </summary>
public static class ElectionEntitlementStorage
{
    public static async Task<ElectionEvidenceWriteOutcome> AddCaptureAsync(
        ElectionsDbContext context, ElectionEntitlementCapture capture, CancellationToken cancellationToken = default)
    {
        if (!capture.HasSupportedSemantics()) return ElectionEvidenceWriteOutcome.Unsupported;
        var existing = await context.ElectionEntitlementCaptures.FindAsync([capture.ElectionId], cancellationToken);
        if (existing is not null)
            return existing == capture ? ElectionEvidenceWriteOutcome.IdenticalReplay : ElectionEvidenceWriteOutcome.Conflict;
        context.ElectionEntitlementCaptures.Add(capture);
        return ElectionEvidenceWriteOutcome.Added;
    }

    public static async Task<ElectionEvidenceWriteOutcome> AddFirstLinkAsync(
        ElectionsDbContext context, ElectionRosterLinkBoundary boundary, CancellationToken cancellationToken = default)
    {
        if (boundary.ElectionId == ElectionId.Empty || boundary.SourceTransactionId == Guid.Empty || boundary.LinkedAtUtc.Kind != DateTimeKind.Utc)
            return ElectionEvidenceWriteOutcome.Unsupported;
        var existing = await context.ElectionRosterLinkBoundaries.FindAsync([boundary.ElectionId], cancellationToken);
        if (existing is not null)
            return existing == boundary ? ElectionEvidenceWriteOutcome.IdenticalReplay : ElectionEvidenceWriteOutcome.Conflict;
        context.ElectionRosterLinkBoundaries.Add(boundary);
        return ElectionEvidenceWriteOutcome.Added;
    }
}

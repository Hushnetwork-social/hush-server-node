namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Deterministic absolute-validity computation for licence-cache projections (pure, no storage):
/// at most the configured maximum TTL, deterministically reduced by 0-10% from the protected subject
/// digest to spread refresh load, and never beyond the assignment's upper-exclusive expiry. Cache
/// reads never slide or extend validity.
/// </summary>
public static class LicenceCacheTtlCalculator
{
    /// <summary>
    /// Computes the absolute cache validity boundary and a conservative Redis TTL.
    /// </summary>
    /// <param name="subjectDigest">32-byte protected subject digest (source of the deterministic jitter).</param>
    /// <param name="nowUtc">Current UTC instant.</param>
    /// <param name="assignmentExpiryUtc">Assignment upper-exclusive expiry when the plan is time bound; null otherwise.</param>
    /// <param name="options">Configured bounds (max TTL days, jitter percent).</param>
    /// <returns>Validity result; <see cref="LicenceCacheTtlResult.HasPositiveLifetime"/> is false when no positive lifetime exists.</returns>
    public static LicenceCacheTtlResult Compute(
        byte[] subjectDigest,
        DateTime nowUtc,
        DateTime? assignmentExpiryUtc,
        LicenceCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(subjectDigest);
        ArgumentNullException.ThrowIfNull(options);

        if (subjectDigest.Length != 32)
        {
            throw new ArgumentException("Subject digest must be 32 bytes.", nameof(subjectDigest));
        }

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));
        }

        var maxTtl = TimeSpan.FromDays(options.MaxTtlDays);

        // Deterministic 0..MaxTtlJitterPercent reduction from the protected digest (test reproducible).
        var jitterPercent = subjectDigest[0] % (options.MaxTtlJitterPercent + 1);
        var reducedTtl = maxTtl - TimeSpan.FromTicks(maxTtl.Ticks * jitterPercent / 100L);

        var boundary = nowUtc + reducedTtl;

        // Annual assignment: validity never outlives the upper-exclusive assignment expiry.
        if (assignmentExpiryUtc is { Kind: DateTimeKind.Utc } expiry && expiry < boundary)
        {
            boundary = expiry;
        }

        // Conservative rounding: Redis TTL (whole seconds) must never outlive the computed boundary.
        var remaining = boundary - nowUtc;
        var ttlSeconds = remaining > TimeSpan.Zero
            ? (long)Math.Floor(remaining.TotalSeconds)
            : 0L;

        return new LicenceCacheTtlResult(
            boundary,
            ttlSeconds,
            ttlSeconds > 0 && boundary > nowUtc);
    }
}

/// <summary>Absolute validity computation result.</summary>
public sealed record LicenceCacheTtlResult(
    DateTime CacheValidUntilUtc,
    long RedisTtlSeconds,
    bool HasPositiveLifetime);

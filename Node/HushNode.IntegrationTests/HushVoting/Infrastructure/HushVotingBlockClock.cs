namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>
/// Historical Given setup only. Public, signed transactions still pass actual admission,
/// block production and indexing. Query/auth/browser clocks are never replaced.
/// </summary>
internal sealed class HushVotingBlockClock : TimeProvider
{
    private DateTimeOffset? _historical = DateTimeOffset.UtcNow.AddYears(-1).AddMinutes(-5);

    public override DateTimeOffset GetUtcNow() => _historical ?? DateTimeOffset.UtcNow;

    public void AdvanceTo(DateTimeOffset instant)
    {
        if (instant < GetUtcNow()) throw new InvalidOperationException("Fixture block time must advance monotonically.");
        _historical = instant;
    }

    public void UseSystemTime() => _historical = null;
}

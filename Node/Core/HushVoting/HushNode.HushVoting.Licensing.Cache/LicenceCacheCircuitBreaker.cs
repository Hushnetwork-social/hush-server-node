namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Licence-cache-specific circuit breaker: three consecutive Redis connection/timeout failures open
/// the circuit for a bounded interval; afterwards one half-open probe is permitted while other calls
/// bypass Redis. A successful probe closes the circuit; a failed probe reopens it. Data-level
/// invalid/corrupt misses are measured separately and never open this connection circuit.
/// Deterministic time is injected so tests never sleep on wall clocks.
/// </summary>
public sealed class LicenceCacheCircuitBreaker
{
    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen,
    }

    private readonly object _gate = new();
    private readonly Func<DateTime> _utcNow;
    private readonly int _openFailureThreshold;
    private readonly TimeSpan _openInterval;

    private CircuitState _state = CircuitState.Closed;
    private int _consecutiveFailures;
    private DateTime _openUntilUtc;
    private int _halfOpenProbesInFlight;

    public LicenceCacheCircuitBreaker(
        Func<DateTime> utcNow,
        LicenceCacheOptions options)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _openFailureThreshold = options.CircuitOpenFailureCount;
        _openInterval = TimeSpan.FromSeconds(options.CircuitOpenSeconds);
    }

    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                if (_state == CircuitState.Open && _utcNow() >= _openUntilUtc)
                {
                    return CircuitState.HalfOpen;
                }

                return _state;
            }
        }
    }

    public bool IsClosed => State == CircuitState.Closed;

    /// <summary>
    /// True when a Redis attempt may proceed (closed, or exactly one half-open probe at a time).
    /// </summary>
    public bool IsAttemptPermitted()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case CircuitState.Closed:
                    return true;
                case CircuitState.Open when _utcNow() >= _openUntilUtc:
                    _state = CircuitState.HalfOpen;
                    _halfOpenProbesInFlight = 1;
                    return true;
                case CircuitState.HalfOpen when _halfOpenProbesInFlight > 0:
                    return false;
                default:
                    return false;
            }
        }
    }

    public void RecordConnectionFailure()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case CircuitState.Closed:
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= _openFailureThreshold)
                    {
                        _state = CircuitState.Open;
                        _openUntilUtc = _utcNow() + _openInterval;
                        _consecutiveFailures = 0;
                    }

                    break;
                case CircuitState.HalfOpen:
                    // A permitted probe failed: reopen for another full interval.
                    _state = CircuitState.Open;
                    _openUntilUtc = _utcNow() + _openInterval;
                    _halfOpenProbesInFlight = 0;
                    break;
                case CircuitState.Open:
                    break;
            }
        }
    }

    public void RecordProbeSuccess()
    {
        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _consecutiveFailures = 0;
                _halfOpenProbesInFlight = 0;
            }
        }
    }
}

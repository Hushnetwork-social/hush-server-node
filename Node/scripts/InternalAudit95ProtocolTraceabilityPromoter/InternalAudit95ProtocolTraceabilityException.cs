namespace InternalAudit95ProtocolTraceabilityPromoter;

public sealed class InternalAudit95ProtocolTraceabilityException : Exception
{
    public InternalAudit95ProtocolTraceabilityException(string message, IReadOnlyList<string>? details = null)
        : base(message)
    {
        Details = details ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

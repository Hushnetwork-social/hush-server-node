namespace FailedFinalizeContinuityRehearsalPromoter;

public sealed class FailedFinalizeContinuityPromotionException : Exception
{
    public FailedFinalizeContinuityPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

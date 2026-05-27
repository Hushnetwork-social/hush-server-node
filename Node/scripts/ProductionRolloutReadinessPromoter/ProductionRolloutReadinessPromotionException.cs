namespace ProductionRolloutReadinessPromoter;

public sealed class ProductionRolloutReadinessPromotionException : Exception
{
    public ProductionRolloutReadinessPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

namespace SecondProductionLikeOperationalRunPromoter;

public sealed class SecondProductionLikeOperationalRunPromotionException : Exception
{
    public SecondProductionLikeOperationalRunPromotionException(string message)
        : this(message, [])
    {
    }

    public SecondProductionLikeOperationalRunPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

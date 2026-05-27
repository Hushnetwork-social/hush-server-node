namespace PublicationCountingHardeningPromoter;

public sealed class PublicationCountingHardeningPromotionException : Exception
{
    public PublicationCountingHardeningPromotionException(string message)
        : this(message, [])
    {
    }

    public PublicationCountingHardeningPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}


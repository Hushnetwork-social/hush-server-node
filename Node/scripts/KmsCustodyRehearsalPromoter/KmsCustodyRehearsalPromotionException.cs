namespace KmsCustodyRehearsalPromoter;

public sealed class KmsCustodyRehearsalPromotionException : Exception
{
    public KmsCustodyRehearsalPromotionException(string message)
        : this(message, [])
    {
    }

    public KmsCustodyRehearsalPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

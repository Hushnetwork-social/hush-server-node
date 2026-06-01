namespace PublicationCountingReplayPromoter;

public sealed class PublicationCountingReplayPromotionException : Exception
{
    public PublicationCountingReplayPromotionException(string message)
        : this(message, [])
    {
    }

    public PublicationCountingReplayPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}


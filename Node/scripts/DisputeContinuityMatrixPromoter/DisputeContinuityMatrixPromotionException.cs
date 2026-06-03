namespace DisputeContinuityMatrixPromoter;

public sealed class DisputeContinuityMatrixPromotionException : Exception
{
    public DisputeContinuityMatrixPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

namespace RetentionLogPrivacyRecurringScanPromoter;

public sealed class RetentionLogPrivacyRecurringScanPromotionException : Exception
{
    public RetentionLogPrivacyRecurringScanPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

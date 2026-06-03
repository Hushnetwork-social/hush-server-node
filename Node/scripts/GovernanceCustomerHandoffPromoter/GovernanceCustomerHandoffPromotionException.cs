namespace GovernanceCustomerHandoffPromoter;

public sealed class GovernanceCustomerHandoffPromotionException : Exception
{
    public GovernanceCustomerHandoffPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

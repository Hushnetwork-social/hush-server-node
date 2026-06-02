namespace DeploymentRollbackRehearsalPromoter;

public sealed class DeploymentRollbackRehearsalPromotionException : Exception
{
    public DeploymentRollbackRehearsalPromotionException(string message)
        : this(message, [])
    {
    }

    public DeploymentRollbackRehearsalPromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

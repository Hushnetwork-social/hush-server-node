namespace PublicStateElectionPrerequisiteRegisterPromoter;

public sealed class PublicStateElectionPrerequisitePromotionException : Exception
{
    public PublicStateElectionPrerequisitePromotionException(string message)
        : base(message)
    {
        Details = [];
    }

    public PublicStateElectionPrerequisitePromotionException(string message, IReadOnlyList<string> details)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyList<string> Details { get; }
}

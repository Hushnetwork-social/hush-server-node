namespace ProductionLikeOperationalRunPromoter;

public sealed class ProductionLikeOperationalRunPromotionException : Exception
{
    public ProductionLikeOperationalRunPromotionException(string message)
        : base(message)
    {
    }

    public ProductionLikeOperationalRunPromotionException(string message, IReadOnlyList<string> errors)
        : base($"{message}{Environment.NewLine}{string.Join(Environment.NewLine, errors)}")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; } = [];
}

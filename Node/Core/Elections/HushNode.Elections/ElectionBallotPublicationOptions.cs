namespace HushNode.Elections;

public sealed record ElectionBallotPublicationOptions(
    int HighWaterMark = 20,
    int LowWaterMark = 10,
    int MaxBatchPerBlock = 20)
{
    public void Validate()
    {
        if (HighWaterMark < 1)
        {
            throw new InvalidOperationException("Ballot publication high-water mark must be at least 1.");
        }

        if (LowWaterMark < 0)
        {
            throw new InvalidOperationException("Ballot publication low-water mark cannot be negative.");
        }

        if (LowWaterMark >= HighWaterMark)
        {
            throw new InvalidOperationException("Ballot publication low-water mark must be lower than the high-water mark.");
        }

        if (MaxBatchPerBlock < 1)
        {
            throw new InvalidOperationException("Ballot publication max batch per block must be at least 1.");
        }
    }
}

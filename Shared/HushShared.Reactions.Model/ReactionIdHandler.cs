namespace HushShared.Reactions.Model;

public static class ReactionIdHandler
{
    public static ReactionId CreateFromString(string value) => new(Guid.Parse(value));
}

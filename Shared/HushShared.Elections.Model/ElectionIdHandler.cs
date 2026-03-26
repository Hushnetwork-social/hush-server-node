namespace HushShared.Elections.Model;

public static class ElectionIdHandler
{
    public static ElectionId CreateFromString(string value) => new(Guid.Parse(value));
}

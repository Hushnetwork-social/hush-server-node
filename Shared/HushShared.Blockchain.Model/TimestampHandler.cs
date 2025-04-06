namespace HushShared.Blockchain.Model;

public static class TimestampHandler
{
    public static Timestamp CreateFromString(string value) => new(DateTime.Parse(value));
}

namespace HushServerNode;

/// <summary>
/// Entry point for the HushServerNode application.
/// This is a thin wrapper that delegates to HushServerNodeCore.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        await using var node = HushServerNodeCore.CreateForProduction(args);
        await node.RunAsync();
    }
}

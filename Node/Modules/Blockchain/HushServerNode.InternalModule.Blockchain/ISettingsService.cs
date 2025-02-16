namespace HushServerNode.InternalModule.Blockchain;

public interface ISettingsService
{
    string GetSettings(string table, string key);
}

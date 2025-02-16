namespace HushServerNode.InternalModule.Blockchain;

public class SettingsdService : ISettingsService
{
    private readonly IBlockchainDbAccess _blockchainDbAccess;

    public SettingsdService(IBlockchainDbAccess blockchainDbAccess)
    {
        this._blockchainDbAccess = blockchainDbAccess;
    }

    public string GetSettings(string table, string key)
    {
        return this._blockchainDbAccess.GetSettings(table, key);
    }
}

using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain.IndexStrategies;

public class SettingsTransactionIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainDbAccess _blockchainDbAccess;

    public SettingsTransactionIndexStrategy(IBlockchainDbAccess blockchainDbAccess)
    {
        this._blockchainDbAccess = blockchainDbAccess;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is Settings)
        {
            return true;
        }

        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var settings = verifiedTransaction.SpecificTransaction as Settings;

        var settingsEntity = new SettingsEntity
        {
            SettingsId = settings.SettingId,
            SettingsType = settings.SettingsTable,
            Key = settings.Key,
            Value = settings.Value,
            ValidSinceBlock = settings.ValidSinceBlock,
            ValidUntilBlock = settings.ValidUntilBlock
        };

        await this._blockchainDbAccess.SaveSettingsAsync(settingsEntity);
    }
}

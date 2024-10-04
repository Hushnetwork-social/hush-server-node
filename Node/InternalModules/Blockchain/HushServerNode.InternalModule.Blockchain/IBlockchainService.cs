using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockchainService
{
    Task InitializeBlockchainAsync();

    Task SaveSettingsAsync(Settings settings);
}

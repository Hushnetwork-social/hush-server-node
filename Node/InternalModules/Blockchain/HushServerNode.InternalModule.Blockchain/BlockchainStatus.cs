using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain.Cache;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainStatus : IBlockchainStatus
{
    private readonly IBlockchainDbAccess _blockchainDbAccess;

    public BlockchainState BlockchainState = null;


    public long BlockIndex { get; private set; }

    public string PreviousBlockId { get; private set; } = string.Empty;

    public string BlockId { get; private set;} = string.Empty;

    public string NextBlockId { get; private set; } = string.Empty;

    public string PublicSigningAddress { get; } = string.Empty;

    public string PrivateSigningKey { get; } = string.Empty;

    public string PublicEncryptionAddress { get; } = string.Empty;

    public string PrivateEncrpytionKey { get; } = string.Empty;

    public BlockchainStatus(
        IConfiguration configuration,
        IBlockchainDbAccess blockchainDbAccess)
    {
        this._blockchainDbAccess = blockchainDbAccess;

        this.PublicSigningAddress = configuration["StackerInfo:PublicSigningAddress"];
        this.PrivateSigningKey = configuration["StackerInfo:PrivateSigningKey"];
        this.PublicEncryptionAddress = configuration["StackerInfo:PublicEncryptAddress"];
        this.PrivateEncrpytionKey = configuration["StackerInfo:PrivateEncryptKey"];
    }

    public void UpdateBlockchainStatus(
        long blockIndex, 
        string previousBlockId, 
        string blockId,
        string nextBlockId)
    {
        this.BlockIndex = blockIndex;
        this.PreviousBlockId = previousBlockId;
        this.BlockId = blockId;
        this.NextBlockId = nextBlockId;

        if (this.BlockchainState == null)
        {
            this.BlockchainState = new BlockchainState
            {
                BlockchainStateId = Guid.NewGuid(),
                LastBlockIndex = blockIndex,
                CurrentPreviousBlockId = previousBlockId,
                CurrentBlockId = blockId,
                CurrentNextBlockId = nextBlockId,
            };
        }
        else
        {
            this.BlockchainState.LastBlockIndex = blockIndex;
            this.BlockchainState.CurrentPreviousBlockId = previousBlockId;
            this.BlockchainState.CurrentBlockId = blockId;
            this.BlockchainState.CurrentNextBlockId = nextBlockId;
        }
    }

    public async Task LoadBlockchainStatus()
    {
        this.BlockchainState = await this._blockchainDbAccess.GetBlockchainStateAsync();

        this.PreviousBlockId = this.BlockchainState.CurrentPreviousBlockId;
        this.BlockId = this.BlockchainState.CurrentBlockId;
        this.NextBlockId = this.BlockchainState.CurrentNextBlockId;
        this.BlockIndex = this.BlockchainState.LastBlockIndex;
    }
}

namespace HushServerNode.InternalModule.Bank.Cache;

public class NonFungibleTokenEntity
{
    public string NonFungibleTokenId { get; set; } = string.Empty;

    public string OwnerPublicAddress { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string NonFungibleTokenType { get; set; } = string.Empty;

    public bool EncryptedContent { get; set; }

    public long BlockIndex { get; set; }
}

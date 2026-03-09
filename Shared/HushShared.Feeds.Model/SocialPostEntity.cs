using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

public class SocialPostEntity
{
    public Guid PostId { get; set; }

    public Guid ReactionScopeId { get; set; }

    public string AuthorPublicAddress { get; set; } = string.Empty;

    public byte[]? AuthorCommitment { get; set; }

    public string Content { get; set; } = string.Empty;

    public SocialPostVisibility AudienceVisibility { get; set; }

    public BlockIndex CreatedAtBlock { get; set; } = new(0);

    public long CreatedAtUnixMs { get; set; }

    public List<SocialPostAudienceCircleEntity> AudienceCircles { get; set; } = [];
}

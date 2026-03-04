using FluentAssertions;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialPostPayloadContractsTests
{
    [Fact]
    public void CreateSocialPostPayload_ShouldPreserveContractFields()
    {
        var postId = Guid.NewGuid();
        var author = "03abc";
        var audience = new SocialPostAudience(SocialPostVisibility.Private, ["circle-1"]);
        var attachments = new[]
        {
            new SocialPostAttachment(
                AttachmentId: "att-1",
                MimeType: "image/jpeg",
                Size: 1024,
                FileName: "photo.jpg",
                Hash: new string('a', 64),
                Kind: SocialPostAttachmentKind.Image)
        };

        var payload = new CreateSocialPostPayload(
            postId,
            author,
            "hello world",
            audience,
            attachments,
            CreatedAtUnixMs: 1234567890);

        payload.PostId.Should().Be(postId);
        payload.AuthorPublicAddress.Should().Be(author);
        payload.Audience.Visibility.Should().Be(SocialPostVisibility.Private);
        payload.Audience.CircleFeedIds.Should().ContainSingle().Which.Should().Be("circle-1");
        payload.Attachments.Should().HaveCount(1);
    }

    [Fact]
    public void ValidateAudience_WithPrivateAndNoCircles_ShouldFail()
    {
        var result = SocialPostContractRules.ValidateAudience(
            new SocialPostAudience(SocialPostVisibility.Private, []));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialPostContractErrorCode.PrivateAudienceRequiresAtLeastOneCircle);
    }

    [Fact]
    public void ValidateAudience_WithDuplicateCircles_ShouldFail()
    {
        var result = SocialPostContractRules.ValidateAudience(
            new SocialPostAudience(SocialPostVisibility.Private, ["CIRCLE-1", "circle-1"]));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialPostContractErrorCode.DuplicateCircleTargets);
    }

    [Fact]
    public void ValidateAttachments_WithMoreThanFive_ShouldFail()
    {
        var attachments = Enumerable.Range(1, 6)
            .Select(index => new SocialPostAttachment(
                AttachmentId: $"att-{index}",
                MimeType: "image/png",
                Size: 128,
                FileName: $"image-{index}.png",
                Hash: new string('a', 64),
                Kind: SocialPostAttachmentKind.Image))
            .ToArray();

        var result = SocialPostContractRules.ValidateAttachments(attachments);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialPostContractErrorCode.AttachmentCountExceeded);
    }

    [Fact]
    public void ValidateAttachments_WithUnsupportedMime_ShouldFail()
    {
        var attachments = new[]
        {
            new SocialPostAttachment(
                AttachmentId: "att-doc",
                MimeType: "application/pdf",
                Size: 2048,
                FileName: "doc.pdf",
                Hash: new string('b', 64),
                Kind: SocialPostAttachmentKind.Image)
        };

        var result = SocialPostContractRules.ValidateAttachments(attachments);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialPostContractErrorCode.AttachmentMimeTypeNotAllowed);
    }
}

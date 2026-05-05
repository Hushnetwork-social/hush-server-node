using Google.Protobuf;
using HushNetwork.proto;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections.gRPC;

internal static partial class ElectionGrpcMappings
{
    public static ElectionVerificationPackageViewProto ToProto(this VerificationPackageView packageView) =>
        packageView switch
        {
            VerificationPackageView.PublicAnonymous => ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous,
            VerificationPackageView.RestrictedOwnerAuditor => ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor,
            _ => ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous,
        };

    public static VerificationPackageView ToDomain(this ElectionVerificationPackageViewProto packageView) =>
        packageView switch
        {
            ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor => VerificationPackageView.RestrictedOwnerAuditor,
            _ => VerificationPackageView.PublicAnonymous,
        };

    public static ElectionVerificationArtifactVisibilityProto ToProto(this VerificationArtifactVisibility visibility) =>
        visibility switch
        {
            VerificationArtifactVisibility.Restricted => ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted,
            _ => ElectionVerificationArtifactVisibilityProto.VerificationArtifactPublic,
        };

    public static ElectionVerifierOverallStatusProto ToProto(this VerificationOverallStatus status) =>
        status switch
        {
            VerificationOverallStatus.Pass => ElectionVerifierOverallStatusProto.ElectionVerifierPass,
            VerificationOverallStatus.Warn => ElectionVerifierOverallStatusProto.ElectionVerifierWarn,
            VerificationOverallStatus.Fail => ElectionVerifierOverallStatusProto.ElectionVerifierFail,
            _ => ElectionVerifierOverallStatusProto.ElectionVerifierNotAvailable,
        };

    public static ElectionVerificationPackageFileView ToProto(this ElectionVerificationPackageFile file) =>
        new()
        {
            RelativePath = file.RelativePath,
            MediaType = file.MediaType,
            Visibility = file.Visibility.ToProto(),
            Content = ByteString.CopyFrom(file.Content),
        };
}

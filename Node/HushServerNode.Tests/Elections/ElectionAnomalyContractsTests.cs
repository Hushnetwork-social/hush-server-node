using FluentAssertions;
using HushNetwork.proto;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyContractsTests
{
    [Fact]
    public void CategoryIds_ShouldMatchEpic014StableTaxonomy()
    {
        ElectionAnomalyCategoryIds.All.Should().Equal(
            ElectionAnomalyCategoryIds.AccessOrAuthenticationAnomaly,
            ElectionAnomalyCategoryIds.BallotCastingOrReceiptAnomaly,
            ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            ElectionAnomalyCategoryIds.CountingOrTallyAnomaly,
            ElectionAnomalyCategoryIds.ReportingOrAuditPackageAnomaly,
            ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
            ElectionAnomalyCategoryIds.ExternalObjectionOrComplaint,
            ElectionAnomalyCategoryIds.OtherProcessAnomaly);

        ElectionAnomalyCategoryIds.IsKnown(ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly)
            .Should()
            .BeTrue();
        ElectionAnomalyCategoryIds.IsKnown("trustee lost key").Should().BeFalse();
    }

    [Fact]
    public void CaseStateIds_ShouldMatchEpic014IntakeStates()
    {
        ElectionAnomalyCaseStateIds.All.Should().Equal(
            ElectionAnomalyCaseStateIds.Submitted,
            ElectionAnomalyCaseStateIds.UnderReview,
            ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
            ElectionAnomalyCaseStateIds.SubmitterInformationProvided,
            ElectionAnomalyCaseStateIds.OwnerResponded,
            ElectionAnomalyCaseStateIds.EscalatedToGovernedDecision,
            ElectionAnomalyCaseStateIds.ResolvedNonBlocking,
            ElectionAnomalyCaseStateIds.ClosedDuplicateFollowup,
            ElectionAnomalyCaseStateIds.ClosedNoFurtherSubmitterInput);

        ElectionAnomalyCaseStateIds.IsKnown(ElectionAnomalyCaseStateIds.AuthorityRequestedInformation)
            .Should()
            .BeTrue();
        ElectionAnomalyCaseStateIds.IsKnown("finalized_with_anomaly").Should().BeFalse();
    }

    [Fact]
    public void ValidationCodes_ShouldExposeDeepDiveStableCodes()
    {
        ElectionAnomalyValidationCodes.All.Should().Equal(
            ElectionAnomalyValidationCodes.DirectWriteForbidden,
            ElectionAnomalyValidationCodes.InvalidActionSignatory,
            ElectionAnomalyValidationCodes.SubmitterScopeClientSupplied,
            ElectionAnomalyValidationCodes.PersonScopeUnresolved,
            ElectionAnomalyValidationCodes.DuplicateThread,
            ElectionAnomalyValidationCodes.CategoryInvalid,
            ElectionAnomalyValidationCodes.BodyRequired,
            ElectionAnomalyValidationCodes.BodyTooLong,
            ElectionAnomalyValidationCodes.SubmissionWindowClosed,
            ElectionAnomalyValidationCodes.FollowupNotRequested,
            ElectionAnomalyValidationCodes.ClarificationRequestNotOpen,
            ElectionAnomalyValidationCodes.ClarificationRequestAlreadyOpen,
            ElectionAnomalyValidationCodes.RecipientWrapMissing,
            ElectionAnomalyValidationCodes.ReadForbidden);

        ElectionAnomalyValidationCodes.IsKnown(ElectionAnomalyValidationCodes.DuplicateThread)
            .Should()
            .BeTrue();
        ElectionAnomalyValidationCodes.IsKnown("duplicate anomaly").Should().BeFalse();
    }

    [Fact]
    public void ActionTypes_ShouldExposeSignedTransactionOnlyAnomalyActions()
    {
        var actionTypes = new[]
        {
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            EncryptedElectionEnvelopeActionTypes.RequestAnomalyInformation,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyInformation,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAuthorityResponse,
            EncryptedElectionEnvelopeActionTypes.ClassifyAnomalyThread,
            EncryptedElectionEnvelopeActionTypes.RegisterExternalAnomalyClaimant,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAuditorRecipientRewrap,
        };

        actionTypes.Should().Equal(
            "submit_anomaly_thread",
            "request_anomaly_information",
            "submit_anomaly_information",
            "record_anomaly_authority_response",
            "classify_anomaly_thread",
            "register_external_anomaly_claimant",
            "record_anomaly_attachment_manifest",
            "record_anomaly_auditor_recipient_rewrap");
    }

    [Fact]
    public void WritePayloads_ShouldCarryActorAndNonceWithoutClientSuppliedSubmitterScope()
    {
        var payloadTypes = new[]
        {
            typeof(SubmitElectionAnomalyThreadActionPayload),
            typeof(RequestElectionAnomalyInformationActionPayload),
            typeof(SubmitElectionAnomalyInformationActionPayload),
            typeof(RecordElectionAnomalyAuthorityResponseActionPayload),
            typeof(ClassifyElectionAnomalyThreadActionPayload),
            typeof(RegisterExternalElectionAnomalyClaimantActionPayload),
            typeof(RecordElectionAnomalyAttachmentManifestActionPayload),
            typeof(RecordElectionAnomalyAuditorRecipientRewrapActionPayload),
        };

        foreach (var payloadType in payloadTypes)
        {
            payloadType.GetProperty("ActorPublicAddress").Should().NotBeNull(payloadType.Name);
            payloadType.GetProperty("ActionNonce")?.PropertyType.Should().Be(typeof(Guid), payloadType.Name);
            payloadType.GetProperties()
                .Should()
                .NotContain(
                    property => property.Name.Contains("PersonScope", StringComparison.OrdinalIgnoreCase),
                    $"{payloadType.Name} must not accept a client-supplied person scope");
        }
    }

    [Fact]
    public void HushElectionsService_ShouldNotExposeDirectAnomalyMutationMethods()
    {
        var serviceMethodNames = typeof(HushElections.HushElectionsBase)
            .GetMethods()
            .Select(method => method.Name);

        serviceMethodNames.Should().NotContain(
            [
                "AddElectionAnomaly",
                "CreateElectionAnomaly",
                "SubmitElectionAnomaly",
                "RequestElectionAnomalyInformation",
                "SubmitElectionAnomalyInformation",
                "RecordElectionAnomalyAuthorityResponse",
                "ClassifyElectionAnomalyThread",
                "RegisterExternalElectionAnomalyClaimant",
                "RecordElectionAnomalyAttachmentManifest",
                "RecordElectionAnomalyAuditorRecipientRewrap",
            ],
            "anomaly writes must enter through HushBlockchain.SubmitSignedTransaction");
    }

    [Fact]
    public void AuditorRestrictedProjectionContracts_ShouldNotExposeSubmitterOrActorReferences()
    {
        var projectionTypes = new[]
        {
            typeof(ElectionAnomalyAuditorRestrictedReviewProjection),
            typeof(ElectionAnomalyAuditorRestrictedThreadProjection),
            typeof(ElectionAnomalyReportManifestSeedProjection),
            typeof(ElectionAnomalyReportManifestThreadProjection),
            typeof(ElectionAnomalyRestrictedMessageProjection),
            typeof(ElectionAnomalyRestrictedRecipientWrapProjection),
            typeof(ElectionAnomalyAuditorCallerWrapProjection),
        };

        foreach (var projectionType in projectionTypes)
        {
            projectionType.GetProperties()
                .Should()
                .NotContain(
                    property =>
                        property.Name.Contains("Submitter", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Actor", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("PersonScope", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("RoleContext", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("PublicAddress", StringComparison.OrdinalIgnoreCase),
                    $"{projectionType.Name} must stay auditor-safe in v1");
        }
    }
}

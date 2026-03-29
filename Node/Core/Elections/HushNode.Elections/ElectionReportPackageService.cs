using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public sealed class ElectionReportPackageService : IElectionReportPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public ElectionReportPackageBuildResult Build(ElectionReportPackageBuildRequest request)
    {
        var frozenEvidenceJson = SerializeJson(BuildFrozenEvidenceProjection(request));
        var frozenEvidenceHash = ComputeHashBytes(frozenEvidenceJson);
        var frozenEvidenceFingerprint = BuildHashHex(frozenEvidenceHash);

        try
        {
            var consistencyFailure = ValidateConsistency(request);
            if (consistencyFailure is not null)
            {
                return ElectionReportPackageBuildResult.Failure(CreateFailedAttempt(
                    request,
                    frozenEvidenceHash,
                    frozenEvidenceFingerprint,
                    consistencyFailure.Value.Code,
                    consistencyFailure.Value.Reason));
            }

            var packageId = Guid.NewGuid();
            var trustees = request.TrusteeInvitations
                .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
                .OrderBy(x => x.TrusteeDisplayName ?? x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
                .Select(x => new TrusteeProjection(
                    x.TrusteeUserAddress,
                    x.TrusteeDisplayName,
                    x.Status.ToString(),
                    x.SentAt,
                    x.RespondedAt))
                .ToArray();
            var participationLookup = request.ParticipationRecords.ToDictionary(
                x => x.OrganizationVoterId,
                StringComparer.OrdinalIgnoreCase);
            var rosterEntries = request.RosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                .Select(x => BuildRosterEntryProjection(x, participationLookup.GetValueOrDefault(x.OrganizationVoterId)))
                .ToArray();
            var outcomeProjection = BuildOutcomeProjection(
                request.Election,
                request.OfficialResult,
                request.CloseEligibilitySnapshot);
            var resultReportProjection = BuildResultReportProjection(
                request.Election,
                request.OfficialResult,
                outcomeProjection);
            var auditProjection = BuildAuditProjection(
                request,
                frozenEvidenceFingerprint,
                trustees);
            var manifestProjection = BuildManifestProjection(
                request,
                packageId,
                frozenEvidenceFingerprint,
                trustees.Length,
                rosterEntries.Length,
                outcomeProjection);
            var evidenceGraphProjection = BuildEvidenceGraphProjection(
                request,
                trustees,
                rosterEntries.Length);

            var machineManifestId = Guid.NewGuid();
            var humanManifestId = Guid.NewGuid();
            var machineEvidenceGraphId = Guid.NewGuid();
            var machineResultId = Guid.NewGuid();
            var humanResultId = Guid.NewGuid();
            var machineRosterId = Guid.NewGuid();
            var humanRosterId = Guid.NewGuid();
            var machineAuditId = Guid.NewGuid();
            var humanAuditId = Guid.NewGuid();
            var machineOutcomeId = Guid.NewGuid();
            var humanOutcomeId = Guid.NewGuid();
            var machineDisputeId = Guid.NewGuid();
            var humanDisputeId = Guid.NewGuid();

            var artifacts = new List<ElectionReportArtifactRecord>
            {
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineManifestId,
                    humanManifestId,
                    ElectionReportArtifactKind.MachineManifest,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    7,
                    "Canonical manifest",
                    "canonical-manifest.json",
                    manifestProjection with
                    {
                        MachineArtifactId = machineManifestId,
                        HumanArtifactId = humanManifestId,
                        EvidenceGraphArtifactId = machineEvidenceGraphId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanManifestId,
                    machineManifestId,
                    ElectionReportArtifactKind.HumanManifest,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    1,
                    "Final manifest",
                    "final-manifest.md",
                    BuildHumanManifestContent(
                        manifestProjection,
                        packageId,
                        frozenEvidenceFingerprint,
                        machineManifestId,
                        humanManifestId,
                        machineEvidenceGraphId)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineEvidenceGraphId,
                    null,
                    ElectionReportArtifactKind.MachineEvidenceGraph,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    8,
                    "Evidence graph",
                    "evidence-graph.json",
                    evidenceGraphProjection with
                    {
                        ArtifactId = machineEvidenceGraphId,
                        ManifestArtifactId = machineManifestId,
                    }),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineResultId,
                    humanResultId,
                    ElectionReportArtifactKind.MachineResultReportProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    9,
                    "Final result report projection",
                    "result-report.json",
                    resultReportProjection with
                    {
                        MachineArtifactId = machineResultId,
                        HumanArtifactId = humanResultId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanResultId,
                    machineResultId,
                    ElectionReportArtifactKind.HumanResultReport,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    2,
                    "Final result report",
                    "final-result-report.md",
                    BuildHumanResultReportContent(resultReportProjection)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineRosterId,
                    humanRosterId,
                    ElectionReportArtifactKind.MachineNamedParticipationRosterProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    10,
                    "Named participation roster projection",
                    "named-participation-roster.json",
                    new RosterProjection(
                        machineRosterId,
                        humanRosterId,
                        request.Election.ElectionId.ToString(),
                        rosterEntries.Length,
                        rosterEntries)),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanRosterId,
                    machineRosterId,
                    ElectionReportArtifactKind.HumanNamedParticipationRoster,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    3,
                    "Named participation roster",
                    "named-participation-roster.md",
                    BuildHumanRosterContent(request.Election, rosterEntries)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineAuditId,
                    humanAuditId,
                    ElectionReportArtifactKind.MachineAuditProvenanceReportProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    11,
                    "Audit and provenance projection",
                    "audit-provenance-report.json",
                    auditProjection with
                    {
                        MachineArtifactId = machineAuditId,
                        HumanArtifactId = humanAuditId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanAuditId,
                    machineAuditId,
                    ElectionReportArtifactKind.HumanAuditProvenanceReport,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    4,
                    "Audit and provenance report",
                    "audit-provenance-report.md",
                    BuildHumanAuditContent(auditProjection)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineOutcomeId,
                    humanOutcomeId,
                    ElectionReportArtifactKind.MachineOutcomeDeterminationProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    12,
                    "Outcome determination projection",
                    "outcome-determination.json",
                    outcomeProjection with
                    {
                        MachineArtifactId = machineOutcomeId,
                        HumanArtifactId = humanOutcomeId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanOutcomeId,
                    machineOutcomeId,
                    ElectionReportArtifactKind.HumanOutcomeDetermination,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    5,
                    "Outcome determination",
                    "outcome-determination.md",
                    BuildHumanOutcomeContent(request.Election, outcomeProjection)),
            };

            var disputeCatalogEntries = artifacts
                .Select(x => new DisputeArtifactCatalogEntryProjection(
                    x.Id,
                    x.ArtifactKind.ToString(),
                    x.Format.ToString(),
                    x.AccessScope.ToString(),
                    x.Title,
                    x.FileName,
                    BuildHashHex(x.ContentHash),
                    x.PairedArtifactId))
                .OrderBy(x => x.ArtifactKind, StringComparer.Ordinal)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .ToArray();

            artifacts.Add(CreateJsonArtifact(
                request,
                packageId,
                machineDisputeId,
                humanDisputeId,
                ElectionReportArtifactKind.MachineDisputeReviewIndexProjection,
                ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                13,
                "Dispute review index projection",
                "dispute-review-index.json",
                new DisputeReviewIndexProjection(
                    machineDisputeId,
                    humanDisputeId,
                    request.Election.ElectionId.ToString(),
                    packageId,
                    disputeCatalogEntries)));
            artifacts.Add(CreateMarkdownArtifact(
                request,
                packageId,
                humanDisputeId,
                machineDisputeId,
                ElectionReportArtifactKind.HumanDisputeReviewIndex,
                ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                6,
                "Dispute review index",
                "dispute-review-index.md",
                BuildHumanDisputeIndexContent(
                    request.Election,
                    packageId,
                    disputeCatalogEntries)));

            var packageHash = ComputeHashBytes(string.Join(
                "\n",
                artifacts
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.ArtifactKind)
                    .Select(x => $"{x.SortOrder}|{x.ArtifactKind}|{x.Format}|{BuildHashHex(x.ContentHash)}")));

            var package = ElectionModelFactory.CreateSealedReportPackage(
                request.Election.ElectionId,
                request.AttemptNumber,
                request.TallyReadyArtifact.Id,
                request.UnofficialResult.Id,
                request.OfficialResult.Id,
                request.FinalizeArtifact.Id,
                frozenEvidenceHash,
                frozenEvidenceFingerprint,
                packageHash,
                artifacts.Count,
                request.AttemptedByPublicAddress,
                previousAttemptId: request.PreviousAttemptId,
                finalizationSessionId: request.FinalizationSession?.Id,
                closeBoundaryArtifactId: request.CloseArtifact.Id,
                closeEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
                finalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
                attemptedAt: request.AttemptedAt,
                sealedAt: request.AttemptedAt,
                preassignedPackageId: packageId);

            return ElectionReportPackageBuildResult.Success(package, artifacts);
        }
        catch (Exception ex)
        {
            return ElectionReportPackageBuildResult.Failure(CreateFailedAttempt(
                request,
                frozenEvidenceHash,
                frozenEvidenceFingerprint,
                "PACKAGE_BUILD_FAILED",
                ex.Message));
        }
    }

    private static ElectionReportPackageRecord CreateFailedAttempt(
        ElectionReportPackageBuildRequest request,
        byte[] frozenEvidenceHash,
        string frozenEvidenceFingerprint,
        string failureCode,
        string failureReason) =>
        ElectionModelFactory.CreateFailedReportPackageAttempt(
            request.Election.ElectionId,
            request.AttemptNumber,
            request.TallyReadyArtifact.Id,
            request.UnofficialResult.Id,
            frozenEvidenceHash,
            frozenEvidenceFingerprint,
            request.AttemptedByPublicAddress,
            failureCode,
            failureReason,
            previousAttemptId: request.PreviousAttemptId,
            finalizationSessionId: request.FinalizationSession?.Id,
            closeBoundaryArtifactId: request.CloseArtifact.Id,
            closeEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
            finalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
            attemptedAt: request.AttemptedAt);

    private static (string Code, string Reason)? ValidateConsistency(ElectionReportPackageBuildRequest request)
    {
        if (request.CloseArtifact.ArtifactType != ElectionBoundaryArtifactType.Close)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires the exact close boundary artifact.");
        }

        if (request.TallyReadyArtifact.ArtifactType != ElectionBoundaryArtifactType.TallyReady)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires the exact tally-ready boundary artifact.");
        }

        if (request.FinalizeArtifact.ArtifactType != ElectionBoundaryArtifactType.Finalize)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires the exact finalize boundary artifact.");
        }

        if (request.UnofficialResult.ArtifactKind != ElectionResultArtifactKind.Unofficial)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires an unofficial result source artifact.");
        }

        if (request.OfficialResult.ArtifactKind != ElectionResultArtifactKind.Official)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires an official result artifact.");
        }

        if (request.OfficialResult.SourceResultArtifactId != request.UnofficialResult.Id)
        {
            return ("CONSISTENCY_MISMATCH", "Official result lineage must point to the sealed unofficial result.");
        }

        if (!ByteArrayEquals(
                request.TallyReadyArtifact.AcceptedBallotSetHash,
                request.FinalizeArtifact.AcceptedBallotSetHash))
        {
            return ("CONSISTENCY_MISMATCH", "Finalize boundary accepted-ballot hash must match tally-ready evidence.");
        }

        if (!ByteArrayEquals(
                request.TallyReadyArtifact.FinalEncryptedTallyHash,
                request.FinalizeArtifact.FinalEncryptedTallyHash))
        {
            return ("CONSISTENCY_MISMATCH", "Finalize boundary tally hash must match tally-ready evidence.");
        }

        if (request.CloseEligibilitySnapshot is not null &&
            request.CloseEligibilitySnapshot.BoundaryArtifactId != request.CloseArtifact.Id)
        {
            return ("CONSISTENCY_MISMATCH", "Close eligibility snapshot must bind to the exact close artifact.");
        }

        return null;
    }

    private static FrozenEvidenceProjection BuildFrozenEvidenceProjection(ElectionReportPackageBuildRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.Election.Title,
            request.Election.OwnerPublicAddress,
            request.Election.GovernanceMode.ToString(),
            request.Election.OfficialResultVisibilityPolicy.ToString(),
            request.Election.OutcomeRule,
            new BoundaryEvidenceProjection(
                request.CloseArtifact.Id,
                request.CloseArtifact.ArtifactType.ToString(),
                request.CloseArtifact.RecordedAt,
                BuildHashHex(request.CloseArtifact.FrozenEligibleVoterSetHash),
                BuildHashHex(request.CloseArtifact.AcceptedBallotSetHash),
                BuildHashHex(request.CloseArtifact.PublishedBallotStreamHash),
                BuildHashHex(request.CloseArtifact.FinalEncryptedTallyHash),
                request.CloseArtifact.SourceTransactionId,
                request.CloseArtifact.SourceBlockHeight,
                request.CloseArtifact.SourceBlockId),
            request.CloseEligibilitySnapshot is null
                ? null
                : new EligibilitySnapshotProjection(
                    request.CloseEligibilitySnapshot.Id,
                    request.CloseEligibilitySnapshot.SnapshotType.ToString(),
                    request.CloseEligibilitySnapshot.RecordedAt,
                    request.CloseEligibilitySnapshot.RosteredCount,
                    request.CloseEligibilitySnapshot.LinkedCount,
                    request.CloseEligibilitySnapshot.ActiveDenominatorCount,
                    request.CloseEligibilitySnapshot.CountedParticipationCount,
                    request.CloseEligibilitySnapshot.BlankCount,
                    request.CloseEligibilitySnapshot.DidNotVoteCount,
                    BuildHashHex(request.CloseEligibilitySnapshot.RosteredVoterSetHash),
                    BuildHashHex(request.CloseEligibilitySnapshot.ActiveDenominatorSetHash),
                    BuildHashHex(request.CloseEligibilitySnapshot.CountedParticipationSetHash)),
            new BoundaryEvidenceProjection(
                request.TallyReadyArtifact.Id,
                request.TallyReadyArtifact.ArtifactType.ToString(),
                request.TallyReadyArtifact.RecordedAt,
                BuildHashHex(request.TallyReadyArtifact.FrozenEligibleVoterSetHash),
                BuildHashHex(request.TallyReadyArtifact.AcceptedBallotSetHash),
                BuildHashHex(request.TallyReadyArtifact.PublishedBallotStreamHash),
                BuildHashHex(request.TallyReadyArtifact.FinalEncryptedTallyHash),
                request.TallyReadyArtifact.SourceTransactionId,
                request.TallyReadyArtifact.SourceBlockHeight,
                request.TallyReadyArtifact.SourceBlockId),
            new ResultArtifactProjection(
                request.UnofficialResult.Id,
                request.UnofficialResult.ArtifactKind.ToString(),
                request.UnofficialResult.Visibility.ToString(),
                request.UnofficialResult.RecordedAt,
                request.UnofficialResult.NamedOptionResults.Select(x => new ResultOptionProjection(
                    x.OptionId,
                    x.DisplayLabel,
                    x.ShortDescription,
                    x.BallotOrder,
                    x.Rank,
                    x.VoteCount)).ToArray(),
                request.UnofficialResult.BlankCount,
                request.UnofficialResult.TotalVotedCount,
                request.UnofficialResult.EligibleToVoteCount,
                request.UnofficialResult.DidNotVoteCount,
                request.UnofficialResult.DenominatorEvidence.EligibilitySnapshotId,
                request.UnofficialResult.DenominatorEvidence.BoundaryArtifactId,
                BuildHashHex(request.UnofficialResult.DenominatorEvidence.ActiveDenominatorSetHash),
                request.UnofficialResult.SourceResultArtifactId),
            request.FinalizationSession is null
                ? null
                : new FinalizationSessionProjection(
                    request.FinalizationSession.Id,
                    request.FinalizationSession.SessionPurpose.ToString(),
                    request.FinalizationSession.Status.ToString(),
                    request.FinalizationSession.CloseArtifactId,
                    BuildHashHex(request.FinalizationSession.AcceptedBallotSetHash),
                    BuildHashHex(request.FinalizationSession.FinalEncryptedTallyHash),
                    request.FinalizationSession.TargetTallyId,
                    request.FinalizationSession.RequiredShareCount,
                    request.FinalizationSession.EligibleTrustees
                        .Select(x => new TrusteeProjection(
                            x.TrusteeUserAddress,
                            x.TrusteeDisplayName,
                            "Accepted",
                            null,
                            null))
                        .ToArray(),
                    request.FinalizationSession.CreatedAt,
                    request.FinalizationSession.CompletedAt,
                    request.FinalizationSession.ReleaseEvidenceId),
            request.FinalizationReleaseEvidence is null
                ? null
                : new FinalizationReleaseProjection(
                    request.FinalizationReleaseEvidence.Id,
                    request.FinalizationReleaseEvidence.FinalizationSessionId,
                    request.FinalizationReleaseEvidence.ReleaseMode.ToString(),
                    request.FinalizationReleaseEvidence.CloseArtifactId,
                    BuildHashHex(request.FinalizationReleaseEvidence.AcceptedBallotSetHash),
                    BuildHashHex(request.FinalizationReleaseEvidence.FinalEncryptedTallyHash),
                    request.FinalizationReleaseEvidence.TargetTallyId,
                    request.FinalizationReleaseEvidence.AcceptedShareCount,
                    request.FinalizationReleaseEvidence.CompletedAt,
                    request.FinalizationReleaseEvidence.AcceptedTrustees
                        .Select(x => new TrusteeProjection(
                            x.TrusteeUserAddress,
                            x.TrusteeDisplayName,
                            "Accepted",
                            null,
                            null))
                        .ToArray()));

    private static ManifestProjection BuildManifestProjection(
        ElectionReportPackageBuildRequest request,
        Guid packageId,
        string frozenEvidenceFingerprint,
        int acceptedTrusteeCount,
        int rosterEntryCount,
        OutcomeDeterminationProjection outcomeProjection) =>
        new(
            PackageId: packageId,
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            EvidenceGraphArtifactId: Guid.Empty,
            ElectionId: request.Election.ElectionId.ToString(),
            ElectionTitle: request.Election.Title,
            AttemptNumber: request.AttemptNumber,
            PreviousAttemptId: request.PreviousAttemptId,
            AttemptedAt: request.AttemptedAt,
            AttemptedBy: request.AttemptedByPublicAddress,
            FrozenEvidenceFingerprint: frozenEvidenceFingerprint,
            GovernanceMode: request.Election.GovernanceMode.ToString(),
            OfficialVisibility: request.Election.OfficialResultVisibilityPolicy.ToString(),
            AcceptedTrusteeCount: acceptedTrusteeCount,
            RosterEntryCount: rosterEntryCount,
            FinalizeArtifactId: request.FinalizeArtifact.Id,
            OfficialResultArtifactId: request.OfficialResult.Id,
            OutcomeLabel: outcomeProjection.ConclusionLabel,
            OutcomeSummary: outcomeProjection.ConclusionSummary);

    private static EvidenceGraphProjection BuildEvidenceGraphProjection(
        ElectionReportPackageBuildRequest request,
        IReadOnlyList<TrusteeProjection> trustees,
        int rosterEntryCount) =>
        new(
            ArtifactId: Guid.Empty,
            ManifestArtifactId: Guid.Empty,
            ElectionId: request.Election.ElectionId.ToString(),
            CloseArtifactId: request.CloseArtifact.Id,
            CloseEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
            TallyReadyArtifactId: request.TallyReadyArtifact.Id,
            UnofficialResultArtifactId: request.UnofficialResult.Id,
            OfficialResultArtifactId: request.OfficialResult.Id,
            FinalizeArtifactId: request.FinalizeArtifact.Id,
            FinalizationSessionId: request.FinalizationSession?.Id,
            FinalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
            AcceptedBallotSetHash: BuildHashHex(request.TallyReadyArtifact.AcceptedBallotSetHash),
            PublishedBallotStreamHash: BuildHashHex(request.TallyReadyArtifact.PublishedBallotStreamHash),
            FinalEncryptedTallyHash: BuildHashHex(request.TallyReadyArtifact.FinalEncryptedTallyHash),
            ActiveDenominatorSetHash: BuildHashHex(request.CloseEligibilitySnapshot?.ActiveDenominatorSetHash),
            RosterEntryCount: rosterEntryCount,
            Trustees: trustees);

    private static ResultReportProjection BuildResultReportProjection(
        ElectionRecord election,
        ElectionResultArtifactRecord officialResult,
        OutcomeDeterminationProjection outcomeProjection)
    {
        var eligibleCount = Math.Max(officialResult.EligibleToVoteCount, 0);
        var turnoutPercent = eligibleCount == 0
            ? 0m
            : decimal.Round((officialResult.TotalVotedCount * 100m) / eligibleCount, 2, MidpointRounding.AwayFromZero);

        var optionResults = officialResult.NamedOptionResults
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.BallotOrder)
            .Select(x =>
            {
                var voteShare = officialResult.TotalVotedCount == 0
                    ? 0m
                    : decimal.Round((x.VoteCount * 100m) / officialResult.TotalVotedCount, 2, MidpointRounding.AwayFromZero);
                return new ResultOptionShareProjection(
                    x.OptionId,
                    x.DisplayLabel,
                    x.ShortDescription,
                    x.Rank,
                    x.VoteCount,
                    voteShare);
            })
            .ToArray();

        return new ResultReportProjection(
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            ElectionId: election.ElectionId.ToString(),
            ElectionTitle: election.Title,
            OfficialResultArtifactId: officialResult.Id,
            Visibility: officialResult.Visibility.ToString(),
            TotalVotedCount: officialResult.TotalVotedCount,
            EligibleToVoteCount: officialResult.EligibleToVoteCount,
            DidNotVoteCount: officialResult.DidNotVoteCount,
            BlankCount: officialResult.BlankCount,
            TurnoutPercent: turnoutPercent,
            DenominatorSnapshotId: officialResult.DenominatorEvidence.EligibilitySnapshotId,
            DenominatorBoundaryArtifactId: officialResult.DenominatorEvidence.BoundaryArtifactId,
            DenominatorHash: BuildHashHex(officialResult.DenominatorEvidence.ActiveDenominatorSetHash),
            OutcomeLabel: outcomeProjection.ConclusionLabel,
            OutcomeSummary: outcomeProjection.ConclusionSummary,
            OptionResults: optionResults);
    }

    private static AuditProvenanceProjection BuildAuditProjection(
        ElectionReportPackageBuildRequest request,
        string frozenEvidenceFingerprint,
        IReadOnlyList<TrusteeProjection> trustees) =>
        new(
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            ElectionId: request.Election.ElectionId.ToString(),
            FrozenEvidenceFingerprint: frozenEvidenceFingerprint,
            CloseArtifactId: request.CloseArtifact.Id,
            TallyReadyArtifactId: request.TallyReadyArtifact.Id,
            UnofficialResultArtifactId: request.UnofficialResult.Id,
            OfficialResultArtifactId: request.OfficialResult.Id,
            FinalizeArtifactId: request.FinalizeArtifact.Id,
            FinalizationSessionId: request.FinalizationSession?.Id,
            FinalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
            AcceptedBallotSetHash: BuildHashHex(request.TallyReadyArtifact.AcceptedBallotSetHash),
            PublishedBallotStreamHash: BuildHashHex(request.TallyReadyArtifact.PublishedBallotStreamHash),
            FinalEncryptedTallyHash: BuildHashHex(request.TallyReadyArtifact.FinalEncryptedTallyHash),
            DenominatorHash: BuildHashHex(request.CloseEligibilitySnapshot?.ActiveDenominatorSetHash),
            Trustees: trustees,
            SourceTransactionId: request.FinalizeArtifact.SourceTransactionId,
            SourceBlockHeight: request.FinalizeArtifact.SourceBlockHeight,
            SourceBlockId: request.FinalizeArtifact.SourceBlockId);

    private static OutcomeDeterminationProjection BuildOutcomeProjection(
        ElectionRecord election,
        ElectionResultArtifactRecord officialResult,
        ElectionEligibilitySnapshotRecord? closeEligibilitySnapshot)
    {
        var topResults = officialResult.NamedOptionResults
            .OrderByDescending(x => x.VoteCount)
            .ThenBy(x => x.BallotOrder)
            .ToArray();
        var leader = topResults.FirstOrDefault();
        var tie = leader is not null &&
            topResults.Count(x => x.VoteCount == leader.VoteCount) > 1;
        var eligibleCount = closeEligibilitySnapshot?.ActiveDenominatorCount ?? officialResult.EligibleToVoteCount;
        var turnoutPercent = eligibleCount <= 0
            ? 0m
            : decimal.Round((officialResult.TotalVotedCount * 100m) / eligibleCount, 2, MidpointRounding.AwayFromZero);

        string conclusionLabel;
        string conclusionSummary;
        string? decisiveOptionId = null;
        string? decisiveOptionLabel = null;

        if (tie || leader is null)
        {
            conclusionLabel = election.OutcomeRule.Kind == OutcomeRuleKind.PassFail
                ? "Tie / unresolved"
                : "Winner unresolved";
            conclusionSummary = "The platform outcome is unresolved because the top counted options are tied.";
        }
        else if (election.OutcomeRule.Kind == OutcomeRuleKind.PassFail)
        {
            decisiveOptionId = leader.OptionId;
            decisiveOptionLabel = leader.DisplayLabel;
            var firstNamedBallotOrder = topResults.Min(x => x.BallotOrder);
            var isPass = string.Equals(election.OutcomeRule.TemplateKey, "pass_fail_yes_no", StringComparison.OrdinalIgnoreCase)
                ? leader.BallotOrder == firstNamedBallotOrder
                : StartsWithAny(leader.DisplayLabel, "yes", "approve", "approved", "pass");
            conclusionLabel = isPass ? "Pass" : "Fail";
            conclusionSummary = $"{conclusionLabel} based on {election.OutcomeRule.CalculationBasis} with decisive option '{leader.DisplayLabel}'.";
        }
        else
        {
            decisiveOptionId = leader.OptionId;
            decisiveOptionLabel = leader.DisplayLabel;
            conclusionLabel = "Winner";
            conclusionSummary = $"Winner '{leader.DisplayLabel}' based on {election.OutcomeRule.CalculationBasis}.";
        }

        return new OutcomeDeterminationProjection(
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            ElectionId: election.ElectionId.ToString(),
            OutcomeRuleKind: election.OutcomeRule.Kind.ToString(),
            OutcomeTemplateKey: election.OutcomeRule.TemplateKey,
            CalculationBasis: election.OutcomeRule.CalculationBasis,
            TieResolutionRule: election.OutcomeRule.TieResolutionRule,
            ConclusionLabel: conclusionLabel,
            ConclusionSummary: conclusionSummary,
            DecisiveOptionId: decisiveOptionId,
            DecisiveOptionLabel: decisiveOptionLabel,
            TotalVotedCount: officialResult.TotalVotedCount,
            EligibleToVoteCount: officialResult.EligibleToVoteCount,
            TurnoutPercent: turnoutPercent,
            BlankCount: officialResult.BlankCount,
            DidNotVoteCount: officialResult.DidNotVoteCount);
    }

    private static RosterEntryProjection BuildRosterEntryProjection(
        ElectionRosterEntryRecord rosterEntry,
        ElectionParticipationRecord? participationRecord) =>
        new(
            rosterEntry.OrganizationVoterId,
            rosterEntry.LinkStatus.ToString(),
            rosterEntry.VotingRightStatus.ToString(),
            rosterEntry.LinkedActorPublicAddress,
            rosterEntry.WasPresentAtOpen,
            rosterEntry.WasActiveAtOpen,
            (participationRecord?.ParticipationStatus ?? ElectionParticipationStatus.DidNotVote).ToString(),
            participationRecord?.CountsAsParticipation ?? false);

    private static ElectionReportArtifactRecord CreateJsonArtifact(
        ElectionReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        Guid? pairedArtifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        object payload)
    {
        var content = SerializeJson(payload);
        return ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Json,
            accessScope,
            sortOrder,
            title,
            fileName,
            "application/json",
            ComputeHashBytes(content),
            content,
            pairedArtifactId,
            request.AttemptedAt,
            artifactId);
    }

    private static ElectionReportArtifactRecord CreateMarkdownArtifact(
        ElectionReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        Guid? pairedArtifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        string content) =>
        ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Markdown,
            accessScope,
            sortOrder,
            title,
            fileName,
            "text/markdown",
            ComputeHashBytes(content),
            content,
            pairedArtifactId,
            request.AttemptedAt,
            artifactId);

    private static string BuildHumanManifestContent(
        ManifestProjection manifest,
        Guid packageId,
        string frozenEvidenceFingerprint,
        Guid machineManifestId,
        Guid humanManifestId,
        Guid evidenceGraphArtifactId) =>
        $"""
        # Final Manifest

        - Package id: `{packageId}`
        - Attempt number: `{manifest.AttemptNumber}`
        - Previous attempt id: `{manifest.PreviousAttemptId?.ToString() ?? "none"}`
        - Election id: `{manifest.ElectionId}`
        - Election title: {manifest.ElectionTitle}
        - Attempted at: `{manifest.AttemptedAt:O}`
        - Attempted by: `{manifest.AttemptedBy}`
        - Frozen evidence fingerprint: `{frozenEvidenceFingerprint}`
        - Governance mode: `{manifest.GovernanceMode}`
        - Official visibility: `{manifest.OfficialVisibility}`
        - Accepted trustee count: `{manifest.AcceptedTrusteeCount}`
        - Roster entry count: `{manifest.RosterEntryCount}`
        - Outcome label: `{manifest.OutcomeLabel}`
        - Outcome summary: {manifest.OutcomeSummary}
        - Machine manifest artifact id: `{machineManifestId}`
        - Human manifest artifact id: `{humanManifestId}`
        - Evidence graph artifact id: `{evidenceGraphArtifactId}`
        - Finalize artifact id: `{manifest.FinalizeArtifactId}`
        - Official result artifact id: `{manifest.OfficialResultArtifactId}`
        """;

    private static string BuildHumanResultReportContent(ResultReportProjection projection)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Final Result Report");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{projection.ElectionId}`");
        builder.AppendLine($"- Election title: {projection.ElectionTitle}");
        builder.AppendLine($"- Official result artifact id: `{projection.OfficialResultArtifactId}`");
        builder.AppendLine($"- Visibility: `{projection.Visibility}`");
        builder.AppendLine($"- Total voted: `{projection.TotalVotedCount}`");
        builder.AppendLine($"- Eligible to vote: `{projection.EligibleToVoteCount}`");
        builder.AppendLine($"- Did not vote: `{projection.DidNotVoteCount}`");
        builder.AppendLine($"- Blank votes: `{projection.BlankCount}`");
        builder.AppendLine($"- Turnout percent: `{projection.TurnoutPercent:F2}`");
        builder.AppendLine($"- Outcome label: `{projection.OutcomeLabel}`");
        builder.AppendLine($"- Outcome summary: {projection.OutcomeSummary}");
        builder.AppendLine();
        builder.AppendLine("| Rank | Option | Votes | Share |");
        builder.AppendLine("|------|--------|-------|-------|");
        foreach (var option in projection.OptionResults)
        {
            builder.AppendLine($"| {option.Rank} | {option.DisplayLabel} | {option.VoteCount} | {option.VoteSharePercent:F2}% |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildHumanRosterContent(
        ElectionRecord election,
        IReadOnlyList<RosterEntryProjection> rosterEntries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Named Participation Roster");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{election.ElectionId}`");
        builder.AppendLine($"- Election title: {election.Title}");
        builder.AppendLine($"- Roster entries: `{rosterEntries.Count}`");
        builder.AppendLine();
        builder.AppendLine("| Organization voter id | Link status | Voting right | Linked actor | Participation | Counts as participation |");
        builder.AppendLine("|-----------------------|-------------|--------------|--------------|---------------|-------------------------|");
        foreach (var entry in rosterEntries)
        {
            builder.AppendLine(
                $"| {entry.OrganizationVoterId} | {entry.LinkStatus} | {entry.VotingRightStatus} | {(entry.LinkedActorPublicAddress ?? "unlinked")} | {entry.ParticipationStatus} | {(entry.CountsAsParticipation ? "yes" : "no")} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildHumanAuditContent(AuditProvenanceProjection projection) =>
        $"""
        # Audit And Provenance Report

        - Election id: `{projection.ElectionId}`
        - Frozen evidence fingerprint: `{projection.FrozenEvidenceFingerprint}`
        - Close artifact id: `{projection.CloseArtifactId}`
        - Tally-ready artifact id: `{projection.TallyReadyArtifactId}`
        - Unofficial result artifact id: `{projection.UnofficialResultArtifactId}`
        - Official result artifact id: `{projection.OfficialResultArtifactId}`
        - Finalize artifact id: `{projection.FinalizeArtifactId}`
        - Finalization session id: `{projection.FinalizationSessionId?.ToString() ?? "none"}`
        - Finalization release evidence id: `{projection.FinalizationReleaseEvidenceId?.ToString() ?? "none"}`
        - Accepted ballot set hash: `{projection.AcceptedBallotSetHash}`
        - Published ballot stream hash: `{projection.PublishedBallotStreamHash}`
        - Final encrypted tally hash: `{projection.FinalEncryptedTallyHash}`
        - Denominator hash: `{projection.DenominatorHash}`
        - Source transaction id: `{projection.SourceTransactionId?.ToString() ?? "none"}`
        - Source block height: `{projection.SourceBlockHeight?.ToString() ?? "none"}`
        - Source block id: `{projection.SourceBlockId?.ToString() ?? "none"}`

        ## Accepted Trustees

        {string.Join(Environment.NewLine, projection.Trustees.Select(x =>
            $"- `{x.TrusteeUserAddress}` ({x.TrusteeDisplayName ?? "unnamed"})"))}
        """;

    private static string BuildHumanOutcomeContent(
        ElectionRecord election,
        OutcomeDeterminationProjection projection) =>
        $"""
        # Outcome Determination

        - Election id: `{projection.ElectionId}`
        - Outcome rule kind: `{projection.OutcomeRuleKind}`
        - Template key: `{projection.OutcomeTemplateKey}`
        - Calculation basis: `{projection.CalculationBasis}`
        - Tie resolution rule: `{projection.TieResolutionRule}`
        - Platform conclusion: `{projection.ConclusionLabel}`
        - Conclusion summary: {projection.ConclusionSummary}
        - Decisive option id: `{projection.DecisiveOptionId ?? "none"}`
        - Decisive option label: `{projection.DecisiveOptionLabel ?? "none"}`
        - Total voted: `{projection.TotalVotedCount}`
        - Eligible to vote: `{projection.EligibleToVoteCount}`
        - Blank votes: `{projection.BlankCount}`
        - Did not vote: `{projection.DidNotVoteCount}`
        - Turnout percent: `{projection.TurnoutPercent:F2}`

        The platform conclusion above is derived from frozen FEAT-101 official counts and the frozen
        FEAT-094 outcome rule for `{election.Title}`.
        """;

    private static string BuildHumanDisputeIndexContent(
        ElectionRecord election,
        Guid packageId,
        IReadOnlyList<DisputeArtifactCatalogEntryProjection> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Dispute Review Index");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{election.ElectionId}`");
        builder.AppendLine($"- Package id: `{packageId}`");
        builder.AppendLine($"- Catalog entries: `{entries.Count}`");
        builder.AppendLine();
        builder.AppendLine("| Title | Kind | Format | Scope | Artifact id | Hash | Paired artifact id |");
        builder.AppendLine("|-------|------|--------|-------|-------------|------|--------------------|");
        foreach (var entry in entries)
        {
            builder.AppendLine(
                $"| {entry.Title} | {entry.ArtifactKind} | {entry.Format} | {entry.AccessScope} | `{entry.ArtifactId}` | `{entry.ContentHash}` | `{entry.PairedArtifactId?.ToString() ?? "none"}` |");
        }

        return builder.ToString().TrimEnd();
    }

    private static byte[] ComputeHashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string SerializeJson(object payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    private static string BuildHashHex(byte[]? value) =>
        value is { Length: > 0 }
            ? Convert.ToHexString(value).ToLowerInvariant()
            : string.Empty;

    private static bool ByteArrayEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

    private sealed record FrozenEvidenceProjection(
        string ElectionId,
        string ElectionTitle,
        string OwnerPublicAddress,
        string GovernanceMode,
        string OfficialVisibility,
        OutcomeRuleDefinition OutcomeRule,
        BoundaryEvidenceProjection CloseBoundary,
        EligibilitySnapshotProjection? CloseEligibilitySnapshot,
        BoundaryEvidenceProjection TallyReadyBoundary,
        ResultArtifactProjection UnofficialResult,
        FinalizationSessionProjection? FinalizationSession,
        FinalizationReleaseProjection? FinalizationReleaseEvidence);

    private sealed record BoundaryEvidenceProjection(
        Guid Id,
        string ArtifactType,
        DateTime RecordedAt,
        string FrozenEligibleVoterSetHash,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        string FinalEncryptedTallyHash,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record EligibilitySnapshotProjection(
        Guid Id,
        string SnapshotType,
        DateTime RecordedAt,
        int RosteredCount,
        int LinkedCount,
        int ActiveDenominatorCount,
        int CountedParticipationCount,
        int BlankCount,
        int DidNotVoteCount,
        string RosteredSetHash,
        string ActiveDenominatorSetHash,
        string CountedParticipationSetHash);

    private sealed record ResultArtifactProjection(
        Guid Id,
        string ArtifactKind,
        string Visibility,
        DateTime RecordedAt,
        IReadOnlyList<ResultOptionProjection> NamedOptionResults,
        int BlankCount,
        int TotalVotedCount,
        int EligibleToVoteCount,
        int DidNotVoteCount,
        Guid? DenominatorSnapshotId,
        Guid? DenominatorBoundaryArtifactId,
        string DenominatorHash,
        Guid? SourceResultArtifactId);

    private sealed record ResultOptionProjection(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int BallotOrder,
        int Rank,
        int VoteCount);

    private sealed record FinalizationSessionProjection(
        Guid Id,
        string SessionPurpose,
        string Status,
        Guid CloseArtifactId,
        string AcceptedBallotSetHash,
        string FinalEncryptedTallyHash,
        string TargetTallyId,
        int RequiredShareCount,
        IReadOnlyList<TrusteeProjection> EligibleTrustees,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        Guid? ReleaseEvidenceId);

    private sealed record FinalizationReleaseProjection(
        Guid Id,
        Guid FinalizationSessionId,
        string ReleaseMode,
        Guid CloseArtifactId,
        string AcceptedBallotSetHash,
        string FinalEncryptedTallyHash,
        string TargetTallyId,
        int AcceptedShareCount,
        DateTime CompletedAt,
        IReadOnlyList<TrusteeProjection> AcceptedTrustees);

    private sealed record TrusteeProjection(
        string TrusteeUserAddress,
        string? TrusteeDisplayName,
        string Status,
        DateTime? SentAt,
        DateTime? RespondedAt);

    private sealed record ManifestProjection(
        Guid PackageId,
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        Guid EvidenceGraphArtifactId,
        string ElectionId,
        string ElectionTitle,
        int AttemptNumber,
        Guid? PreviousAttemptId,
        DateTime AttemptedAt,
        string AttemptedBy,
        string FrozenEvidenceFingerprint,
        string GovernanceMode,
        string OfficialVisibility,
        int AcceptedTrusteeCount,
        int RosterEntryCount,
        Guid FinalizeArtifactId,
        Guid OfficialResultArtifactId,
        string OutcomeLabel,
        string OutcomeSummary);

    private sealed record EvidenceGraphProjection(
        Guid ArtifactId,
        Guid ManifestArtifactId,
        string ElectionId,
        Guid CloseArtifactId,
        Guid? CloseEligibilitySnapshotId,
        Guid TallyReadyArtifactId,
        Guid UnofficialResultArtifactId,
        Guid OfficialResultArtifactId,
        Guid FinalizeArtifactId,
        Guid? FinalizationSessionId,
        Guid? FinalizationReleaseEvidenceId,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        string FinalEncryptedTallyHash,
        string ActiveDenominatorSetHash,
        int RosterEntryCount,
        IReadOnlyList<TrusteeProjection> Trustees);

    private sealed record ResultReportProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string ElectionTitle,
        Guid OfficialResultArtifactId,
        string Visibility,
        int TotalVotedCount,
        int EligibleToVoteCount,
        int DidNotVoteCount,
        int BlankCount,
        decimal TurnoutPercent,
        Guid? DenominatorSnapshotId,
        Guid? DenominatorBoundaryArtifactId,
        string DenominatorHash,
        string OutcomeLabel,
        string OutcomeSummary,
        IReadOnlyList<ResultOptionShareProjection> OptionResults);

    private sealed record ResultOptionShareProjection(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int Rank,
        int VoteCount,
        decimal VoteSharePercent);

    private sealed record RosterProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        int EntryCount,
        IReadOnlyList<RosterEntryProjection> Entries);

    private sealed record RosterEntryProjection(
        string OrganizationVoterId,
        string LinkStatus,
        string VotingRightStatus,
        string? LinkedActorPublicAddress,
        bool WasPresentAtOpen,
        bool WasActiveAtOpen,
        string ParticipationStatus,
        bool CountsAsParticipation);

    private sealed record AuditProvenanceProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string FrozenEvidenceFingerprint,
        Guid CloseArtifactId,
        Guid TallyReadyArtifactId,
        Guid UnofficialResultArtifactId,
        Guid OfficialResultArtifactId,
        Guid FinalizeArtifactId,
        Guid? FinalizationSessionId,
        Guid? FinalizationReleaseEvidenceId,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        string FinalEncryptedTallyHash,
        string DenominatorHash,
        IReadOnlyList<TrusteeProjection> Trustees,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record OutcomeDeterminationProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string OutcomeRuleKind,
        string OutcomeTemplateKey,
        string CalculationBasis,
        string TieResolutionRule,
        string ConclusionLabel,
        string ConclusionSummary,
        string? DecisiveOptionId,
        string? DecisiveOptionLabel,
        int TotalVotedCount,
        int EligibleToVoteCount,
        decimal TurnoutPercent,
        int BlankCount,
        int DidNotVoteCount);

    private sealed record DisputeReviewIndexProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        Guid PackageId,
        IReadOnlyList<DisputeArtifactCatalogEntryProjection> Entries);

    private sealed record DisputeArtifactCatalogEntryProjection(
        Guid ArtifactId,
        string ArtifactKind,
        string Format,
        string AccessScope,
        string Title,
        string FileName,
        string ContentHash,
        Guid? PairedArtifactId);
}

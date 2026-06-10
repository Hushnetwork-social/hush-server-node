using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ReadinessRegisterPromoter;

public sealed record ReadinessRegisterPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string OutputRoot)
{
    public string SchemaPath => Path.Combine(SourceRoot, "readiness-register.schema.json");
    public string RegisterPath => Path.Combine(SourceRoot, "readiness-register.json");
    public string ExamplePath => Path.Combine(SourceRoot, "readiness-register.example.json");
    public string CatalogPath => Path.Combine(OutputRoot, "readiness-register-catalog.json");

    public static ReadinessRegisterPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        var root = Path.GetFullPath(workspaceRoot);
        return new ReadinessRegisterPromotionPaths(
            root,
            Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness", "Readiness-Register"),
            Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "HushVoting-Readiness-Register"));
    }
}

public sealed record ReadinessRegisterPromotionOptions(
    ReadinessRegisterPromotionPaths Paths,
    string RegisterId,
    string? Version,
    string? PublicationStatus,
    bool ValidateOnly,
    bool Scaffold,
    DateTimeOffset? GeneratedAt,
    bool CheckOnly = false);

public sealed record ReadinessRegisterPromotionResult(
    string RegisterVersion,
    string RegisterVersionId,
    string Status,
    DateTimeOffset GeneratedAt,
    int TotalScore,
    string StrongestAllowedClaim,
    string PublicationStatus,
    string ManifestHash,
    string ArchiveHash,
    string CatalogPath,
    string VersionOutputRoot,
    IReadOnlyList<string> WrittenFiles);

public sealed class ReadinessRegisterPromotionException(
    string message,
    IReadOnlyList<string> details) : InvalidOperationException(message)
{
    public IReadOnlyList<string> Details { get; } = details;
}

public sealed class ReadinessRegisterPromotionService
{
    public const string SchemaFileName = "readiness-register.schema.json";
    public const string RegisterFileName = "readiness-register.json";
    public const string ExampleFileName = "readiness-register.example.json";
    public const string ScorecardFileName = "readiness-scorecard.md";
    public const string RestrictedReviewerExtractFileName = "restricted-reviewer-extract.md";
    public const string PublicSafeSummaryFileName = "public-safe-summary.md";
    public const string ManifestFileName = "readiness-register-manifest.json";
    public const string CatalogFileName = "readiness-register-catalog.json";
    public const string ArchivePrefix = "HushVoting-Readiness-Register";
    public const string ReadinessCheckPagesDirectory = "readiness-checks";
    public const string ExternalAuditorEntryPointFileName = "external-auditor-entry-point.md";

    private const string Feat156TargetVersion = "v0.1.6";
    private const string Feat156TargetPublicationStatus = "production_rollout_with_limitations";
    private const string Feat156PromotionSourceFileName = "production-rollout-promotion-source.json";
    private const string InternalAudit95FinalTargetVersion = "v0.1.8";
    private const string InternalAudit95FinalTargetPublicationStatus = "pilot_only_with_limitations";
    private const string InternalAudit95PromotionSourceFileName = "internal-audit-95-promotion-source.json";
    private const string DevelopmentProfileClarificationTargetVersion = "v0.1.9";
    private const string DevelopmentProfileClarificationPublicationStatus = "pilot_only_with_limitations";
    private const string DevelopmentProfileClarificationSourceId = "RDY-REG-v0.1.9-development-profile-clarification";

    private static readonly Regex VersionPattern = new("^v[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex RegisterVersionIdPattern = new("^RDY-REG-v[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex HexSha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled);
    private static readonly Regex EvidenceIdPattern = new("^RDY-EVID-AT-RDY-[0-9]{3}-FEAT-[0-9]{3}-[0-9]{3}$", RegexOptions.Compiled);
    private static readonly Regex BlockerIdPattern = new("^RDY-BLOCK-[A-Z0-9_]+-[0-9]{3}$", RegexOptions.Compiled);
    private static readonly Regex ScoreChangeIdPattern = new("^RDY-SCORE-[0-9]{8}-[0-9]{3}$", RegexOptions.Compiled);
    private static readonly Regex ExceptionIdPattern = new("^RDY-EXC-[0-9]{8}-[0-9]{3}$", RegexOptions.Compiled);

    private static readonly DateTimeOffset FixedZipTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    private static readonly JsonSerializerOptions ReadableJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        "DraftInternal",
        "AcceptedInternal",
        "ReviewerReady",
        "Superseded",
        "Blocked",
    };

    private static readonly string[] DimensionIds =
    [
        "RDY-DIM-001",
        "RDY-DIM-002",
        "RDY-DIM-003",
        "RDY-DIM-004",
        "RDY-DIM-005",
        "RDY-DIM-006",
        "RDY-DIM-007",
        "RDY-DIM-008",
        "RDY-DIM-009",
        "RDY-DIM-010",
    ];

    private static readonly string[] ClaimLevels =
    [
        "internal_development",
        "internal_non_binding_rehearsal",
        "friendly_organization_pilot",
        "production_organizational_rollout",
        "public_or_state_election",
    ];

    private static readonly string[] ClaimProfileIds =
    [
        "hushvoting.direct.non_binding",
        "hushvoting.direct.binding",
        "hushvoting.veritas_3_of_5.non_binding",
        "hushvoting.veritas_3_of_5.binding",
        "hushvoting.veritas_7_of_10.non_binding",
        "hushvoting.veritas_7_of_10.binding",
        "hushvoting.veritas_8_of_13.non_binding",
        "hushvoting.veritas_8_of_13.binding",
        "hushvoting.enterprise_n_of_k.non_binding",
        "hushvoting.enterprise_n_of_k.binding",
    ];

    private const string DirectNonBindingCurrentVerifierOutputRef =
        "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Non-Binding-20260605102141/public-verifier-output-current-public-20260605c/VerifierOutput.json";

    private const string DirectBindingCurrentVerifierOutputRef =
        "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Rehearsal-II-20260604215137/public-verifier-output-current-public-20260605c/VerifierOutput.json";

    private static readonly OperationalChecklistRow[] EnvironmentOperationalChecklistRows =
    [
        new(
            "Development",
            "Machine",
            "Required",
            "Verify product mode, bindingStatus, isNonBindingElection, package manifest/hash integrity, verifier profile, development release binding, and public/restricted privacy boundary.",
            "Direct profile evidence refs; VFY-*; SP-08 development proof; OPS-000, OPS-001, OPS-003, OPS-004, OPS-005, OPS-007",
            "Can pass internal technical Direct claim profiles only."),
        new(
            "Development",
            "Human",
            "Not required",
            "No real privileged-access attempt, backup/restore rehearsal, or auditor-room access trial is required for the local development profile.",
            "None",
            "No production, customer, public/state, legal, certification, or rollout claim."),
        new(
            "PreProduction",
            "Machine",
            "Optional full candidate",
            "When a PreProduction environment exists, verify SP-10 refs/hashes, package-manifest entries, restricted refs, timestamps, release/deployment binding, migration package hash, and immutable candidate identity.",
            "OPS-002, OPS-006, OPS-008 plus SP-08 release/deployment refs",
            "Can accept a production candidate before activation if the same immutable candidate is later promoted."),
        new(
            "PreProduction",
            "Human",
            "Optional full candidate or attested",
            "Authorized reviewer records direct observations or attaches a signed tester/auditor document that states the PreProduction human checks were completed.",
            "Access-control review, restore-test review, auditor-room review/signoff, or signed external attestation",
            "Feeds the candidate promotion policy; the editable checklist itself does not directly block a claim."),
        new(
            "Production Direct",
            "Machine",
            "Required when no PreProduction candidate exists",
            "Run the full machine readiness workflow in Production: immutable deployment refs, signed restricted evidence refs, fresh access snapshot, fresh backup/restore evidence, auditor-room access-log binding, and migration/package evidence.",
            "Full production machine checklist including OPS-002, OPS-006, OPS-008",
            "Required when deploying directly to Production."),
        new(
            "Production Direct",
            "Human",
            "Required or attested when no PreProduction candidate exists",
            "Operational/security reviewer records observations or attaches a signed tester/auditor document covering access rejection tests, backup/restore evidence, auditor-room controls, exceptions, and residual risk.",
            "Human checklist signoff, external attestation, and restricted reviewer extract",
            "Feeds the production promotion policy; the editable checklist itself does not directly block a claim."),
        new(
            "Production Activation",
            "Machine",
            "Required when promoting an accepted PreProduction candidate",
            "Verify the production activation uses the same accepted release manifest, container/build digest, protocol package, migration package, and approved environment-delta refs.",
            "production-activation-addendum.json; release/deployment/migration/config delta proof",
            "Allows Production activation without repeating the full readiness report when the candidate is unchanged."),
        new(
            "Production Activation",
            "Human",
            "Required signoff",
            "Activation signer confirms the traffic switch, rollback/backup readiness, and approved environment deltas for the accepted candidate.",
            "Activation signoff and restricted activation notes",
            "Feeds the promotion policy; the editable checklist itself does not directly block a claim."),
    ];

    private static readonly ProfileEvidenceProcedureRow[] ProfileEvidenceProcedureRows =
    [
        new(
            "RDY-CHECK-DIRECT-PROFILE-GATE",
            "direct-profile-gate.md",
            "Direct profile gate",
            "Check productMode, selectedProfileId, SelectedProfileDevOnly, bindingStatus, isNonBindingElection, governanceMode, circuitClassification, and ContactCodeProviderReadiness in the canonical manifest and result report.",
            "ElectionRecord, ElectionBoundaryArtifactRecord, ElectionReportPackageRecord, ElectionReportArtifactRecord",
            "canonical-manifest.json; result-report.json; audit-boundary-note.md",
            "Direct Non-Binding passes only when bindingStatus is Non-Binding, isNonBindingElection is true, and any DevOnly provider/profile flags are explicitly scoped to development/rehearsal. Direct Binding passes only when bindingStatus is Binding, isNonBindingElection is false, and production/profile readiness flags are not hidden.",
            "Required",
            "Verify the Direct profile tuple from canonical-manifest.json, result-report.json, and audit-boundary-note.md for both binding and non-binding runs. Treat DevOnly as the persisted development/rehearsal provider/profile flag, not as a production readiness claim.",
            "Required for any candidate profile promoted from Development.",
            "Required for every production election profile before stronger claims.",
            "Non-binding checked at report package attempt 2026-06-05T10:21:41.7215447Z; binding checked at report package attempt 2026-06-04T21:32:52.7016107Z.",
            "Passed for RDY-REG-v0.1.8 Direct: NonBinding/isNonBindingElection=true/admin-dev-1of1 and Binding/isNonBindingElection=false/admin-prod-1of1. RDY-REG-v0.1.9 clarifies that DevOnly contact/profile flags are accepted only inside the development/rehearsal boundary.",
            "Use the report package attemptedAt timestamp and verifier verifiedAt timestamp for the election being promoted."),
        new(
            "RDY-CHECK-PROTOCOL-OMEGA-BINDING",
            "protocol-omega-binding.md",
            "Protocol Omega binding",
            "Check the sealed Protocol Omega package id, version, spec hash, proof hash, release-manifest hash, approval status, external-review status, draft revision, source transaction, block height, and access-location content hashes.",
            "ProtocolPackageBindingRecord; ApprovedProtocolPackageCatalogEntryRecord; ElectionRecord",
            "ElectionRecord.json; canonical-manifest.json protocolPackageBinding; evidence-graph.json protocolPackageBinding",
            "The binding must be status Sealed, source SealedAtOpen, and the hashes must match the election record and public package locations. NotReviewed external-review status is acceptable only for development/rehearsal verifier profiles; production or external-review claims require imported reviewer evidence and an updated SP-09/catalog status.",
            "Required",
            "Verify the sealed Protocol Omega binding at open, including source transaction/block refs and hash equality across ElectionRecord.json, canonical-manifest.json, and evidence-graph.json. Verify that NotReviewed is presented as a development/rehearsal non-claim, not as completed external review.",
            "Required with the production candidate's approved package and immutable access-location hashes.",
            "Required with the exact package id/version/hash used for creation, open, close/count, and finalize, plus reviewed SP-09 evidence when making production or external-review claims.",
            "Non-binding sealed at 2026-06-05T10:19:23.846873Z; binding sealed at 2026-06-04T21:27:55.828125Z.",
            "Passed for development/rehearsal protocol binding: both Direct runs use omega-hushvoting-v1/v1.2.1 with matching spec, proof, and release-manifest hashes. External reviewer conclusion remains a separate SP-09 non-claim until reviewer evidence is imported.",
            "Use protocolPackageBinding.boundAt/sealedAt plus the source transaction/block fields for the promoted election."),
        new(
            "RDY-CHECK-CIRCUIT-BALLOT-TALLY-BINDING",
            "circuit-ballot-tally-binding.md",
            "Circuit, ballot encryption, and tally binding",
            "Check release-manifest circuitAndKeys, publication-proof transcript proofConstruction, statementId, ballotEncryptionSchemeVersion, electionPublicKeyId, accepted ballot set hash, published ballot stream hash, final encrypted tally hash, and verifier SP-07/REL results.",
            "ProtocolPackageBindingRecord; ElectionBoundaryArtifactRecord; ElectionReportPackageRecord; ElectionReportArtifactRecord",
            "ApprovedProtocolPackageCatalog.json; release-manifest.json; publication-proof-transcript.json; tally-replay.json; VerifierOutput.json",
            "The circuit id/hash, proving-key hash, verifying-key hash, protocol package manifest hash, ballot encryption scheme, proof transcript, and tally replay hashes must match the approved Protocol Omega binding and pass verifier checks.",
            "Required",
            "Verify circuitAndKeys, ballotEncryptionSchemeVersion, publication proof transcript, tally replay binding, and VFY-SP07-000/REL-000 for the Direct evidence packages.",
            "Required for candidate rehearsal and any production activation candidate.",
            "Required for every production election before result publication or external review handoff.",
            "Current corpus validation observed good-profile replay evidence generated at 2026-06-02T12:00:00Z and sample verifier outputs generated on 2026-06-01.",
            "Accepted for RDY-REG-v0.1.8 through FEAT-160: RDY-DIM-004 is 10/10, circuitAndKeys names protocol-omega-publication-proof-v1, and the transcript uses babyjubjub-elgamal-vector-ballot-v1 with SP-07 tally replay binding.",
            "Use release-manifest generatedAt, publication-proof transcript generatedAt, tally-ready/finalize artifact timestamps, and verifier verifiedAt for the promoted election."),
        new(
            "RDY-CHECK-KMS-CUSTODY-KEY-LIFECYCLE",
            "kms-custody-key-lifecycle.md",
            "AWS KMS custody key lifecycle",
            "Check open-time per-election KMS custody creation or verified reuse, custody mode, provider family, selected profile, encryption context, decrypt-authority proof, finalization cleanup, key disablement/deletion scheduling or retry state, and reconciliation output.",
            "ElectionAdminOnlyProtectedTallyRecord; custody provider profile; custody reconciliation output; ElectionReportArtifactRecord",
            "FEAT-131-Custody-Evidence-Handoff.md; operational-custody-evidence.json; kms-custody-rehearsal validation summaries; restricted evidence index",
            "Public report output must show only provider family, custody mode, lifecycle state, tally public-key fingerprint, and safe reference hashes. Restricted reviewer evidence must carry the private custody row plus KmsKeyId/KmsKeyArn/KmsAlias/region/account boundary when admin-only AWS KMS custody applies.",
            "Required when AdminOnly uses aws_kms_per_election_envelope_v1",
            "Verify custodyMode aws_kms_per_election_envelope_v1, OPS-003 pass, executor key lifecycle, public secret-scan boundary, and restricted-only custody-key references. Default CI may use deterministic provider evidence; live AWS smoke remains restricted-only.",
            "Required with candidate restricted reviewer evidence and no public KMS identifier leakage.",
            "Required for every protected AdminOnly production election before open and again at finalize/reconciliation.",
            "FEAT-131 accepted custody evidence was produced on 2026-05-19; FEAT-161 KMS rehearsal score proposal was generated at 2026-06-02T12:00:00Z.",
            "Accepted for RDY-REG-v0.1.8 through FEAT-161: RDY-DIM-005 is 9/9, productionCustodyMode is aws_kms_per_election_envelope_v1, providerFamily is aws-kms, and raw KMS key identifiers are restricted-only.",
            "Use custody row created/destroyed/last-updated timestamps, key deletion scheduled-at timestamp, reconciliation run time, and verifier/package export time for the promoted election."),
        new(
            "RDY-CHECK-DEPLOYMENT-SOFTWARE-PROOF-BINDING",
            "deployment-software-proof-binding.md",
            "Deployment and software proof binding",
            "Check deployment proof ledger status, active proof set at open, checkpoints for DraftToOpen, OpenToClose, CloseToFinalize, FinalPackageExport, component observations, proof-family bindings, and claim limitations.",
            "ElectionDeploymentProofLedgerRecord; ElectionDeploymentProofCheckpointRecord; ElectionDeploymentProofComponentObservationRecord; ElectionDeploymentProofEventRecord; ElectionProofFamilyBindingStatusRecord; ElectionWebClientDeploymentProofObservationRecord",
            "deployment-proof-binding-ledger.json; canonical-manifest.json deploymentProofBinding; evidence-graph.json deploymentProofBinding",
            "A development/rehearsal profile may accept development-runtime deployment evidence inside the Development evidence boundary. The same evidence must remain visible as non-production evidence and cannot become a deployment/build completeness claim for Production.",
            "Development/rehearsal accepted; production deployment proof not required",
            "Do not require production deployment proof, access-control snapshot, backup/restore, or auditor-room logs. Verify development runtime self-attestation, active proof set at open, lifecycle checkpoint linkage, server component proof hash, and visible WebClient non-production boundary.",
            "Required when a PreProduction candidate exists; must verify immutable release/deployment refs, migration/config deltas, and required operational evidence.",
            "Required unless activating an unchanged accepted PreProduction candidate; direct production must run the full deployment and operations proof workflow.",
            "Non-binding ledger observed across 2026-06-05T10:19:23.846873Z to 2026-06-05T10:21:41.7215447Z; binding ledger observed across 2026-06-04T21:27:55.828125Z to 2026-06-04T21:32:52.7016107Z.",
            "Accepted for Development/rehearsal scope: server development proof matched, the WebClient production proof remains explicitly outside this scope, and no production deployment/build completeness claim is made.",
            "Use ledger created/opened/closed/finalized timestamps, checkpoint observedAtUtc values, activeProofSetIdAtOpen, component observations, and proof-family records."),
        new(
            "RDY-CHECK-LIFECYCLE-BALLOT-PUBLICATION-TALLY-COUNT",
            "lifecycle-ballot-publication-tally-count.md",
            "Lifecycle, ballot, publication, tally, and count",
            "Check finalized lifecycle, open/close/tally-ready/finalize artifact ids, accepted ballot set hash, published ballot stream hash, final encrypted tally hash, tally replay result, official/unofficial result artifact ids, and count totals.",
            "ElectionBoundaryArtifactRecord; ElectionAcceptedBallotRecord; ElectionPublishedBallotRecord; ElectionFinalizationSessionRecord; ElectionFinalizationReleaseEvidenceRecord; ElectionResultArtifactRecord; ElectionReportPackageRecord",
            "ElectionRecord.json; evidence-graph.json; tally-replay.json; result-binding.json; result-report.json",
            "Hashes and artifact ids must be consistent across the election record, evidence graph, tally replay, result binding, final manifest, and result report.",
            "Required",
            "Verify finalized lifecycle, accepted/published/tally hashes, close/tally-ready/finalize/result artifact ids, and visible count totals in the exported report package.",
            "Required for candidate rehearsal and any production activation candidate.",
            "Required for every production election before result publication.",
            "Non-binding finalized at 2026-06-05T10:21:41.7215447Z; binding finalized at 2026-06-04T21:32:52.7016107Z.",
            "Passed: both Direct runs finalized cleanly with two eligible voters, two counted votes, zero blanks, and consistent accepted/published/tally/result hashes.",
            "Use close, tally-ready, finalize, result artifact timestamps plus the verifier verifiedAt timestamp for the exported package."),
        new(
            "RDY-CHECK-VERIFIER-PACKAGE-INTEGRITY",
            "verifier-package-integrity.md",
            "Verifier package integrity",
            "Check that the verifier input manifest references the same package, election id, audit package hash, profile id, required root files, and artifact directories, then check verifier output results.",
            "Exported verification package over persisted election/report records",
            "VerifierInputManifest.json; VerifierProfile.json; AuditPackageManifest.json; VerifierOutput.json",
            "Manifest, election, accepted-ballot, published-ballot, SP-04, privacy, release, external-review, and applicable operational checks must pass or carry explicit non-claim warnings.",
            "Required",
            "Verify package manifest hashes, verifier profile binding, election consistency, accepted/published ballot checks, SP-04, privacy, development release binding, and explicit operational warnings.",
            "Required with candidate package, candidate verifier profile, and operational evidence expected for that environment.",
            "Required with production verifier profile and all production-claim evidence enabled.",
            "Non-binding verifier output checked at 2026-06-05T13:21:30.060513Z; binding verifier output checked at 2026-06-05T13:21:21.0565757Z.",
            "Passed with development warnings: exitCode=0, package/election/ballot/publication/privacy/release checks pass, OPS-002/006/008 remain out of Development scope.",
            "Use VerifierOutput.verifiedAt, exitCode, overallStatus, and per-check result timestamps where the verifier exports them."),
        new(
            "RDY-CHECK-PRIVACY-PUBLIC-RESTRICTED-BOUNDARY",
            "privacy-public-restricted-boundary.md",
            "Privacy and public/restricted boundary",
            "Check that the public package excludes restricted evidence, that reviewer-only material stays in the restricted package, and that the audit boundary note states the claim limitations.",
            "ElectionReportArtifactRecord access scopes; verification package public/restricted views",
            "audit-boundary-note.md; public-verification-package; restricted-owner-auditor-package; VFY-PRIVACY-000 result",
            "A public/restricted leakage or hidden high-assurance claim fails the profile gate even when the numeric readiness score is high.",
            "Required",
            "Verify VFY-PRIVACY-000, public package boundaries, restricted owner/auditor package separation, and audit-boundary wording.",
            "Required for candidate package handoff and reviewer access control.",
            "Required before any production publication or external reviewer handoff.",
            "Non-binding privacy verifier check ran at 2026-06-05T13:21:30.060513Z; binding privacy verifier check ran at 2026-06-05T13:21:21.0565757Z.",
            "Passed: VFY-PRIVACY-000 passes for both Direct public packages and the audit boundary notes preserve claim limits.",
            "Use the public verifier run time plus package export time and restricted package access-grant timestamp where available."),
        new(
            "RDY-CHECK-AUDITOR-RESTRICTED-ACCESS-KEYS",
            "auditor-restricted-access-keys.md",
            "Auditor restricted access and reader keys",
            "Check auditor grant role, restricted package scope, report access grant, envelope access record, auditor-room access-log hash, access-control snapshot, and the reader-access package key wrapping evidence for the intended auditor.",
            "ElectionReportAccessGrantRecord; ElectionEnvelopeAccessRecord; ElectionReportPackageRecord; ElectionReportArtifactRecord; operational access-control snapshot; auditor-room access log",
            "restricted-owner-auditor-package; artifacts/restricted/operational-access-control-snapshot.json; artifacts/restricted/auditor-room-access-log.json; audit-boundary-note.md; VerifierOutput OPS-002/OPS-008",
            "Auditor access must use a reader-access package key wrapped to the intended auditor or authorized reviewer. It must not reuse the election tally key, trustee ceremony transport keys, trustee shares, or executor private key.",
            "Not required for Direct development unless restricted reviewer access is exercised",
            "Verify any development restricted reviewer access by grant id, actor public address, package id, wrapped reader key reference, and OPS-002/OPS-008 warnings or passes.",
            "Required when a PreProduction candidate gives auditors access to restricted evidence.",
            "Required for every production auditor handoff and every Veritas restricted owner/auditor package.",
            "Direct development evidence currently keeps auditor-room/access-control as PreProduction/Production controls; no production auditor access claim is made in RDY-REG-v0.1.8.",
            "Not claimed as completed for Direct development; the row defines the required Veritas and production auditor evidence boundary.",
            "Use grant created/revoked timestamps, package sealed/exported time, access-log timestamp, and verifier verifiedAt."),
        new(
            "RDY-CHECK-VERITAS-TRUSTEE-CEREMONY-ACCEPTANCE",
            "veritas-trustee-ceremony-acceptance.md",
            "Veritas trustee ceremony and acceptance",
            "For Veritas profiles, check trustee invitations, acceptance/revocation state, active ceremony version, transcript events, trustee states, trustee transport-key fingerprints, tally public-key fingerprint, share custody declarations, bound threshold profile, active trustees, required approvals, and invalid role combinations.",
            "ElectionTrusteeInvitationRecord; ElectionCeremonyProfileRecord; ElectionCeremonyVersionRecord; ElectionCeremonyTranscriptEventRecord; ElectionCeremonyTrusteeStateRecord; ElectionCeremonyShareCustodyRecord; ElectionTrusteeControlDomainRecord; ElectionBoundaryArtifactRecord",
            "trustee-control-profile.json; trustee-control-summary.json; trustee-control-domains.json; trustee-release-evidence.json; trustee-verifier-output.json; future ceremony transcript and acceptance evidence",
            "Veritas remains not_observed or future_gated until trustee ceremony evidence proves the exact threshold, exact accepted trustee set, election-scoped trustee keys, tally public key, and custody declarations for that election.",
            "Disabled for Direct development; future-gated for Veritas",
            "Do not require trustee ceremonies for Direct runs. Verify the exclusion instead: acceptedTrusteeCount=0, finalizationShareCount=0, governedApprovalCount=0, and trustees=[] are visible.",
            "Required for any Veritas candidate; all trustee invitation, acceptance, transcript, trustee-key fingerprint, tally-public-key, state, and share-custody evidence must be present.",
            "Required for every Veritas production election before open/finalization claims.",
            "Not tested as Veritas in RDY-REG-v0.1.8; Direct exclusion checked in report packages generated at 2026-06-05T10:21:41.7215447Z and 2026-06-04T21:32:52.7016107Z.",
            "Correctly disabled/not observed for Direct: the Direct packages explicitly show zero trustees, zero finalization shares, and no Veritas ceremony claim.",
            "Use trustee invitation sent/accepted/revoked timestamps, ceremony version activation time, transcript event order, share-custody timestamps, and trustee verifier verifiedAt."),
        new(
            "RDY-CHECK-VERITAS-GOVERNED-ACTION-CLOSE-COUNTING",
            "veritas-governed-action-close-counting.md",
            "Veritas governed action and close-counting linkage",
            "For Veritas profiles, check governed proposals for Open, Close, and Finalize, approval records, approval signer keys, signed target hashes, source transaction/block refs, executed boundary artifacts, finalization session, close-counting job, trustee shares, accepted share count, and release evidence.",
            "ElectionGovernedProposalRecord; ElectionGovernedProposalApprovalRecord; ElectionFinalizationSessionRecord; ElectionCloseCountingJobRecord; ElectionFinalizationShareRecord; ElectionFinalizationReleaseEvidenceRecord",
            "governed action records; close-counting evidence; trustee share evidence; evidence-graph.json trustees and finalization fields; final report package",
            "Open, Close, and Finalize approvals must be signed by eligible owner/trustee keys for the exact action target. The tally protocol/version/profile used at close-counting and finalize must match the Protocol Omega binding sealed at open, and every trustee share must be bound to the exact close artifact and accepted-ballot-set hash.",
            "Disabled for Direct development; future-gated for Veritas",
            "Do not require governed trustee approvals for Direct runs. Verify the exclusion and the Direct clean-finalization result binding instead.",
            "Required for any Veritas candidate close/count/finalize rehearsal.",
            "Required for every Veritas production close/count/finalize workflow.",
            "Not tested as Veritas in RDY-REG-v0.1.8; Direct result binding checked at finalization/export times 2026-06-05T10:21:41.7215447Z and 2026-06-04T21:32:52.7016107Z.",
            "Correctly disabled/not observed for Direct: governedApprovalCount=0 and finalizationShareCount=0 are visible, while Direct clean finalization and result binding pass.",
            "Use governed proposal created/approved/executed timestamps, close-counting job timestamps, finalization session timestamps, trustee share release times, and final verifier verifiedAt."),
        new(
            "RDY-CHECK-NO-KEY-MATERIAL-PERSISTENCE",
            "no-key-material-persistence.md",
            "No persisted private key, trustee share, or witness material",
            "Check that no public, restricted, database, log, support, backup, report, verifier-output, or package artifact contains reusable tally private keys, trustee raw shares, executor private keys, vote secrets, encryption randomness, raw proof witnesses, or per-ballot decrypt material.",
            "ElectionCeremonyShareCustodyRecord; ElectionFinalizationShareRecord; ElectionFinalizationReleaseEvidenceRecord; ElectionReportPackageRecord; ElectionReportArtifactRecord; operational/support/log/backup evidence surfaces",
            "public-verification-package; restricted-owner-auditor-package; support-export-privacy-proof.json; operational log/privacy scans; backup/restore evidence; no-secret-scan-result.json; VerifierOutput VFY-PRIVACY/OPS checks",
            "The report must state where the scan looked. A pass requires explicit negative findings for public package, restricted package, database export surfaces, support exports, logs, backups, and verifier outputs; any raw trustee share, private key, executor private key, proof witness, or vote-secret finding blocks the claim.",
            "Required",
            "Verify development packages include no raw trustee shares, no executor private key, no proof witness, no vote secret, and no plaintext vote in public/restricted exports.",
            "Required with candidate package scans and restricted operational evidence scans.",
            "Required for every production election package, backup/restore proof, support export, and auditor handoff.",
            "Direct development privacy and operational boundary checks ran at the public verifier timestamps; Veritas-specific raw-share persistence is future-gated until a Veritas run exists.",
            "Partially applicable for RDY-REG-v0.1.8 Direct evidence: public/restricted package privacy boundaries pass, while Veritas trustee raw-share persistence remains a required future Veritas check.",
            "Use package export time, no-secret scan time, verifier verifiedAt, support-export scan time, log-scan time, and backup/restore evidence timestamp."),
    ];

    private static readonly ExternalAuditorEntryPointRow[] ExternalAuditorEntryPointRows =
    [
        new(
            "Protocol Omega package",
            "Which Protocol Omega package, spec, proof set, and release package was used for this election?",
            "ProtocolPackageBindingRecord; ApprovedProtocolPackageCatalogEntryRecord; ElectionRecord",
            "ApprovedProtocolPackageCatalog.json; release-manifest.json; canonical-manifest.json; evidence-graph.json",
            "RDY-CHECK-PROTOCOL-OMEGA-BINDING; REL-000; SP-08",
            "A stale or unapproved package, hash mismatch, wrong profile, mutable reference, or open-time binding mismatch blocks the claim."),
        new(
            "Circuit and proof binding",
            "Was the correct circuit, proving key, verifying key, ballot encryption scheme, publication proof, tally replay, and result binding used?",
            "ProtocolPackageBindingRecord; ElectionBoundaryArtifactRecord; ElectionReportPackageRecord; ElectionReportArtifactRecord",
            "release-manifest.json circuitAndKeys; publication-proof-transcript.json; tally-replay.json; result-binding.json; VerifierOutput.json",
            "RDY-CHECK-CIRCUIT-BALLOT-TALLY-BINDING; VFY-SP07-000; REL-000",
            "A circuit id/hash mismatch, key hash mismatch, ballot encryption scheme mismatch, statement mismatch, or tally replay mismatch blocks the claim."),
        new(
            "Invitations and access grants",
            "Were trustees, auditors, and restricted reviewers invited or granted access only through the intended election-scoped records?",
            "ElectionTrusteeInvitationRecord; ElectionReportAccessGrantRecord; ElectionEnvelopeAccessRecord; ElectionReportPackageRecord",
            "Invitation transaction refs; restricted-owner-auditor-package; operational-access-control-snapshot.json; auditor-room-access-log.json",
            "RDY-CHECK-AUDITOR-RESTRICTED-ACCESS-KEYS; OPS-002; OPS-008; VFY-PRIVACY-000",
            "An unauthorized grant, missing or revoked actor, reader key bound to the wrong recipient, or restricted package leak blocks the claim."),
        new(
            "AWS KMS custody key lifecycle",
            "Was the per-election AWS KMS custody key created or verified for the election, constrained to the right context, and cleaned up without public leakage?",
            "ElectionAdminOnlyProtectedTallyRecord; custody provider profile; custody reconciliation output; ElectionReportArtifactRecord",
            "operational-custody-evidence.json; kms-custody-rehearsal validation summaries; restricted evidence index",
            "RDY-CHECK-KMS-CUSTODY-KEY-LIFECYCLE; RDY-DIM-005; OPS-003; VFY-PRIVACY-000",
            "A missing restricted KMS reference, wrong election/profile/encryption context, undeleted or unscheduled key, public KMS id leak, or decrypt-authority mismatch blocks the claim."),
        new(
            "Trustee key ceremony",
            "For Veritas profiles, did the exact trustee set participate in the key ceremony with the expected threshold, transport-key fingerprints, tally public key, and share custody declarations?",
            "ElectionCeremonyProfileRecord; ElectionCeremonyVersionRecord; ElectionCeremonyTranscriptEventRecord; ElectionCeremonyTrusteeStateRecord; ElectionCeremonyShareCustodyRecord; ElectionTrusteeControlDomainRecord",
            "trustee-control-profile.json; trustee-control-summary.json; trustee-control-domains.json; trustee-release-evidence.json; trustee-verifier-output.json",
            "RDY-CHECK-VERITAS-TRUSTEE-CEREMONY-ACCEPTANCE; CTRL-*; SP-06",
            "A wrong threshold, missing trustee, duplicate account/person/custody domain, wrong trustee key fingerprint, tally public-key mismatch, or raw share leakage blocks the claim."),
        new(
            "Open approval",
            "Was Open approved by eligible owner or trustee keys for the exact election action target and sealed Protocol Omega binding?",
            "ElectionGovernedProposalRecord; ElectionGovernedProposalApprovalRecord; ElectionBoundaryArtifactRecord; ProtocolPackageBindingRecord",
            "governed action records; open boundary artifact; canonical-manifest.json; evidence-graph.json",
            "RDY-CHECK-VERITAS-GOVERNED-ACTION-CLOSE-COUNTING; approval signer key checks; signed target hash checks",
            "A missing eligible approval, signed wrong action target, wrong signer key, missing tx/block ref, or unsealed Protocol Omega package at open blocks the claim."),
        new(
            "Ballots and publication",
            "Do accepted ballots, published ballots, receipt commitments, and publication proof bind to the same election and approved circuit package?",
            "ElectionAcceptedBallotRecord; ElectionPublishedBallotRecord; ElectionBoundaryArtifactRecord; ElectionReportArtifactRecord",
            "accepted-ballot-set; published-ballot-stream; receipt commitments; publication-proof-transcript.json; VerifierOutput.json",
            "RDY-CHECK-LIFECYCLE-BALLOT-PUBLICATION-TALLY-COUNT; RDY-CHECK-CIRCUIT-BALLOT-TALLY-BINDING; VFY-ACCEPTED; VFY-PUBLISHED; SP-04; SP-07",
            "A hash mismatch, duplicate nullifier, missing receipt commitment, unexpected plaintext, vote secret, or proof transcript mismatch blocks the claim."),
        new(
            "Close, count, and tally",
            "Were Close and Count approved for the exact close artifact, accepted ballot set, final encrypted tally, trustee shares, and tally replay target?",
            "ElectionGovernedProposalRecord; ElectionGovernedProposalApprovalRecord; ElectionCloseCountingJobRecord; ElectionFinalizationSessionRecord; ElectionFinalizationShareRecord; ElectionFinalizationReleaseEvidenceRecord",
            "close-counting evidence; tally-replay.json; result-binding.json; final encrypted tally hash; trustee-release-evidence.json",
            "RDY-CHECK-VERITAS-GOVERNED-ACTION-CLOSE-COUNTING; SP-07; CTRL-008; CTRL-009; CTRL-011",
            "A missing Close/Count approval, tally replay mismatch, trustee share target mismatch, insufficient accepted shares, or final encrypted tally mismatch blocks the claim."),
        new(
            "Finalize and results",
            "Was Finalize approved, and do result artifacts, report packages, verifier output, and restricted auditor package all bind to the same finalized election state?",
            "ElectionFinalizationSessionRecord; ElectionResultArtifactRecord; ElectionReportPackageRecord; ElectionReportArtifactRecord; ElectionReportAccessGrantRecord",
            "result-report.json; AuditPackageManifest.json; VerifierInputManifest.json; VerifierOutput.json; restricted-owner-auditor-package",
            "RDY-CHECK-LIFECYCLE-BALLOT-PUBLICATION-TALLY-COUNT; RDY-CHECK-VERIFIER-PACKAGE-INTEGRITY; RDY-CHECK-PRIVACY-PUBLIC-RESTRICTED-BOUNDARY",
            "A result/report mismatch, stale package, verifier failure, restricted leak, missing auditor boundary, or finalize approval mismatch blocks the claim."),
        new(
            "No key material persisted",
            "Where did the evidence scan look, and did it confirm that no reusable private key, trustee raw share, proof witness, vote secret, or decrypt material was recorded?",
            "ElectionCeremonyShareCustodyRecord; ElectionFinalizationShareRecord; ElectionFinalizationReleaseEvidenceRecord; ElectionReportPackageRecord; ElectionReportArtifactRecord; operational/support/log/backup evidence surfaces",
            "public-verification-package; restricted-owner-auditor-package; support-export-privacy-proof.json; operational log/privacy scans; backup/restore evidence; no-secret-scan-result.json",
            "RDY-CHECK-NO-KEY-MATERIAL-PERSISTENCE; VFY-PRIVACY-000; OPS privacy scans",
            "Any raw trustee share, private key, executor private key, proof witness, vote secret, encryption randomness, or per-ballot decrypt material finding blocks the claim."),
    ];

    private static readonly HashSet<string> EvidenceStates = new(StringComparer.Ordinal)
    {
        "missing",
        "placeholder",
        "draft",
        "observed",
        "accepted",
        "blocked",
        "rejected",
        "superseded",
    };

    private static readonly HashSet<string> ClaimEffects = new(StringComparer.Ordinal)
    {
        "none",
        "score_increase",
        "downgrade",
        "block",
        "unblock",
        "residual_risk_update",
    };

    private static readonly HashSet<string> PublicForbiddenTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "51/100",
        "total score",
        "dimension score",
        "score delta",
        "score history",
        "restricted_reviewer",
        "internal/",
        "sha-256",
        "reviewer-only",
        "client data",
        "raw log",
        "anomaly detail",
        "deployment credential",
    };

    public ReadinessRegisterPromotionResult Promote(ReadinessRegisterPromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidatePathConfiguration(options.Paths);

        if (options.Scaffold)
        {
            ScaffoldMissingSourceFiles(options.Paths);
        }

        var missing = new[]
            {
                options.Paths.SchemaPath,
                options.Paths.RegisterPath,
                options.Paths.ExamplePath,
            }
            .Where(path => !File.Exists(path))
            .Select(path => Path.GetRelativePath(options.Paths.SourceRoot, path))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Readiness register promotion failed because required source files are missing.",
                missing);
        }

        var schema = ReadJsonObject(options.Paths.SchemaPath, SchemaFileName);
        var register = ReadJsonObject(options.Paths.RegisterPath, RegisterFileName);
        var example = ReadJsonObject(options.Paths.ExamplePath, ExampleFileName);
        var feat156Promotion = TryApplyFeat156ProductionRolloutPromotion(register, options);
        var internalAudit95Promotion = TryApplyInternalAudit95FinalPromotion(register, options);
        var developmentProfileClarification = TryApplyDevelopmentProfileClarificationRelease(register, options);

        ApplyCommandOverrides(register, options);
        if (internalAudit95Promotion is not null || IsInternalAudit95Accepted(register))
        {
            EnsureInternalAudit95ClaimProfiles(register);
            example["claimProfiles"] = BuildInternalAudit95ExampleClaimProfiles();
        }

        var validationErrors = new List<string>();
        ValidateSchemaDocument(schema, validationErrors);
        ValidateRegister(register, options, validationErrors);
        ValidateExample(example, options, validationErrors);
        if (validationErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Readiness register promotion failed validation.",
                validationErrors);
        }

        var generatedAt =
            options.GeneratedAt ??
            developmentProfileClarification?.GeneratedAt ??
            internalAudit95Promotion?.GeneratedAt ??
            feat156Promotion?.GeneratedAt ??
            DateTimeOffset.UtcNow;
        var registerVersion = GetRequiredString(register, "registerVersion");
        var registerVersionId = GetRequiredString(register, "registerVersionId");
        var status = GetRequiredString(register, "status");
        var totalScore = GetRequiredInt(GetRequiredObject(register, "score"), "total");
        var strongestAllowedClaim = GetCurrentStrongestAllowedClaim(register);
        var publicationStatus = GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus");

        var promotedFiles = BuildPromotedFiles(schema, register, example);
        promotedFiles.AddRange(BuildProfileEvidenceProcedurePageFiles(register));
        promotedFiles.Add(BuildExternalAuditorEntryPointPageFile(register));
        promotedFiles.Add(new PromotedFile(
            ScorecardFileName,
            "restricted",
            EncodingWithoutBom(GetScorecardMarkdown(register, promotedFiles)),
            "text/markdown"));
        promotedFiles.Add(new PromotedFile(
            RestrictedReviewerExtractFileName,
            "restricted",
            EncodingWithoutBom(GetRestrictedReviewerExtractMarkdown(register)),
            "text/markdown"));
        promotedFiles.Add(new PromotedFile(
            PublicSafeSummaryFileName,
            "public-safe",
            EncodingWithoutBom(GetPublicSafeSummaryMarkdown(register)),
            "text/markdown"));

        ValidateGeneratedViews(promotedFiles, validationErrors);
        if (validationErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Readiness register generated views failed validation.",
                validationErrors);
        }

        var archiveFileName = $"{ArchivePrefix}-{registerVersion}.zip";
        var archiveBytes = BuildDeterministicArchive(promotedFiles);
        var archiveHash = ComputeSha256Hex(archiveBytes);
        var manifestWithoutHash = BuildManifest(
            register,
            generatedAt,
            promotedFiles,
            archiveFileName,
            archiveBytes.Length,
            archiveHash,
            manifestHash: null);
        var manifestHash = ComputeSha256Hex(EncodingWithoutBom(SerializeJson(manifestWithoutHash)));
        var manifest = BuildManifest(
            register,
            generatedAt,
            promotedFiles,
            archiveFileName,
            archiveBytes.Length,
            archiveHash,
            manifestHash);
        var manifestBytes = EncodingWithoutBom(SerializeJson(manifest));
        promotedFiles.Add(new PromotedFile(
            ManifestFileName,
            "restricted",
            manifestBytes,
            "application/json"));

        EnsureCatalogAllowsPromotion(options.Paths.CatalogPath, registerVersionId, manifestHash, archiveHash);
        var feat147AuditPackage = Feat147PromotionAudit.TryGenerate(options.Paths, register, generatedAt);
        var feat150CleanupPackage = Feat150CleanupAudit.TryGenerate(
            options.Paths,
            register,
            generatedAt,
            GetPromotedFileContent(promotedFiles, RegisterFileName),
            GetPromotedFileContent(promotedFiles, ScorecardFileName),
            GetPromotedFileContent(promotedFiles, RestrictedReviewerExtractFileName),
            GetPromotedFileContent(promotedFiles, PublicSafeSummaryFileName));
        var feat156ReviewerOutputPackage = Feat156ReviewerOutputs.TryGenerate(
            options.Paths,
            register,
            generatedAt,
            manifestHash,
            archiveHash);

        var versionOutputRoot = Path.Combine(options.Paths.OutputRoot, registerVersion);
        var writtenFiles = new List<string>();
        if (options.CheckOnly)
        {
            var checkErrors = ValidateExistingPromotedArtifacts(
                options.Paths,
                versionOutputRoot,
                promotedFiles,
                archiveFileName,
                archiveBytes,
                manifest,
                manifestHash,
                archiveHash);
            if (feat147AuditPackage is not null)
            {
                checkErrors.AddRange(Feat147PromotionAudit.ValidateExistingArtifacts(
                    Feat147PromotionAudit.PackageRoot(options.Paths),
                    feat147AuditPackage.Artifacts));
            }

            if (feat150CleanupPackage is not null)
            {
                checkErrors.AddRange(Feat150CleanupAudit.ValidateExistingArtifacts(
                    Feat150CleanupAudit.PackageRoot(options.Paths),
                    feat150CleanupPackage.Artifacts));
            }

            if (feat156ReviewerOutputPackage is not null)
            {
                checkErrors.AddRange(Feat156ReviewerOutputs.ValidateExistingArtifacts(
                    Feat156ReviewerOutputs.PackageRoot(options.Paths),
                    feat156ReviewerOutputPackage.Artifacts));
            }

            if (checkErrors.Count > 0)
            {
                throw new ReadinessRegisterPromotionException(
                    "Readiness register check-only validation failed.",
                    checkErrors);
            }
        }
        else if (!options.ValidateOnly)
        {
            WritePromotedArtifacts(
                options.Paths,
                versionOutputRoot,
                promotedFiles,
                archiveFileName,
                archiveBytes,
                manifest,
                writtenFiles);
            if (feat147AuditPackage is not null)
            {
                Feat147PromotionAudit.WriteArtifacts(
                    Feat147PromotionAudit.PackageRoot(options.Paths),
                    feat147AuditPackage.Artifacts,
                    writtenFiles);
            }

            if (feat150CleanupPackage is not null)
            {
                Feat150CleanupAudit.WriteArtifacts(
                    Feat150CleanupAudit.PackageRoot(options.Paths),
                    feat150CleanupPackage.Artifacts,
                    writtenFiles);
            }

            if (feat156ReviewerOutputPackage is not null)
            {
                Feat156ReviewerOutputs.WriteArtifacts(
                    Feat156ReviewerOutputs.PackageRoot(options.Paths),
                    feat156ReviewerOutputPackage.Artifacts,
                    writtenFiles);
            }
        }

        return new ReadinessRegisterPromotionResult(
            registerVersion,
            registerVersionId,
            status,
            generatedAt,
            totalScore,
            strongestAllowedClaim,
            publicationStatus,
            manifestHash,
            archiveHash,
            options.Paths.CatalogPath,
            versionOutputRoot,
            writtenFiles);
    }

    private static void ValidatePathConfiguration(ReadinessRegisterPromotionPaths paths)
    {
        var workspaceRoot = Path.GetFullPath(paths.WorkspaceRoot);
        if (!Directory.Exists(workspaceRoot))
        {
            throw new ReadinessRegisterPromotionException(
                "Workspace root does not exist.",
                [workspaceRoot]);
        }

        EnsureContained(workspaceRoot, Path.GetFullPath(paths.SourceRoot), "Source root must stay inside workspace root.");
        EnsureContained(workspaceRoot, Path.GetFullPath(paths.OutputRoot), "Output root must stay inside workspace root.");

        if (!Directory.Exists(Path.Combine(workspaceRoot, "hush-memory-bank")) ||
            !Directory.Exists(Path.Combine(workspaceRoot, "hush-documents")) ||
            !Directory.Exists(Path.Combine(workspaceRoot, "hush-server-node")))
        {
            throw new ReadinessRegisterPromotionException(
                "Workspace root must contain hush-memory-bank, hush-documents, and hush-server-node.",
                [workspaceRoot]);
        }
    }

    private static void EnsureContained(string root, string child, string message)
    {
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!child.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReadinessRegisterPromotionException(message, [child]);
        }
    }

    private static JsonObject ReadJsonObject(string path, string displayName)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new JsonException("Root is not an object.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new ReadinessRegisterPromotionException(
                $"Could not read {displayName}.",
                [$"{path}: {ex.Message}"]);
        }
    }

    private static Feat156PromotionApplication? TryApplyFeat156ProductionRolloutPromotion(
        JsonObject register,
        ReadinessRegisterPromotionOptions options)
    {
        if (!IsFeat156ProductionRolloutRequest(options))
        {
            return null;
        }

        var sourcePath = Path.Combine(
            options.Paths.WorkspaceRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            "Production-Rollout-Promotion-Register",
            "examples",
            "release-baseline",
            Feat156PromotionSourceFileName);
        if (!File.Exists(sourcePath))
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-156 production rollout promotion source is required.",
                [Path.GetRelativePath(options.Paths.WorkspaceRoot, sourcePath)]);
        }

        var source = ReadJsonObject(sourcePath, Feat156PromotionSourceFileName);
        var sourceValidation = new Feat156PromotionSourceValidator().Validate(source, options.Paths.WorkspaceRoot);
        if (!sourceValidation.IsValid)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-156 production rollout promotion source failed validation.",
                sourceValidation.Errors);
        }

        var baselineErrors = ValidateFeat156Baseline(register, source);
        if (baselineErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-156 production rollout promotion source does not match the current baseline register.",
                baselineErrors);
        }

        var generatedAt = ParseRequiredTimestamp(source, "generatedAt");
        ApplyFeat156ProductionRolloutPromotionSource(register, source, generatedAt, sourceValidation.RecalculatedScore, options.Paths.WorkspaceRoot);
        return new Feat156PromotionApplication(generatedAt);
    }

    private static InternalAudit95PromotionApplication? TryApplyInternalAudit95FinalPromotion(
        JsonObject register,
        ReadinessRegisterPromotionOptions options)
    {
        if (!IsInternalAudit95FinalPromotionRequest(options))
        {
            return null;
        }

        var sourcePath = GetInternalAudit95PromotionSourcePath(options.Paths);
        if (!File.Exists(sourcePath))
        {
            throw new ReadinessRegisterPromotionException(
                "Internal audit 95 promotion source is missing.",
                [Path.GetRelativePath(options.Paths.WorkspaceRoot, sourcePath)]);
        }

        var source = ReadJsonObject(sourcePath, InternalAudit95PromotionSourceFileName);
        var sourceErrors = ValidateInternalAudit95PromotionSource(source, options.Paths.WorkspaceRoot);
        if (sourceErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Internal audit 95 promotion source failed validation.",
                sourceErrors);
        }

        var baselineErrors = ValidateInternalAudit95Baseline(register, source);
        if (baselineErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Internal audit 95 promotion source does not match the current baseline register.",
                baselineErrors);
        }

        var generatedAt = ParseRequiredTimestamp(source, "generatedAt");
        ApplyInternalAudit95PromotionSource(register, source, generatedAt, options.Paths.WorkspaceRoot);
        return new InternalAudit95PromotionApplication(generatedAt);
    }

    private static bool IsInternalAudit95FinalPromotionRequest(ReadinessRegisterPromotionOptions options) =>
        string.Equals(options.Version, InternalAudit95FinalTargetVersion, StringComparison.Ordinal) &&
        string.Equals(options.PublicationStatus, InternalAudit95FinalTargetPublicationStatus, StringComparison.Ordinal);

    private static DevelopmentProfileClarificationApplication? TryApplyDevelopmentProfileClarificationRelease(
        JsonObject register,
        ReadinessRegisterPromotionOptions options)
    {
        if (!IsDevelopmentProfileClarificationReleaseRequest(options))
        {
            return null;
        }

        var validationErrors = ValidateDevelopmentProfileClarificationBaseline(register);
        if (validationErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Development profile clarification release source does not match the required v0.1.8 baseline.",
                validationErrors);
        }

        var generatedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow;
        register["registerVersion"] = DevelopmentProfileClarificationTargetVersion;
        register["registerVersionId"] = $"RDY-REG-{DevelopmentProfileClarificationTargetVersion}";
        register["status"] = "AcceptedInternal";
        register["promotedAt"] = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        register["sourceCommit"] = DevelopmentProfileClarificationSourceId;
        GetRequiredObject(register, "generatedViews")["publicSafePublicationStatus"] =
            DevelopmentProfileClarificationPublicationStatus;

        return new DevelopmentProfileClarificationApplication(generatedAt);
    }

    private static bool IsDevelopmentProfileClarificationReleaseRequest(ReadinessRegisterPromotionOptions options) =>
        string.Equals(options.Version, DevelopmentProfileClarificationTargetVersion, StringComparison.Ordinal) &&
        string.Equals(options.PublicationStatus, DevelopmentProfileClarificationPublicationStatus, StringComparison.Ordinal);

    private static List<string> ValidateDevelopmentProfileClarificationBaseline(JsonObject register)
    {
        var errors = new List<string>();
        var score = GetRequiredObject(register, "score");
        AddMismatch(errors, "registerVersion", InternalAudit95FinalTargetVersion, GetStringOrDefault(register, "registerVersion"));
        AddMismatch(errors, "registerVersionId", $"RDY-REG-{InternalAudit95FinalTargetVersion}", GetStringOrDefault(register, "registerVersionId"));
        AddMismatch(errors, "status", "AcceptedInternal", GetStringOrDefault(register, "status"));
        AddMismatch(
            errors,
            "score.total",
            InternalAudit95ReadinessPlan.TargetScore.ToString(CultureInfo.InvariantCulture),
            GetIntOrDefault(score, "total").ToString(CultureInfo.InvariantCulture));
        AddMismatch(errors, "strongestAllowedClaim", "friendly_organization_pilot", GetCurrentStrongestAllowedClaim(register));
        AddMismatch(
            errors,
            "publicationStatus",
            DevelopmentProfileClarificationPublicationStatus,
            GetStringOrDefault(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus"));
        return errors;
    }

    private static string GetInternalAudit95PromotionSourcePath(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-documents",
        "PrivateServer_ElectronicVoting",
        "Internal-Audit-95-Promotion-Register",
        "package",
        InternalAudit95PromotionSourceFileName);

    private static List<string> ValidateInternalAudit95PromotionSource(JsonObject source, string workspaceRoot)
    {
        var errors = new List<string>();
        if (GetStringOrDefault(source, "schemaVersion") != "internal-audit-95-promotion-source.v1")
        {
            errors.Add("schemaVersion must be internal-audit-95-promotion-source.v1.");
        }

        if (GetStringOrDefault(source, "status") != "accepted")
        {
            errors.Add("status must be accepted.");
        }

        var target = source["targetRegister"] as JsonObject;
        if (target is null)
        {
            errors.Add("targetRegister is required.");
        }
        else
        {
            AddMismatch(errors, "targetRegister.registerVersion", InternalAudit95FinalTargetVersion, GetStringOrDefault(target, "registerVersion"));
            AddMismatch(errors, "targetRegister.registerVersionId", $"RDY-REG-{InternalAudit95FinalTargetVersion}", GetStringOrDefault(target, "registerVersionId"));
            AddMismatch(errors, "targetRegister.status", "AcceptedInternal", GetStringOrDefault(target, "status"));
            AddMismatch(errors, "targetRegister.totalScore", InternalAudit95ReadinessPlan.TargetScore.ToString(CultureInfo.InvariantCulture), GetIntOrDefault(target, "totalScore").ToString(CultureInfo.InvariantCulture));
            AddMismatch(errors, "targetRegister.publicationStatus", InternalAudit95FinalTargetPublicationStatus, GetStringOrDefault(target, "publicationStatus"));
            AddMismatch(errors, "targetRegister.strongestAllowedClaim", "friendly_organization_pilot", GetStringOrDefault(target, "strongestAllowedClaim"));
        }

        var baseline = source["baselineRegister"] as JsonObject;
        if (baseline is null)
        {
            errors.Add("baselineRegister is required.");
        }
        else
        {
            AddMismatch(errors, "baselineRegister.registerVersion", InternalAudit95ReadinessPlan.TargetVersion, GetStringOrDefault(baseline, "registerVersion"));
            AddMismatch(errors, "baselineRegister.registerVersionId", $"RDY-REG-{InternalAudit95ReadinessPlan.TargetVersion}", GetStringOrDefault(baseline, "registerVersionId"));
            AddMismatch(errors, "baselineRegister.totalScore", "80", GetIntOrDefault(baseline, "totalScore").ToString(CultureInfo.InvariantCulture));
        }

        if (source["scoreMovements"] is not JsonArray movements)
        {
            errors.Add("scoreMovements is required.");
            return errors;
        }

        if (movements.Count != InternalAudit95ReadinessPlan.Tasks.Length)
        {
            errors.Add($"scoreMovements must contain {InternalAudit95ReadinessPlan.Tasks.Length} items.");
        }

        var movementByDimension = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var movement in movements.Select(node => node as JsonObject))
        {
            if (movement is null)
            {
                errors.Add("scoreMovements items must be objects.");
                continue;
            }

            var dimensionId = GetStringOrDefault(movement, "dimensionId");
            if (string.IsNullOrWhiteSpace(dimensionId))
            {
                errors.Add("scoreMovements.dimensionId is required.");
                continue;
            }

            if (!movementByDimension.TryAdd(dimensionId, movement))
            {
                errors.Add($"Duplicate score movement for {dimensionId}.");
            }
        }
        var acceptedDelta = 0;
        foreach (var task in InternalAudit95ReadinessPlan.Tasks)
        {
            if (!movementByDimension.TryGetValue(task.DimensionId, out var movement))
            {
                errors.Add($"Missing score movement for {task.DimensionId}.");
                continue;
            }

            AddMismatch(errors, $"{task.DimensionId}.featureId", task.FeatureId, GetStringOrDefault(movement, "featureId"));
            AddMismatch(errors, $"{task.DimensionId}.targetBlockerId", task.BlockerId, GetStringOrDefault(movement, "targetBlockerId"));
            var previousScore = GetIntOrDefault(movement, "previousScore");
            var acceptedScore = GetIntOrDefault(movement, "acceptedScore");
            var delta = GetIntOrDefault(movement, "delta");
            if (previousScore != 8)
            {
                errors.Add($"{task.DimensionId}.previousScore must be 8.");
            }

            if (acceptedScore != task.TargetScore)
            {
                errors.Add($"{task.DimensionId}.acceptedScore must be {task.TargetScore}.");
            }

            if (delta != acceptedScore - previousScore)
            {
                errors.Add($"{task.DimensionId}.delta must equal acceptedScore - previousScore.");
            }

            acceptedDelta += delta;
            if (movement["directRegisterMutation"]?.GetValue<bool>() != false)
            {
                errors.Add($"{task.DimensionId}.directRegisterMutation must be false.");
            }

            if (GetReadinessEvidenceIds(movement).Count == 0)
            {
                errors.Add($"{task.DimensionId}.evidenceIds must include a FEAT-130 readiness evidence id.");
            }

            ValidateInternalAudit95ArtifactRefs(movement, workspaceRoot, $"{task.DimensionId}.artifactRefs", errors);
        }

        if (acceptedDelta != InternalAudit95ReadinessPlan.TargetScore - 80)
        {
            errors.Add($"scoreMovements delta must sum to {InternalAudit95ReadinessPlan.TargetScore - 80}.");
        }

        return errors;
    }

    private static void ValidateInternalAudit95ArtifactRefs(
        JsonObject movement,
        string workspaceRoot,
        string path,
        List<string> errors)
    {
        if (movement["artifactRefs"] is not JsonArray artifactRefs || artifactRefs.Count == 0)
        {
            errors.Add($"{path} must contain at least one artifact ref.");
            return;
        }

        foreach (var artifactRef in artifactRefs.Select((node, index) => (node, index)))
        {
            if (artifactRef.node is not JsonObject item)
            {
                errors.Add($"{path}[{artifactRef.index}] must be an object.");
                continue;
            }

            var relativePath = GetStringOrDefault(item, "path");
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                errors.Add($"{path}[{artifactRef.index}].path is required.");
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
            if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{path}[{artifactRef.index}].path must stay within the workspace.");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                errors.Add($"{path}[{artifactRef.index}] does not exist: {relativePath}.");
                continue;
            }

            var expectedHash = NormalizeSha256(GetStringOrDefault(item, "sha256Hash"));
            if (!HexSha256Pattern.IsMatch(expectedHash))
            {
                errors.Add($"{path}[{artifactRef.index}].sha256Hash must be a SHA-256 hash.");
                continue;
            }

            var actualHash = ComputeSha256Hex(File.ReadAllBytes(fullPath));
            if (actualHash != expectedHash)
            {
                errors.Add($"{path}[{artifactRef.index}] hash mismatch for {relativePath}.");
            }
        }
    }

    private static List<string> ValidateInternalAudit95Baseline(JsonObject register, JsonObject source)
    {
        var errors = new List<string>();
        var baseline = GetRequiredObject(source, "baselineRegister");
        var score = GetRequiredObject(register, "score");

        AddMismatch(errors, "registerVersionId", GetRequiredString(baseline, "registerVersionId"), GetStringOrDefault(register, "registerVersionId"));
        AddMismatch(errors, "registerVersion", GetRequiredString(baseline, "registerVersion"), GetStringOrDefault(register, "registerVersion"));
        AddMismatch(errors, "status", GetStringOrDefault(baseline, "status"), GetStringOrDefault(register, "status"));
        AddMismatch(
            errors,
            "score.total",
            GetIntOrDefault(baseline, "totalScore").ToString(CultureInfo.InvariantCulture),
            GetIntOrDefault(score, "total").ToString(CultureInfo.InvariantCulture));
        AddMismatch(errors, "strongestAllowedClaim", GetStringOrDefault(baseline, "strongestAllowedClaim"), GetCurrentStrongestAllowedClaim(register));

        return errors;
    }

    private static bool IsFeat156ProductionRolloutRequest(ReadinessRegisterPromotionOptions options) =>
        string.Equals(options.Version, Feat156TargetVersion, StringComparison.Ordinal) &&
        string.Equals(options.PublicationStatus, Feat156TargetPublicationStatus, StringComparison.Ordinal) ||
        string.Equals(options.Version, InternalAudit95ReadinessPlan.TargetVersion, StringComparison.Ordinal) &&
        string.Equals(options.PublicationStatus, InternalAudit95ReadinessPlan.PublicationStatus, StringComparison.Ordinal);

    private static List<string> ValidateFeat156Baseline(JsonObject register, JsonObject source)
    {
        var errors = new List<string>();
        var baseline = GetRequiredObject(source, "baselineRegister");
        var score = GetRequiredObject(register, "score");

        AddMismatch(
            errors,
            "registerVersionId",
            GetStringOrDefault(baseline, "registerVersionId"),
            GetStringOrDefault(register, "registerVersionId"));
        AddMismatch(
            errors,
            "registerVersion",
            GetStringOrDefault(baseline, "registerVersion"),
            GetStringOrDefault(register, "registerVersion"));
        AddMismatch(
            errors,
            "status",
            GetStringOrDefault(baseline, "status"),
            GetStringOrDefault(register, "status"));
        AddMismatch(
            errors,
            "score.total",
            GetIntOrDefault(baseline, "totalScore").ToString(CultureInfo.InvariantCulture),
            GetIntOrDefault(score, "total").ToString(CultureInfo.InvariantCulture));
        AddMismatch(
            errors,
            "strongestAllowedClaim",
            GetStringOrDefault(baseline, "strongestAllowedClaim"),
            GetCurrentStrongestAllowedClaim(register));

        return errors;
    }

    private static void AddMismatch(List<string> errors, string field, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            errors.Add($"{field} must be {expected}; found {actual}.");
        }
    }

    private static void ApplyFeat156ProductionRolloutPromotionSource(
        JsonObject register,
        JsonObject source,
        DateTimeOffset generatedAt,
        int recalculatedScore,
        string workspaceRoot)
    {
        var target = GetRequiredObject(source, "targetRegister");
        register["registerVersion"] = GetRequiredString(target, "registerVersion");
        register["registerVersionId"] = GetRequiredString(target, "registerVersionId");
        register["status"] = GetRequiredString(target, "status");
        register["promotedAt"] = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        register["sourceCommit"] = GetRequiredString(source, "sourceId");

        var score = GetRequiredObject(register, "score");
        score["total"] = recalculatedScore;
        var scoreModel = GetRequiredObject(source, "scoreModel");
        var internalAuditTargetScore = GetIntOrDefault(scoreModel, "internalAuditTargetScore");
        if (internalAuditTargetScore > 0)
        {
            score["strongerTargetScore"] = internalAuditTargetScore;
        }

        var claimPolicy = GetRequiredObject(register, "claimPolicy");
        claimPolicy["strongestAllowedV1Claim"] = GetRequiredString(target, "strongestAllowedClaim");
        if (internalAuditTargetScore > 0)
        {
            claimPolicy["strongerTargetScore"] = internalAuditTargetScore;
        }

        if (GetRequiredString(target, "strongestAllowedClaim") == "production_organizational_rollout")
        {
            RemoveAlwaysBlockedClaim(claimPolicy, "production_organizational_rollout");
        }
        else
        {
            AddAlwaysBlockedClaim(claimPolicy, "production_organizational_rollout");
            AddAlwaysBlockedClaim(claimPolicy, "public_or_state_election");
        }

        GetRequiredObject(register, "generatedViews")["publicSafePublicationStatus"] =
            GetRequiredString(target, "publicationStatus");

        var scoreMovements = GetRequiredArray(source, "scoreMovements")
            .Select(node => node!.AsObject())
            .ToArray();
        ApplyFeat156DimensionMovements(register, scoreMovements);
        ApplyFeat156ClaimDecisions(register, source);
        ApplyFeat156BlockerDecisions(register, source);
        EnsureFeat156EvidenceItems(register, scoreMovements, generatedAt, workspaceRoot);
        EnsureFeat156ScoreChanges(register, scoreMovements, generatedAt, GetIntOrDefault(GetRequiredObject(source, "scoreModel"), "baselineTotal"));
        if (internalAuditTargetScore >= InternalAudit95ReadinessPlan.TargetScore)
        {
            ApplyInternalAudit95Targets(register);
        }
    }

    private static void RemoveAlwaysBlockedClaim(JsonObject claimPolicy, string claimLevel)
    {
        if (claimPolicy["alwaysBlockedV1Claims"] is not JsonArray alwaysBlocked)
        {
            return;
        }

        var remaining = alwaysBlocked
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != claimLevel)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var replacement = new JsonArray();
        foreach (var value in remaining)
        {
            replacement.Add(value);
        }

        claimPolicy["alwaysBlockedV1Claims"] = replacement;
    }

    private static void AddAlwaysBlockedClaim(JsonObject claimPolicy, string claimLevel)
    {
        if (claimPolicy["alwaysBlockedV1Claims"] is not JsonArray alwaysBlocked)
        {
            claimPolicy["alwaysBlockedV1Claims"] = new JsonArray(claimLevel);
            return;
        }

        var existing = alwaysBlocked
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        if (!existing.Contains(claimLevel))
        {
            alwaysBlocked.Add(claimLevel);
        }
    }

    private static void ApplyFeat156DimensionMovements(JsonObject register, IReadOnlyList<JsonObject> scoreMovements)
    {
        foreach (var movement in scoreMovements)
        {
            var dimensionId = GetRequiredString(movement, "dimensionId");
            var dimension = FindDimension(register, dimensionId)
                ?? throw new ReadinessRegisterPromotionException(
                    "FEAT-156 promotion source references an unknown score dimension.",
                    [dimensionId]);
            var expectedPreviousScore = GetRequiredInt(movement, "previousScore");
            var actualPreviousScore = GetRequiredInt(dimension, "currentScore");
            if (actualPreviousScore != expectedPreviousScore)
            {
                throw new ReadinessRegisterPromotionException(
                    "FEAT-156 promotion source score movement does not match the current dimension score.",
                    [$"{dimensionId} expected {expectedPreviousScore}; found {actualPreviousScore}."]);
            }

            dimension["currentScore"] = GetRequiredInt(movement, "acceptedScore");
            AddUniqueStrings(GetRequiredArray(dimension, "evidenceIds"), GetReadinessEvidenceIds(movement));
            AddUniqueStrings(GetRequiredArray(dimension, "acceptanceGateIds"), GetStringArray(movement, "acceptanceGateIds"));
            AddUniqueStrings(GetRequiredArray(dimension, "sourceGapRows"), GetStringArray(movement, "sourceGapRows"));
            dimension["residualRisk"] = GetRequiredString(movement, "residualRisk");
            dimension["scoreRationale"] = $"{GetRequiredString(movement, "featureId")} accepted FEAT-156 promotion movement: {GetRequiredString(movement, "claimEffect")}";
        }
    }

    private static void ApplyFeat156ClaimDecisions(JsonObject register, JsonObject source)
    {
        var target = GetRequiredObject(source, "targetRegister");
        var productionClaimSource = GetRequiredObject(target, "productionClaim");
        var publicStateClaimSource = GetRequiredObject(target, "publicStateClaim");
        var productionDecision = FindSourceBlockerDecision(source, "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001")
            ?? throw new ReadinessRegisterPromotionException("FEAT-156 source is missing production blocker decision.", []);

        var productionClaim = FindClaimLevel(register, "production_organizational_rollout")
            ?? throw new ReadinessRegisterPromotionException("FEAT-156 production claim level is missing.", []);
        productionClaim["blockerSeverity"] = GetRequiredString(productionClaimSource, "severity");
        var productionStatus = GetRequiredString(productionClaimSource, "status");
        var productionWording = GetRequiredString(productionClaimSource, "wording");
        productionClaim["status"] = productionStatus;
        productionClaim["allowedWording"] = IsAllowedClaimStatus(productionStatus) ? productionWording : "";
        productionClaim["limitationWording"] = productionStatus == "future_gated"
            ? productionWording
            : GetRequiredString(productionDecision, "limitationWording");
        productionClaim["blockedWording"] = productionStatus == "blocked" ? productionWording : "";
        productionClaim["publicSafeStatus"] = GetRequiredString(productionClaimSource, "publicSafeStatus");
        productionClaim["blockerIds"] = new JsonArray("RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");

        var publicStateClaim = FindClaimLevel(register, "public_or_state_election")
            ?? throw new ReadinessRegisterPromotionException("FEAT-156 public/state claim level is missing.", []);
        var publicStateStatus = GetRequiredString(publicStateClaimSource, "status");
        var publicStateWording = GetRequiredString(publicStateClaimSource, "wording");
        publicStateClaim["blockerSeverity"] = GetRequiredString(publicStateClaimSource, "severity");
        publicStateClaim["status"] = publicStateStatus;
        publicStateClaim["allowedWording"] = IsAllowedClaimStatus(publicStateStatus) ? publicStateWording : "";
        publicStateClaim["limitationWording"] = publicStateStatus == "external_boundary" ? publicStateWording : "";
        publicStateClaim["blockedWording"] = publicStateStatus == "blocked" ? publicStateWording : "";
        publicStateClaim["publicSafeStatus"] = GetRequiredString(publicStateClaimSource, "publicSafeStatus");
        publicStateClaim["blockerIds"] = new JsonArray("RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
    }

    private static bool IsAllowedClaimStatus(string status) =>
        status is "allowed" or "allowed_with_limitations";

    private static void ApplyFeat156BlockerDecisions(JsonObject register, JsonObject source)
    {
        foreach (var decision in GetRequiredArray(source, "blockerDecisions").Select(node => node!.AsObject()))
        {
            var blockerId = GetRequiredString(decision, "blockerId");
            var blocker = FindBlocker(register, blockerId);
            if (blocker is null)
            {
                continue;
            }

            blocker["severity"] = GetRequiredString(decision, "targetSeverity");
            var targetStatus = GetRequiredString(decision, "targetStatus");
            blocker["status"] = targetStatus == "allowed_with_limitations" ? "open" : targetStatus;
            blocker["limitationWording"] = GetRequiredString(decision, "limitationWording");
            blocker["resolutionCriteria"] = GetRequiredString(decision, "residualRisk");
            if (blockerId == "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001")
            {
                blocker["featureId"] = "FEAT-156";
                if (targetStatus == "open")
                {
                    blocker["description"] = "Production organizational rollout remains blocked until the Hush-owned internal audit target reaches 95+ and the hardening work items are accepted.";
                }
                else if (targetStatus == "superseded")
                {
                    blocker["description"] = "Superseded by the Hush-owned internal audit 95+ hardening plan; production claim status is now driven by the dimension-level hardening blockers.";
                }
            }

            if (blockerId == "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001" && targetStatus == "superseded")
            {
                blocker["description"] = "Public/state election readiness is outside the Hush-owned internal audit report and is tracked as a downstream external-prerequisite boundary.";
                blocker["featureId"] = "FEAT-149";
            }
        }
    }

    private static void EnsureFeat156EvidenceItems(
        JsonObject register,
        IReadOnlyList<JsonObject> scoreMovements,
        DateTimeOffset generatedAt,
        string workspaceRoot)
    {
        var evidenceItems = GetRequiredArray(register, "evidenceItems");
        var existingEvidenceIds = evidenceItems
            .Select(node => node?.AsObject())
            .Where(node => node is not null)
            .Select(node => GetStringOrDefault(node!, "evidenceId"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var movement in scoreMovements)
        {
            foreach (var evidenceId in GetReadinessEvidenceIds(movement))
            {
                if (existingEvidenceIds.Contains(evidenceId))
                {
                    continue;
                }

                evidenceItems.Add(BuildFeat156EvidenceItem(movement, evidenceId, generatedAt, workspaceRoot));
                existingEvidenceIds.Add(evidenceId);
            }
        }
    }

    private static JsonObject BuildFeat156EvidenceItem(
        JsonObject movement,
        string evidenceId,
        DateTimeOffset generatedAt,
        string workspaceRoot)
    {
        var featureId = GetRequiredString(movement, "featureId");
        var dimensionId = GetRequiredString(movement, "dimensionId");
        var sourceGapRow = GetStringArray(movement, "sourceGapRows").FirstOrDefault() ?? "Production rollout promotion";
        return new JsonObject
        {
            ["evidenceId"] = evidenceId,
            ["parentEpic"] = "EPIC-015",
            ["featureId"] = featureId,
            ["sourceGapRow"] = sourceGapRow,
            ["acceptanceGateIds"] = CloneStringArray(GetRequiredArray(movement, "acceptanceGateIds")),
            ["dimensionIds"] = new JsonArray(dimensionId),
            ["electionScope"] = "not_election_specific",
            ["releaseScope"] = "production-rollout-promotion-v0.1.6",
            ["visibility"] = "restricted_reviewer",
            ["status"] = "accepted",
            ["producedAt"] = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["owner"] = "HushVoting readiness owner",
            ["artifactRefs"] = BuildFeat156EvidenceArtifactRefs(movement, workspaceRoot),
            ["checkResults"] = new JsonArray(
                new JsonObject
                {
                    ["checkId"] = $"CHK-FEAT156-{dimensionId}",
                    ["status"] = "pass",
                    ["summary"] = GetRequiredString(movement, "claimEffect"),
                    ["detailsRef"] = GetRequiredString(movement, "movementId"),
                }),
            ["freshness"] = new JsonObject
            {
                ["state"] = "current",
                ["invalidationRule"] = "Event-based invalidation when the FEAT-156 production rollout promotion source or any referenced source feature evidence changes.",
                ["staleReason"] = "",
                ["timeSensitive"] = false,
            },
            ["residualRisk"] = GetRequiredString(movement, "residualRisk"),
            ["claimEffect"] = "score_increase",
            ["signoffs"] = CreateFeat156Signoffs(featureId, dimensionId, generatedAt),
            ["relatedExceptionIds"] = new JsonArray(),
            ["relatedBlockerIds"] = GetFeat156RelatedBlockers(dimensionId),
        };
    }

    private static JsonArray BuildFeat156EvidenceArtifactRefs(JsonObject movement, string workspaceRoot)
    {
        var result = new JsonArray();
        foreach (var artifactRef in GetRequiredArray(movement, "artifactRefs").Select(node => node!.AsObject()))
        {
            var relativePath = GetStringOrDefault(artifactRef, "path");
            var fullPath = string.IsNullOrWhiteSpace(relativePath)
                ? string.Empty
                : Path.Combine(workspaceRoot, relativePath);
            result.Add(new JsonObject
            {
                ["artifactId"] = GetRequiredString(artifactRef, "artifactId"),
                ["relativePath"] = relativePath,
                ["hashAlgorithm"] = "SHA-256",
                ["sha256Hash"] = NormalizeSha256(GetRequiredString(artifactRef, "sha256Hash")),
                ["mediaType"] = GuessMediaType(relativePath),
                ["sizeBytes"] = GetArtifactSizeBytes(fullPath),
                ["visibility"] = "restricted_reviewer",
            });
        }

        return result;
    }

    private static void EnsureFeat156ScoreChanges(
        JsonObject register,
        IReadOnlyList<JsonObject> scoreMovements,
        DateTimeOffset generatedAt,
        int baselineTotal)
    {
        var scoreChanges = GetRequiredArray(register, "scoreChanges");
        var existingScoreChangeIds = scoreChanges
            .Select(node => node?.AsObject())
            .Where(node => node is not null)
            .Select(node => GetStringOrDefault(node!, "scoreChangeId"))
            .ToHashSet(StringComparer.Ordinal);
        var runningTotal = baselineTotal;
        var index = 1;
        foreach (var movement in scoreMovements)
        {
            var scoreChangeId = $"RDY-SCORE-20260531-{index:000}";
            var delta = GetRequiredInt(movement, "delta");
            if (!existingScoreChangeIds.Contains(scoreChangeId))
            {
                scoreChanges.Add(BuildFeat156ScoreChange(movement, scoreChangeId, generatedAt, runningTotal, runningTotal + delta));
                existingScoreChangeIds.Add(scoreChangeId);
            }

            runningTotal += delta;
            index++;
        }
    }

    private static JsonObject BuildFeat156ScoreChange(
        JsonObject movement,
        string scoreChangeId,
        DateTimeOffset generatedAt,
        int previousTotal,
        int acceptedTotal)
    {
        var dimensionId = GetRequiredString(movement, "dimensionId");
        var featureId = GetRequiredString(movement, "featureId");
        var acceptedScore = GetRequiredInt(movement, "acceptedScore");
        return new JsonObject
        {
            ["scoreChangeId"] = scoreChangeId,
            ["dimensionId"] = dimensionId,
            ["direction"] = "increase",
            ["previousScore"] = GetRequiredInt(movement, "previousScore"),
            ["proposedScore"] = acceptedScore,
            ["acceptedScore"] = acceptedScore,
            ["evidenceIds"] = ToJsonArray(GetReadinessEvidenceIds(movement)),
            ["sourceGapRow"] = GetStringArray(movement, "sourceGapRows").FirstOrDefault() ?? "Production rollout promotion",
            ["acceptanceGateIds"] = CloneStringArray(GetRequiredArray(movement, "acceptanceGateIds")),
            ["blockerImpactBefore"] = GetFeat156RelatedBlockers(dimensionId),
            ["blockerImpactAfter"] = GetFeat156RelatedBlockers(dimensionId),
            ["claimImpact"] = GetRequiredString(movement, "claimEffect"),
            ["reason"] = $"{featureId} accepted evidence is consumed by FEAT-156 to promote RDY-REG-v0.1.6 with production rollout limitations.",
            ["generatedDiff"] = $"{dimensionId} currentScore {GetRequiredInt(movement, "previousScore")} -> {acceptedScore}; total score {previousTotal} -> {acceptedTotal}.",
            ["signoffs"] = CreateFeat156Signoffs(featureId, dimensionId, generatedAt),
        };
    }

    private static JsonObject CreateFeat156Signoffs(string featureId, string dimensionId, DateTimeOffset generatedAt)
    {
        var signedAt = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var basis = $"Accepted {featureId} evidence for FEAT-156 {dimensionId} production rollout limited promotion.";
        return new JsonObject
        {
            ["engineering"] = new JsonObject
            {
                ["role"] = "engineering",
                ["signerId"] = "paulo-aboim-pinto",
                ["signerName"] = "Paulo Aboim Pinto",
                ["signedAt"] = signedAt,
                ["basis"] = basis,
                ["samePersonTwoHat"] = true,
            },
            ["operationsProduct"] = new JsonObject
            {
                ["role"] = "operations_product",
                ["signerId"] = "paulo-aboim-pinto",
                ["signerName"] = "Paulo Aboim Pinto",
                ["signedAt"] = signedAt,
                ["basis"] = basis,
                ["samePersonTwoHat"] = true,
            },
        };
    }

    private static JsonArray GetFeat156RelatedBlockers(string dimensionId)
    {
        var blockers = new JsonArray("RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");
        if (dimensionId == "RDY-DIM-010")
        {
            blockers.Add("RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
        }

        return blockers;
    }

    private static void ApplyInternalAudit95PromotionSource(
        JsonObject register,
        JsonObject source,
        DateTimeOffset generatedAt,
        string workspaceRoot)
    {
        var target = GetRequiredObject(source, "targetRegister");
        register["registerVersion"] = GetRequiredString(target, "registerVersion");
        register["registerVersionId"] = GetRequiredString(target, "registerVersionId");
        register["status"] = GetRequiredString(target, "status");
        register["promotedAt"] = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        register["sourceCommit"] = GetRequiredString(source, "sourceId");

        var score = GetRequiredObject(register, "score");
        score["total"] = GetRequiredInt(target, "totalScore");
        score["strongerTargetScore"] = InternalAudit95ReadinessPlan.TargetScore;

        var claimPolicy = GetRequiredObject(register, "claimPolicy");
        claimPolicy["strongerTargetScore"] = InternalAudit95ReadinessPlan.TargetScore;
        claimPolicy["strongestAllowedV1Claim"] = GetRequiredString(target, "strongestAllowedClaim");
        AddAlwaysBlockedClaim(claimPolicy, "production_organizational_rollout");
        AddAlwaysBlockedClaim(claimPolicy, "public_or_state_election");

        GetRequiredObject(register, "generatedViews")["publicSafePublicationStatus"] =
            GetRequiredString(target, "publicationStatus");

        var movements = GetRequiredArray(source, "scoreMovements")
            .Select(node => node!.AsObject())
            .OrderBy(movement => Array.FindIndex(
                InternalAudit95ReadinessPlan.Tasks,
                task => task.DimensionId == GetRequiredString(movement, "dimensionId")))
            .ToArray();
        ApplyInternalAudit95DimensionMovements(register, movements);
        ApplyInternalAudit95ClaimState(register, source);
        EnsureInternalAudit95ClaimProfiles(register);
        ApplyInternalAudit95BlockerResolutions(register, movements);
        EnsureInternalAudit95EvidenceItems(register, movements, generatedAt, workspaceRoot);
        EnsureInternalAudit95ScoreChanges(register, movements, generatedAt, baselineTotal: 80);
    }

    private static void EnsureInternalAudit95ClaimProfiles(JsonObject register)
    {
        register["claimProfiles"] = BuildInternalAudit95ClaimProfiles();
    }

    private static JsonArray BuildInternalAudit95ClaimProfiles() =>
        new()
        {
            BuildClaimProfile(
                "hushvoting.direct.non_binding",
                "Non-Binding HushVoting! Direct",
                "HushVoting! Direct",
                "non_binding",
                "Non-Binding",
                true,
                "direct",
                "standard",
                "green",
                "passed",
                "internal_non_binding_rehearsal",
                "Product mode HushVoting! Direct, binding status Non-Binding, and isNonBindingElection true pass the internal technical machine claim profile gate.",
                "The pass is limited to runtime/profile evidence and internal audit use. SP-10 access-control, backup/restore, and auditor-room controls are pre-production/production checklist items split into machine and human sub-checklists; no customer, production, public/state, legal, certification, independent-validation, deployment/build completeness, or web-client proof-binding claim is made.",
                [
                    "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Non-Binding-20260605102141/audit-boundary-note.md",
                    "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Non-Binding-20260605102141/public-verification-package/artifacts/report-package/canonical-manifest.json",
                    "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Non-Binding-20260605102141/public-verification-package/artifacts/report-package/result-report.json",
                    DirectNonBindingCurrentVerifierOutputRef,
                ],
                ["productMode == HushVoting! Direct", "bindingStatus == Non-Binding", "isNonBindingElection == true", "SP-10 operational controls deferred to pre-production/production checklists"]),
            BuildClaimProfile(
                "hushvoting.direct.binding",
                "Binding HushVoting! Direct",
                "HushVoting! Direct",
                "binding",
                "Binding",
                false,
                "direct",
                "standard",
                "green",
                "passed",
                "internal_non_binding_rehearsal",
                "Product mode HushVoting! Direct, binding status Binding, and isNonBindingElection false pass the internal technical machine claim profile gate.",
                "The pass is limited to runtime/profile evidence and internal audit use. SP-10 access-control, backup/restore, and auditor-room controls are pre-production/production checklist items split into machine and human sub-checklists; no customer, production, public/state, legal, certification, independent-validation, deployment/build completeness, or web-client proof-binding claim is made.",
                [
                    "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Rehearsal-II-20260604215137/audit-boundary-note.md",
                    "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Rehearsal-II-20260604215137/public-verification-package/artifacts/report-package/canonical-manifest.json",
                    "hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Rehearsal-II-20260604215137/public-verification-package/artifacts/report-package/result-report.json",
                    DirectBindingCurrentVerifierOutputRef,
                ],
                ["productMode == HushVoting! Direct", "bindingStatus == Binding", "isNonBindingElection == false", "SP-10 operational controls deferred to pre-production/production checklists"]),
            BuildClaimProfile(
                "hushvoting.veritas_3_of_5.non_binding",
                "Non-Binding HushVoting! Veritas 3/5",
                "HushVoting! Veritas",
                "non_binding",
                "Non-Binding",
                true,
                "3/5",
                "standard",
                "amber",
                "not_observed",
                "internal_non_binding_rehearsal",
                "The non-binding Veritas 3/5 profile is tracked, but no accepted runtime rehearsal evidence is bound to it in the current accepted evidence baseline.",
                "Requires a Veritas 3/5 threshold ceremony, trustee evidence, bindingStatus Non-Binding, and isNonBindingElection true.",
                [],
                ["productMode == HushVoting! Veritas", "thresholdProfile == 3/5", "bindingStatus == Non-Binding", "isNonBindingElection == true"]),
            BuildClaimProfile(
                "hushvoting.veritas_3_of_5.binding",
                "Binding HushVoting! Veritas 3/5",
                "HushVoting! Veritas",
                "binding",
                "Binding",
                false,
                "3/5",
                "standard",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "The binding Veritas 3/5 profile is tracked as a standard future profile, but the current accepted evidence baseline does not claim it as passed.",
                "Requires accepted Veritas 3/5 threshold ceremony evidence plus customer governance and downstream production-context gates before stronger claims.",
                [],
                ["productMode == HushVoting! Veritas", "thresholdProfile == 3/5", "bindingStatus == Binding", "isNonBindingElection == false"]),
            BuildClaimProfile(
                "hushvoting.veritas_7_of_10.non_binding",
                "Non-Binding HushVoting! Veritas 7/10",
                "HushVoting! Veritas",
                "non_binding",
                "Non-Binding",
                true,
                "7/10",
                "standard",
                "amber",
                "not_observed",
                "internal_non_binding_rehearsal",
                "The non-binding Veritas 7/10 profile is tracked, but no accepted runtime rehearsal evidence is bound to it in the current accepted evidence baseline.",
                "Requires a Veritas 7/10 threshold ceremony, trustee evidence, bindingStatus Non-Binding, and isNonBindingElection true.",
                [],
                ["productMode == HushVoting! Veritas", "thresholdProfile == 7/10", "bindingStatus == Non-Binding", "isNonBindingElection == true"]),
            BuildClaimProfile(
                "hushvoting.veritas_7_of_10.binding",
                "Binding HushVoting! Veritas 7/10",
                "HushVoting! Veritas",
                "binding",
                "Binding",
                false,
                "7/10",
                "standard",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "The binding Veritas 7/10 profile is tracked as a standard future profile, but the current accepted evidence baseline does not claim it as passed.",
                "Requires accepted Veritas 7/10 threshold ceremony evidence plus customer governance and downstream production-context gates before stronger claims.",
                [],
                ["productMode == HushVoting! Veritas", "thresholdProfile == 7/10", "bindingStatus == Binding", "isNonBindingElection == false"]),
            BuildClaimProfile(
                "hushvoting.veritas_8_of_13.non_binding",
                "Non-Binding HushVoting! Veritas 8/13",
                "HushVoting! Veritas",
                "non_binding",
                "Non-Binding",
                true,
                "8/13",
                "standard",
                "amber",
                "not_observed",
                "internal_non_binding_rehearsal",
                "The non-binding Veritas 8/13 profile is tracked, but no accepted runtime rehearsal evidence is bound to it in the current accepted evidence baseline.",
                "Requires a Veritas 8/13 threshold ceremony, trustee evidence, bindingStatus Non-Binding, and isNonBindingElection true.",
                [],
                ["productMode == HushVoting! Veritas", "thresholdProfile == 8/13", "bindingStatus == Non-Binding", "isNonBindingElection == true"]),
            BuildClaimProfile(
                "hushvoting.veritas_8_of_13.binding",
                "Binding HushVoting! Veritas 8/13",
                "HushVoting! Veritas",
                "binding",
                "Binding",
                false,
                "8/13",
                "standard",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "The binding Veritas 8/13 profile is tracked as a standard future profile, but the current accepted evidence baseline does not claim it as passed.",
                "Requires accepted Veritas 8/13 threshold ceremony evidence plus customer governance and downstream production-context gates before stronger claims.",
                [],
                ["productMode == HushVoting! Veritas", "thresholdProfile == 8/13", "bindingStatus == Binding", "isNonBindingElection == false"]),
            BuildClaimProfile(
                "hushvoting.enterprise_n_of_k.non_binding",
                "Non-Binding HushVoting! Enterprise n/k",
                "HushVoting! Enterprise",
                "non_binding",
                "Non-Binding",
                true,
                "n/k",
                "enterprise",
                "amber",
                "future_gated",
                "internal_non_binding_rehearsal",
                "The non-binding Enterprise n/k profile is tracked for thresholds outside the standard profiles, but the current accepted evidence baseline does not claim it as passed.",
                "Requires explicit threshold rationale, accepted custom-profile evidence, bindingStatus Non-Binding, and isNonBindingElection true.",
                [],
                ["productMode == HushVoting! Enterprise", "thresholdProfile == n/k", "bindingStatus == Non-Binding", "isNonBindingElection == true", "custom threshold evidence accepted"]),
            BuildClaimProfile(
                "hushvoting.enterprise_n_of_k.binding",
                "Binding HushVoting! Enterprise n/k",
                "HushVoting! Enterprise",
                "binding",
                "Binding",
                false,
                "n/k",
                "enterprise",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "The binding Enterprise n/k profile is tracked for thresholds outside the standard profiles, but the current accepted evidence baseline does not claim it as passed.",
                "Requires explicit threshold rationale, accepted custom-profile evidence, customer governance, bindingStatus Binding, isNonBindingElection false, and downstream production-context gates.",
                [],
                ["productMode == HushVoting! Enterprise", "thresholdProfile == n/k", "bindingStatus == Binding", "isNonBindingElection == false", "custom threshold evidence accepted"]),
        };

    private static JsonArray BuildInternalAudit95ExampleClaimProfiles() =>
        new()
        {
            BuildClaimProfile(
                "hushvoting.direct.non_binding",
                "Non-Binding HushVoting! Direct",
                "HushVoting! Direct",
                "non_binding",
                "Non-Binding",
                true,
                "direct",
                "standard",
                "green",
                "passed",
                "internal_non_binding_rehearsal",
                "Example non-binding Direct profile gate pass.",
                "The profile pass is limited by the broad claim level.",
                ["example/non-binding-direct-audit-boundary-note.md"],
                ["productMode == HushVoting! Direct", "bindingStatus == Non-Binding", "isNonBindingElection == true"]),
            BuildClaimProfile(
                "hushvoting.direct.binding",
                "Binding HushVoting! Direct",
                "HushVoting! Direct",
                "binding",
                "Binding",
                false,
                "direct",
                "standard",
                "green",
                "passed",
                "internal_non_binding_rehearsal",
                "Example Direct binding profile gate pass.",
                "The profile pass is limited by the broad claim level.",
                ["example/audit-boundary-note.md"],
                ["productMode == HushVoting! Direct", "bindingStatus == Binding", "isNonBindingElection == false"]),
            BuildClaimProfile(
                "hushvoting.veritas_3_of_5.non_binding",
                "Non-Binding HushVoting! Veritas 3/5",
                "HushVoting! Veritas",
                "non_binding",
                "Non-Binding",
                true,
                "3/5",
                "standard",
                "amber",
                "not_observed",
                "internal_non_binding_rehearsal",
                "Example Veritas 3/5 non-binding profile gate.",
                "Requires matching threshold evidence.",
                [],
                ["thresholdProfile == 3/5"]),
            BuildClaimProfile(
                "hushvoting.veritas_3_of_5.binding",
                "Binding HushVoting! Veritas 3/5",
                "HushVoting! Veritas",
                "binding",
                "Binding",
                false,
                "3/5",
                "standard",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "Example Veritas 3/5 binding profile gate.",
                "Requires matching threshold and governance evidence.",
                [],
                ["thresholdProfile == 3/5"]),
            BuildClaimProfile(
                "hushvoting.veritas_7_of_10.non_binding",
                "Non-Binding HushVoting! Veritas 7/10",
                "HushVoting! Veritas",
                "non_binding",
                "Non-Binding",
                true,
                "7/10",
                "standard",
                "amber",
                "not_observed",
                "internal_non_binding_rehearsal",
                "Example Veritas 7/10 non-binding profile gate.",
                "Requires matching threshold evidence.",
                [],
                ["thresholdProfile == 7/10"]),
            BuildClaimProfile(
                "hushvoting.veritas_7_of_10.binding",
                "Binding HushVoting! Veritas 7/10",
                "HushVoting! Veritas",
                "binding",
                "Binding",
                false,
                "7/10",
                "standard",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "Example Veritas 7/10 binding profile gate.",
                "Requires matching threshold and governance evidence.",
                [],
                ["thresholdProfile == 7/10"]),
            BuildClaimProfile(
                "hushvoting.veritas_8_of_13.non_binding",
                "Non-Binding HushVoting! Veritas 8/13",
                "HushVoting! Veritas",
                "non_binding",
                "Non-Binding",
                true,
                "8/13",
                "standard",
                "amber",
                "not_observed",
                "internal_non_binding_rehearsal",
                "Example Veritas 8/13 non-binding profile gate.",
                "Requires matching threshold evidence.",
                [],
                ["thresholdProfile == 8/13"]),
            BuildClaimProfile(
                "hushvoting.veritas_8_of_13.binding",
                "Binding HushVoting! Veritas 8/13",
                "HushVoting! Veritas",
                "binding",
                "Binding",
                false,
                "8/13",
                "standard",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "Example Veritas 8/13 binding profile gate.",
                "Requires matching threshold and governance evidence.",
                [],
                ["thresholdProfile == 8/13"]),
            BuildClaimProfile(
                "hushvoting.enterprise_n_of_k.non_binding",
                "Non-Binding HushVoting! Enterprise n/k",
                "HushVoting! Enterprise",
                "non_binding",
                "Non-Binding",
                true,
                "n/k",
                "enterprise",
                "amber",
                "future_gated",
                "internal_non_binding_rehearsal",
                "Example Enterprise n/k non-binding profile gate.",
                "Requires custom threshold evidence.",
                [],
                ["thresholdProfile == n/k"]),
            BuildClaimProfile(
                "hushvoting.enterprise_n_of_k.binding",
                "Binding HushVoting! Enterprise n/k",
                "HushVoting! Enterprise",
                "binding",
                "Binding",
                false,
                "n/k",
                "enterprise",
                "amber",
                "future_gated",
                "production_organizational_rollout",
                "Example Enterprise n/k binding profile gate.",
                "Requires custom threshold and governance evidence.",
                [],
                ["thresholdProfile == n/k"]),
        };

    private static JsonObject BuildClaimProfile(
        string profileId,
        string label,
        string productMode,
        string governanceEffect,
        string bindingStatus,
        bool isNonBindingElection,
        string thresholdProfile,
        string profileClass,
        string gateSeverity,
        string gateStatus,
        string claimLevel,
        string claimWording,
        string limitationWording,
        IReadOnlyList<string> evidenceRefs,
        IReadOnlyList<string> requiredEvidence,
        IReadOnlyList<ClaimProfileVerifierWarning>? verifierWarnings = null)
    {
        var verifierWarningArray = ToClaimProfileVerifierWarningsArray(verifierWarnings);
        return new()
        {
            ["profileId"] = profileId,
            ["label"] = label,
            ["productMode"] = productMode,
            ["governanceEffect"] = governanceEffect,
            ["bindingStatus"] = bindingStatus,
            ["isNonBindingElection"] = isNonBindingElection,
            ["thresholdProfile"] = thresholdProfile,
            ["profileClass"] = profileClass,
            ["gateSeverity"] = gateSeverity,
            ["gateStatus"] = gateStatus,
            ["claimLevel"] = claimLevel,
            ["claimWording"] = claimWording,
            ["limitationWording"] = limitationWording,
            ["evidenceRefs"] = ToJsonArray(evidenceRefs),
            ["requiredEvidence"] = ToJsonArray(requiredEvidence),
            ["verifierWarningCount"] = verifierWarningArray.Count,
            ["verifierWarnings"] = verifierWarningArray,
        };
    }

    private static JsonArray ToClaimProfileVerifierWarningsArray(
        IReadOnlyList<ClaimProfileVerifierWarning>? verifierWarnings)
    {
        var result = new JsonArray();
        foreach (var warning in verifierWarnings ?? [])
        {
            result.Add(new JsonObject
            {
                ["checkCode"] = warning.CheckCode,
                ["resultCode"] = warning.ResultCode,
                ["message"] = warning.Message,
                ["evidenceRef"] = warning.EvidenceRef,
            });
        }

        return result;
    }

    private static void ApplyInternalAudit95DimensionMovements(JsonObject register, IReadOnlyList<JsonObject> movements)
    {
        foreach (var movement in movements)
        {
            var dimensionId = GetRequiredString(movement, "dimensionId");
            var dimension = FindDimension(register, dimensionId)
                ?? throw new ReadinessRegisterPromotionException(
                    "Internal audit 95 promotion source references an unknown score dimension.",
                    [dimensionId]);
            var expectedPreviousScore = GetRequiredInt(movement, "previousScore");
            var actualPreviousScore = GetRequiredInt(dimension, "currentScore");
            if (actualPreviousScore != expectedPreviousScore)
            {
                throw new ReadinessRegisterPromotionException(
                    "Internal audit 95 promotion source score movement does not match the current dimension score.",
                    [$"{dimensionId} expected {expectedPreviousScore}; found {actualPreviousScore}."]);
            }

            dimension["currentScore"] = GetRequiredInt(movement, "acceptedScore");
            AddUniqueStrings(GetRequiredArray(dimension, "evidenceIds"), GetReadinessEvidenceIds(movement));
            AddUniqueStrings(GetRequiredArray(dimension, "acceptanceGateIds"), GetStringArray(movement, "acceptanceGateIds"));
            AddUniqueStrings(GetRequiredArray(dimension, "sourceGapRows"), GetStringArray(movement, "sourceGapRows"));
            dimension["residualRisk"] = GetRequiredString(movement, "residualRisk");
            dimension["scoreRationale"] = $"{GetRequiredString(movement, "featureId")} accepted internal-audit-95 promotion movement: {GetRequiredString(movement, "claimEffect")}";
        }
    }

    private static void ApplyInternalAudit95ClaimState(JsonObject register, JsonObject source)
    {
        var target = GetRequiredObject(source, "targetRegister");
        var internalRehearsalClaim = FindClaimLevel(register, "internal_non_binding_rehearsal")
            ?? throw new ReadinessRegisterPromotionException("Internal audit 95 internal rehearsal claim level is missing.", []);
        internalRehearsalClaim["blockerSeverity"] = "amber";
        internalRehearsalClaim["status"] = "allowed_with_limitations";
        internalRehearsalClaim["allowedWording"] =
            "HushVoting may use internal technical rehearsal evidence when product-mode profile limitations and stronger claim boundaries remain visible.";
        internalRehearsalClaim["limitationWording"] =
            "Internal rehearsal evidence is not a customer, production, public/state, legal, certification, or independent-validation claim; runtime binding status is represented by the HushVoting claim profile gates.";
        internalRehearsalClaim["blockedWording"] = "";
        internalRehearsalClaim["publicSafeStatus"] = "not_for_publication";
        internalRehearsalClaim["blockerIds"] = new JsonArray();

        var friendlyPilotClaim = FindClaimLevel(register, "friendly_organization_pilot")
            ?? throw new ReadinessRegisterPromotionException("Internal audit 95 friendly pilot claim level is missing.", []);
        friendlyPilotClaim["blockerSeverity"] = "amber";
        friendlyPilotClaim["status"] = "allowed_with_limitations";
        friendlyPilotClaim["allowedWording"] =
            "HushVoting may be discussed for controlled friendly-organization pilot planning when limitations remain explicit and private readiness review is available.";
        friendlyPilotClaim["limitationWording"] =
            "Friendly-pilot readiness is a broad claim boundary. It does not change which product-mode profile gates have passed, and it does not claim production rollout, public/state election readiness, independent validation, or legal sufficiency.";
        friendlyPilotClaim["blockedWording"] = "";
        friendlyPilotClaim["publicSafeStatus"] = "pilot_only_with_limitations";
        friendlyPilotClaim["blockerIds"] = new JsonArray();

        var productionClaim = FindClaimLevel(register, "production_organizational_rollout")
            ?? throw new ReadinessRegisterPromotionException("Internal audit 95 production claim level is missing.", []);
        productionClaim["blockerSeverity"] = "amber";
        productionClaim["status"] = "future_gated";
        productionClaim["allowedWording"] = "";
        productionClaim["limitationWording"] = GetRequiredString(target, "productionFutureGateWording");
        productionClaim["blockedWording"] = "";
        productionClaim["publicSafeStatus"] = "not_ready_for_public_claim";
        productionClaim["blockerIds"] = new JsonArray();

        var publicStateClaim = FindClaimLevel(register, "public_or_state_election")
            ?? throw new ReadinessRegisterPromotionException("Internal audit 95 public/state claim level is missing.", []);
        publicStateClaim["blockerSeverity"] = "amber";
        publicStateClaim["status"] = "external_boundary";
        publicStateClaim["allowedWording"] = "";
        publicStateClaim["limitationWording"] = GetRequiredString(target, "publicStateExternalBoundaryWording");
        publicStateClaim["blockedWording"] = "";
        publicStateClaim["publicSafeStatus"] = "public_claim_blocked";
        publicStateClaim["blockerIds"] = new JsonArray();
    }

    private static void ApplyInternalAudit95BlockerResolutions(JsonObject register, IReadOnlyList<JsonObject> movements)
    {
        foreach (var movement in movements)
        {
            var blockerId = GetRequiredString(movement, "targetBlockerId");
            var blocker = FindBlocker(register, blockerId)
                ?? throw new ReadinessRegisterPromotionException(
                    "Internal audit 95 promotion source references an unknown blocker.",
                    [blockerId]);
            blocker["severity"] = "green";
            blocker["status"] = "resolved";
            blocker["limitationWording"] = "";
            blocker["resolutionCriteria"] = GetRequiredString(movement, "resolutionCriteria");
        }

        var productionBlocker = FindBlocker(register, "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");
        if (productionBlocker is not null)
        {
            productionBlocker["severity"] = "green";
            productionBlocker["status"] = "superseded";
            productionBlocker["resolutionCriteria"] =
                "The Hush-owned internal audit 95 target was promoted in RDY-REG-v0.1.8; production rollout remains a downstream execution gate for local rehearsal, binding-election proof validation, customer governance, and external review.";
        }

        var publicStateBlocker = FindBlocker(register, "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
        if (publicStateBlocker is not null)
        {
            publicStateBlocker["severity"] = "green";
            publicStateBlocker["status"] = "superseded";
            publicStateBlocker["resolutionCriteria"] =
                "Public/state election readiness remains outside the Hush-owned internal audit report and requires external authority, jurisdiction, certification, procurement, accessibility, transparency, and dispute-remedy prerequisites.";
        }
    }

    private static void EnsureInternalAudit95EvidenceItems(
        JsonObject register,
        IReadOnlyList<JsonObject> movements,
        DateTimeOffset generatedAt,
        string workspaceRoot)
    {
        var evidenceItems = GetRequiredArray(register, "evidenceItems");
        var existingEvidenceIds = evidenceItems
            .Select(node => node?.AsObject())
            .Where(node => node is not null)
            .Select(node => GetStringOrDefault(node!, "evidenceId"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var movement in movements)
        {
            foreach (var evidenceId in GetReadinessEvidenceIds(movement))
            {
                if (existingEvidenceIds.Contains(evidenceId))
                {
                    continue;
                }

                evidenceItems.Add(BuildInternalAudit95EvidenceItem(movement, evidenceId, generatedAt, workspaceRoot));
                existingEvidenceIds.Add(evidenceId);
            }
        }
    }

    private static JsonObject BuildInternalAudit95EvidenceItem(
        JsonObject movement,
        string evidenceId,
        DateTimeOffset generatedAt,
        string workspaceRoot)
    {
        var featureId = GetRequiredString(movement, "featureId");
        var dimensionId = GetRequiredString(movement, "dimensionId");
        return new JsonObject
        {
            ["evidenceId"] = evidenceId,
            ["parentEpic"] = "EPIC-015",
            ["featureId"] = featureId,
            ["sourceGapRow"] = GetStringArray(movement, "sourceGapRows").FirstOrDefault() ?? "Internal audit 95 hardening",
            ["acceptanceGateIds"] = CloneStringArray(GetRequiredArray(movement, "acceptanceGateIds")),
            ["dimensionIds"] = new JsonArray(dimensionId),
            ["electionScope"] = "not_election_specific",
            ["releaseScope"] = "internal-audit-95-promotion-v0.1.8",
            ["visibility"] = "restricted_reviewer",
            ["status"] = "accepted",
            ["producedAt"] = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["owner"] = "HushVoting readiness owner",
            ["artifactRefs"] = BuildInternalAudit95EvidenceArtifactRefs(movement, workspaceRoot),
            ["checkResults"] = new JsonArray(
                new JsonObject
                {
                    ["checkId"] = $"CHK-RDY-IA95-{dimensionId}",
                    ["status"] = "pass",
                    ["summary"] = GetRequiredString(movement, "claimEffect"),
                    ["detailsRef"] = GetRequiredString(movement, "movementId"),
                }),
            ["freshness"] = new JsonObject
            {
                ["state"] = "current",
                ["invalidationRule"] = "Event-based invalidation when the internal-audit-95 promotion source or referenced score proposal changes.",
                ["staleReason"] = "",
                ["timeSensitive"] = false,
            },
            ["residualRisk"] = GetRequiredString(movement, "residualRisk"),
            ["claimEffect"] = "score_increase",
            ["signoffs"] = CreateInternalAudit95Signoffs(featureId, dimensionId, generatedAt),
            ["relatedExceptionIds"] = new JsonArray(),
            ["relatedBlockerIds"] = new JsonArray(GetRequiredString(movement, "targetBlockerId")),
        };
    }

    private static JsonArray BuildInternalAudit95EvidenceArtifactRefs(JsonObject movement, string workspaceRoot)
    {
        var result = new JsonArray();
        foreach (var artifactRef in GetRequiredArray(movement, "artifactRefs").Select(node => node!.AsObject()))
        {
            var relativePath = GetRequiredString(artifactRef, "path");
            var fullPath = Path.Combine(workspaceRoot, relativePath);
            result.Add(new JsonObject
            {
                ["artifactId"] = GetRequiredString(artifactRef, "artifactId"),
                ["relativePath"] = relativePath,
                ["hashAlgorithm"] = "SHA-256",
                ["sha256Hash"] = NormalizeSha256(GetRequiredString(artifactRef, "sha256Hash")),
                ["mediaType"] = GuessMediaType(relativePath),
                ["sizeBytes"] = GetArtifactSizeBytes(fullPath),
                ["visibility"] = GetStringOrDefault(artifactRef, "visibility") switch
                {
                    "public" or "public_safe" => "public_safe",
                    "internal" => "internal",
                    _ => "restricted_reviewer",
                },
            });
        }

        return result;
    }

    private static void EnsureInternalAudit95ScoreChanges(
        JsonObject register,
        IReadOnlyList<JsonObject> movements,
        DateTimeOffset generatedAt,
        int baselineTotal)
    {
        var scoreChanges = GetRequiredArray(register, "scoreChanges");
        var existingScoreChangeIds = scoreChanges
            .Select(node => node?.AsObject())
            .Where(node => node is not null)
            .Select(node => GetStringOrDefault(node!, "scoreChangeId"))
            .ToHashSet(StringComparer.Ordinal);
        var runningTotal = baselineTotal;
        var index = 1;
        var datePrefix = generatedAt.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var movement in movements)
        {
            var scoreChangeId = $"RDY-SCORE-{datePrefix}-{index:000}";
            var delta = GetRequiredInt(movement, "delta");
            if (!existingScoreChangeIds.Contains(scoreChangeId))
            {
                scoreChanges.Add(BuildInternalAudit95ScoreChange(movement, scoreChangeId, generatedAt, runningTotal, runningTotal + delta));
                existingScoreChangeIds.Add(scoreChangeId);
            }

            runningTotal += delta;
            index++;
        }
    }

    private static JsonObject BuildInternalAudit95ScoreChange(
        JsonObject movement,
        string scoreChangeId,
        DateTimeOffset generatedAt,
        int previousTotal,
        int acceptedTotal)
    {
        var dimensionId = GetRequiredString(movement, "dimensionId");
        var featureId = GetRequiredString(movement, "featureId");
        var acceptedScore = GetRequiredInt(movement, "acceptedScore");
        var blockerId = GetRequiredString(movement, "targetBlockerId");
        return new JsonObject
        {
            ["scoreChangeId"] = scoreChangeId,
            ["dimensionId"] = dimensionId,
            ["direction"] = "increase",
            ["previousScore"] = GetRequiredInt(movement, "previousScore"),
            ["proposedScore"] = acceptedScore,
            ["acceptedScore"] = acceptedScore,
            ["evidenceIds"] = ToJsonArray(GetReadinessEvidenceIds(movement)),
            ["sourceGapRow"] = GetStringArray(movement, "sourceGapRows").FirstOrDefault() ?? "Internal audit 95 hardening",
            ["acceptanceGateIds"] = CloneStringArray(GetRequiredArray(movement, "acceptanceGateIds")),
            ["blockerImpactBefore"] = new JsonArray(blockerId),
            ["blockerImpactAfter"] = new JsonArray(),
            ["claimImpact"] = GetRequiredString(movement, "claimEffect"),
            ["reason"] = $"{featureId} accepted evidence is consumed by the internal-audit-95 promotion to promote RDY-REG-v0.1.8.",
            ["generatedDiff"] = $"{dimensionId} currentScore {GetRequiredInt(movement, "previousScore")} -> {acceptedScore}; total score {previousTotal} -> {acceptedTotal}.",
            ["signoffs"] = CreateInternalAudit95Signoffs(featureId, dimensionId, generatedAt),
        };
    }

    private static JsonObject CreateInternalAudit95Signoffs(string featureId, string dimensionId, DateTimeOffset generatedAt)
    {
        var signedAt = generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var basis = $"Accepted {featureId} evidence for RDY-REG-v0.1.8 internal-audit-95 {dimensionId} promotion.";
        return new JsonObject
        {
            ["engineering"] = new JsonObject
            {
                ["role"] = "engineering",
                ["signerId"] = "paulo-aboim-pinto",
                ["signerName"] = "Paulo Aboim Pinto",
                ["signedAt"] = signedAt,
                ["basis"] = basis,
                ["samePersonTwoHat"] = true,
            },
            ["operationsProduct"] = new JsonObject
            {
                ["role"] = "operations_product",
                ["signerId"] = "paulo-aboim-pinto",
                ["signerName"] = "Paulo Aboim Pinto",
                ["signedAt"] = signedAt,
                ["basis"] = basis,
                ["samePersonTwoHat"] = true,
            },
        };
    }

    private static void ApplyInternalAudit95Targets(JsonObject register)
    {
        var blockers = GetRequiredArray(register, "blockers");
        var existingBlockerIds = blockers
            .Select(node => node?.AsObject())
            .Where(node => node is not null)
            .Select(node => GetStringOrDefault(node!, "blockerId"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var task in InternalAudit95ReadinessPlan.Tasks)
        {
            var dimension = FindDimension(register, task.DimensionId)
                ?? throw new ReadinessRegisterPromotionException(
                    "Internal audit 95 target references an unknown readiness dimension.",
                    [task.DimensionId]);

            dimension["targetScoreBeforeReviewPilot"] = task.TargetScore;
            var dimensionBlockerIds = GetRequiredArray(dimension, "blockerIds");
            RemoveStrings(
                dimensionBlockerIds,
                new[]
                {
                    "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001",
                    "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001",
                });
            AddUniqueStrings(dimensionBlockerIds, new[] { task.BlockerId });

            if (!existingBlockerIds.Contains(task.BlockerId))
            {
                blockers.Add(new JsonObject
                {
                    ["blockerId"] = task.BlockerId,
                    ["claimLevel"] = "production_organizational_rollout",
                    ["severity"] = "amber",
                    ["status"] = "open",
                    ["description"] = task.Description,
                    ["featureId"] = task.FeatureId,
                    ["acceptanceGateIds"] = CloneStringArray(GetRequiredArray(dimension, "acceptanceGateIds")),
                    ["dimensionIds"] = new JsonArray(task.DimensionId),
                    ["limitationWording"] = "Hush-owned internal audit hardening task required before the 95+ target can be claimed.",
                    ["resolutionCriteria"] = task.ResolutionCriteria,
                });
                existingBlockerIds.Add(task.BlockerId);
            }
        }

        var productionClaim = FindClaimLevel(register, "production_organizational_rollout");
        if (productionClaim is not null)
        {
            productionClaim["blockerIds"] = ToJsonArray(InternalAudit95ReadinessPlan.Tasks.Select(task => task.BlockerId));
        }

        var publicStateClaim = FindClaimLevel(register, "public_or_state_election");
        if (publicStateClaim is not null)
        {
            publicStateClaim["blockerIds"] = new JsonArray();
        }
    }

    private static JsonObject? FindSourceBlockerDecision(JsonObject source, string blockerId) =>
        GetRequiredArray(source, "blockerDecisions")
            .Select(node => node!.AsObject())
            .FirstOrDefault(decision => GetStringOrDefault(decision, "blockerId") == blockerId);

    private static IReadOnlyList<string> GetReadinessEvidenceIds(JsonObject movement) =>
        GetStringArray(movement, "evidenceIds")
            .Where(evidenceId => EvidenceIdPattern.IsMatch(evidenceId))
            .ToArray();

    private static IReadOnlyList<string> GetStringArray(JsonObject item, string propertyName) =>
        item[propertyName] is JsonArray array
            ? array
                .Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
            : [];

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static JsonArray CloneStringArray(JsonArray source) => ToJsonArray(
        source
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!));

    private static void AddUniqueStrings(JsonArray target, IEnumerable<string> values)
    {
        var existing = target
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (existing.Add(value))
            {
                target.Add(value);
            }
        }
    }

    private static void RemoveStrings(JsonArray target, IEnumerable<string> values)
    {
        var valuesToRemove = values.ToHashSet(StringComparer.Ordinal);
        for (var index = target.Count - 1; index >= 0; index--)
        {
            var value = target[index]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value) && valuesToRemove.Contains(value))
            {
                target.RemoveAt(index);
            }
        }
    }

    private static DateTimeOffset ParseRequiredTimestamp(JsonObject item, string propertyName)
    {
        var value = GetRequiredString(item, propertyName);
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static string NormalizeSha256(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value["sha256:".Length..].ToLowerInvariant()
            : value.ToLowerInvariant();

    private static string GuessMediaType(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            _ => "application/octet-stream",
        };
    }

    private static int GetArtifactSizeBytes(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            return 0;
        }

        var length = new FileInfo(fullPath).Length;
        return length > int.MaxValue ? int.MaxValue : (int)length;
    }

    private static void ApplyCommandOverrides(JsonObject register, ReadinessRegisterPromotionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Version))
        {
            register["registerVersion"] = options.Version;
            register["registerVersionId"] = $"RDY-REG-{options.Version}";
        }

        if (!string.IsNullOrWhiteSpace(options.PublicationStatus))
        {
            GetRequiredObject(register, "generatedViews")["publicSafePublicationStatus"] = options.PublicationStatus;
        }
    }

    private static void ValidateSchemaDocument(JsonObject schema, List<string> errors)
    {
        if (GetStringOrDefault(schema, "$schema") != "https://json-schema.org/draft/2020-12/schema")
        {
            errors.Add("readiness-register.schema.json must use JSON Schema draft 2020-12.");
        }

        if (GetStringOrDefault(schema, "$id") != "https://hushnetwork.social/schemas/hushvoting/readiness-register.schema.json")
        {
            errors.Add("readiness-register.schema.json must use the FEAT-130 schema id.");
        }
    }

    private static void ValidateRegister(JsonObject register, ReadinessRegisterPromotionOptions options, List<string> errors)
    {
        RequireFixed(register, "schemaVersion", "1.0", errors);
        RequireFixed(register, "registerId", options.RegisterId, errors);
        RequirePattern(register, "registerVersion", VersionPattern, errors);
        RequirePattern(register, "registerVersionId", RegisterVersionIdPattern, errors);
        RequireEnum(register, "status", ValidStatuses, errors);
        RequireFixed(register, "parentEpic", "EPIC-015", errors);
        RequireNonEmpty(register, "sourceGapRegister", errors);
        RequireNonEmpty(register, "createdAt", errors);
        RequireNonEmpty(register, "sourceCommit", errors);

        var registerVersion = GetStringOrDefault(register, "registerVersion");
        var registerVersionId = GetStringOrDefault(register, "registerVersionId");
        if (!string.IsNullOrWhiteSpace(registerVersion) &&
            registerVersionId != $"RDY-REG-{registerVersion}")
        {
            errors.Add("registerVersionId must be RDY-REG-{registerVersion}.");
        }

        var score = RequireObject(register, "score", errors);
        var dimensions = RequireArray(register, "dimensions", errors);
        var claimLevels = RequireArray(register, "claimLevels", errors);
        var blockers = RequireArray(register, "blockers", errors);
        var evidenceItems = RequireArray(register, "evidenceItems", errors);
        var scoreChanges = RequireArray(register, "scoreChanges", errors);
        var exceptions = RequireArray(register, "exceptions", errors);
        var generatedViews = RequireObject(register, "generatedViews", errors);
        var signoffPolicy = RequireObject(register, "signoffPolicy", errors);
        var claimPolicy = RequireObject(register, "claimPolicy", errors);

        if (score is not null && dimensions is not null)
        {
            ValidateScore(score, dimensions, errors);
        }

        if (claimPolicy is not null)
        {
            ValidateClaimPolicy(register, claimPolicy, errors);
        }

        if (dimensions is not null)
        {
            ValidateDimensions(dimensions, errors);
        }

        if (claimLevels is not null)
        {
            ValidateClaimLevels(claimLevels, errors);
        }

        if (register["claimProfiles"] is JsonArray claimProfiles)
        {
            ValidateClaimProfiles(claimProfiles, errors);
        }

        if (blockers is not null)
        {
            ValidateBlockers(blockers, errors);
        }

        var evidenceById = evidenceItems is null
            ? new Dictionary<string, JsonObject>(StringComparer.Ordinal)
            : ValidateEvidence(evidenceItems, errors);
        if (scoreChanges is not null)
        {
            ValidateScoreChanges(scoreChanges, evidenceById, errors);
        }

        if (exceptions is not null)
        {
            ValidateExceptions(exceptions, errors);
        }

        if (generatedViews is not null)
        {
            RequireFixed(generatedViews, "scorecardPath", ScorecardFileName, errors);
            RequireFixed(generatedViews, "restrictedReviewerExtractPath", RestrictedReviewerExtractFileName, errors);
            RequireFixed(generatedViews, "publicSafeSummaryPath", PublicSafeSummaryFileName, errors);
            RequireEnum(
                generatedViews,
                "publicSafePublicationStatus",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "not_for_publication",
                    "not_ready_for_public_claim",
                    "pilot_only_with_limitations",
                    "production_rollout_with_limitations",
                    "public_claim_blocked",
                },
                errors);
            ValidateProductionRolloutBoundary(register, errors);
        }

        if (signoffPolicy is not null)
        {
            ValidateSignoffPolicy(signoffPolicy, errors);
        }
    }

    private static void ValidateScore(JsonObject score, JsonArray dimensions, List<string> errors)
    {
        var total = RequireInt(score, "total", errors);
        RequireInt(score, "baselineTotal", errors);
        RequireInt(score, "dimensionCount", errors);
        RequireInt(score, "minimumConfidenceScore", errors);
        RequireInt(score, "strongerTargetScore", errors);
        var dimensionIds = RequireArray(score, "dimensionIds", errors);

        var currentTotal = dimensions
            .Select(x => x?.AsObject())
            .Where(x => x is not null)
            .Sum(x => GetIntOrDefault(x!, "currentScore"));
        if (total != currentTotal)
        {
            errors.Add($"score.total must equal dimension score sum. Expected {currentTotal}, found {total}.");
        }

        if (GetIntOrDefault(score, "baselineTotal") != 51)
        {
            errors.Add("score.baselineTotal must be 51 for RDY-REG-v0.1.0.");
        }

        if (GetIntOrDefault(score, "dimensionCount") != 10)
        {
            errors.Add("score.dimensionCount must be 10.");
        }

        if (dimensionIds is not null)
        {
            var ids = dimensionIds.Select(x => x?.GetValue<string>()).ToArray();
            if (!DimensionIds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(ids.OrderBy(x => x, StringComparer.Ordinal)))
            {
                errors.Add("score.dimensionIds must contain RDY-DIM-001 through RDY-DIM-010 exactly once.");
            }
        }
    }

    private static void ValidateClaimPolicy(JsonObject register, JsonObject claimPolicy, List<string> errors)
    {
        if (GetIntOrDefault(claimPolicy, "minimumConfidenceScore") != 70)
        {
            errors.Add("claimPolicy.minimumConfidenceScore must be 70.");
        }

        var strongerTargetScore = GetIntOrDefault(claimPolicy, "strongerTargetScore");
        if (strongerTargetScore is not (80 or InternalAudit95ReadinessPlan.TargetScore))
        {
            errors.Add("claimPolicy.strongerTargetScore must be 80 or 95.");
        }

        var strongestAllowedV1Claim = GetStringOrDefault(claimPolicy, "strongestAllowedV1Claim");
        if (strongestAllowedV1Claim is not ("friendly_organization_pilot" or "production_organizational_rollout"))
        {
            errors.Add("claimPolicy.strongestAllowedV1Claim must be friendly_organization_pilot or production_organizational_rollout.");
        }

        if (strongestAllowedV1Claim == "production_organizational_rollout" &&
            GetStringOrDefault(register, "registerVersion") != "v0.1.6")
        {
            errors.Add("production_organizational_rollout policy ceiling is only valid for RDY-REG-v0.1.6.");
        }

        if (claimPolicy["publicScoreAllowed"]?.GetValue<bool>() != false)
        {
            errors.Add("claimPolicy.publicScoreAllowed must be false.");
        }

        var visibility = RequireArray(claimPolicy, "numericScoreVisibility", errors);
        if (visibility is not null)
        {
            var values = visibility.Select(x => x?.GetValue<string>()).ToArray();
            if (!values.Contains("internal", StringComparer.Ordinal) ||
                !values.Contains("restricted_reviewer", StringComparer.Ordinal) ||
                values.Contains("public_safe", StringComparer.Ordinal))
            {
                errors.Add("claimPolicy.numericScoreVisibility must include internal and restricted_reviewer only.");
            }
        }
    }

    private static void ValidateProductionRolloutBoundary(JsonObject register, List<string> errors)
    {
        var generatedViews = GetRequiredObject(register, "generatedViews");
        var publicationStatus = GetStringOrDefault(generatedViews, "publicSafePublicationStatus");
        var claimPolicy = GetRequiredObject(register, "claimPolicy");
        var policyCeiling = GetStringOrDefault(claimPolicy, "strongestAllowedV1Claim");
        var strongestAllowedClaim = GetCurrentStrongestAllowedClaim(register);
        var productionMode =
            publicationStatus == "production_rollout_with_limitations" ||
            policyCeiling == "production_organizational_rollout" ||
            strongestAllowedClaim == "production_organizational_rollout";

        if (!productionMode)
        {
            return;
        }

        if (publicationStatus != "production_rollout_with_limitations")
        {
            errors.Add("production_organizational_rollout requires publicSafePublicationStatus production_rollout_with_limitations.");
        }

        if (policyCeiling != "production_organizational_rollout")
        {
            errors.Add("production_rollout_with_limitations requires claimPolicy.strongestAllowedV1Claim production_organizational_rollout.");
        }

        if (GetStringOrDefault(register, "registerVersion") != "v0.1.6")
        {
            errors.Add("production_rollout_with_limitations is only valid for RDY-REG-v0.1.6.");
        }

        var total = GetIntOrDefault(GetRequiredObject(register, "score"), "total");
        if (total < 80)
        {
            errors.Add("production_rollout_with_limitations requires score.total to be at least 80.");
        }

        var productionClaim = FindClaimLevel(register, "production_organizational_rollout");
        if (productionClaim is null)
        {
            errors.Add("production_organizational_rollout claim level is required.");
        }
        else
        {
            if (GetStringOrDefault(productionClaim, "blockerSeverity") != "amber" ||
                GetStringOrDefault(productionClaim, "status") != "allowed_with_limitations")
            {
                errors.Add("production_organizational_rollout must be amber and allowed_with_limitations.");
            }

            if (string.IsNullOrWhiteSpace(GetStringOrDefault(productionClaim, "limitationWording")))
            {
                errors.Add("production_organizational_rollout must include limitation wording.");
            }

            if (GetStringOrDefault(productionClaim, "blockerSeverity") == "green" ||
                GetStringOrDefault(productionClaim, "status") == "allowed")
            {
                errors.Add("production_organizational_rollout cannot be green or unqualified allowed in FEAT-156.");
            }
        }

        var friendlyClaim = FindClaimLevel(register, "friendly_organization_pilot");
        if (friendlyClaim is null ||
            GetStringOrDefault(friendlyClaim, "blockerSeverity") != "amber" ||
            GetStringOrDefault(friendlyClaim, "status") != "allowed_with_limitations")
        {
            errors.Add("friendly_organization_pilot must remain amber allowed_with_limitations under production rollout promotion.");
        }

        var publicStateClaim = FindClaimLevel(register, "public_or_state_election");
        if (publicStateClaim is null ||
            GetStringOrDefault(publicStateClaim, "blockerSeverity") != "red" ||
            GetStringOrDefault(publicStateClaim, "status") != "blocked")
        {
            errors.Add("public_or_state_election must remain red and blocked.");
        }

        var productionBlocker = FindBlocker(register, "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");
        if (productionBlocker is null ||
            GetStringOrDefault(productionBlocker, "severity") != "amber" ||
            GetStringOrDefault(productionBlocker, "status") != "open")
        {
            errors.Add("RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001 must be amber/open for limited production rollout.");
        }

        var publicStateBlocker = FindBlocker(register, "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
        if (publicStateBlocker is null ||
            GetStringOrDefault(publicStateBlocker, "severity") != "red" ||
            GetStringOrDefault(publicStateBlocker, "status") != "open")
        {
            errors.Add("RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001 must remain red/open.");
        }
    }

    private static void ValidateDimensions(JsonArray dimensions, List<string> errors)
    {
        if (dimensions.Count != 10)
        {
            errors.Add("dimensions must contain exactly ten items.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dimension in dimensions.Select((node, index) => (node, index)))
        {
            if (dimension.node is not JsonObject item)
            {
                errors.Add($"dimensions[{dimension.index}] must be an object.");
                continue;
            }

            var dimensionId = GetStringOrDefault(item, "dimensionId");
            if (!DimensionIds.Contains(dimensionId, StringComparer.Ordinal))
            {
                errors.Add($"dimensions[{dimension.index}].dimensionId is not a supported dimension id.");
            }

            if (!string.IsNullOrWhiteSpace(dimensionId) && !seen.Add(dimensionId))
            {
                errors.Add($"Dimension id {dimensionId} is duplicated.");
            }

            RequireNonEmpty(item, "name", errors);
            if (GetIntOrDefault(item, "weight") != 10)
            {
                errors.Add($"{dimensionId}.weight must be 10.");
            }

            var currentScore = RequireInt(item, "currentScore", errors);
            if (currentScore < 0 || currentScore > 10)
            {
                errors.Add($"{dimensionId}.currentScore must be 0..10.");
            }

            var evidenceIds = RequireArray(item, "evidenceIds", errors);
            if (currentScore > 0 && evidenceIds is { Count: 0 })
            {
                errors.Add($"{dimensionId}.evidenceIds cannot be empty when currentScore is above 0.");
            }

            RequireArray(item, "sourceGapRows", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "blockerIds", errors);
            RequireNonEmpty(item, "residualRisk", errors);
            RequireNonEmpty(item, "scoreRationale", errors);
        }
    }

    private static void ValidateClaimLevels(JsonArray claimLevels, List<string> errors)
    {
        if (claimLevels.Count != 5)
        {
            errors.Add("claimLevels must contain exactly five items.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in claimLevels.Select((node, index) => (node, index)))
        {
            if (claim.node is not JsonObject item)
            {
                errors.Add($"claimLevels[{claim.index}] must be an object.");
                continue;
            }

            var claimLevel = GetStringOrDefault(item, "claimLevel");
            if (!ClaimLevels.Contains(claimLevel, StringComparer.Ordinal))
            {
                errors.Add($"Unsupported claim level: {claimLevel}.");
            }

            if (!string.IsNullOrWhiteSpace(claimLevel) && !seen.Add(claimLevel))
            {
                errors.Add($"Claim level {claimLevel} is duplicated.");
            }

            var severity = GetStringOrDefault(item, "blockerSeverity");
            var status = GetStringOrDefault(item, "status");
            if (!new[] { "green", "amber", "red" }.Contains(severity, StringComparer.Ordinal))
            {
                errors.Add($"{claimLevel}.blockerSeverity must be green, amber, or red.");
            }

            if (!new[]
                {
                    "allowed",
                    "allowed_with_limitations",
                    "blocked",
                    "future_gated",
                    "external_boundary",
                    "downgraded",
                }.Contains(status, StringComparer.Ordinal))
            {
                errors.Add($"{claimLevel}.status is invalid.");
            }

            if (severity == "amber" && string.IsNullOrWhiteSpace(GetStringOrDefault(item, "limitationWording")))
            {
                errors.Add($"{claimLevel} is amber and must include limitation wording.");
            }

            if ((severity == "red" || status == "blocked") &&
                string.IsNullOrWhiteSpace(GetStringOrDefault(item, "blockedWording")))
            {
                errors.Add($"{claimLevel} is blocked/red and must include blocked wording.");
            }

            if (severity == "red" && status is "allowed" or "allowed_with_limitations")
            {
                errors.Add($"{claimLevel} is red and cannot be allowed.");
            }

            RequireArray(item, "blockerIds", errors);
            RequireNonEmpty(item, "publicSafeStatus", errors);
        }
    }

    private static void ValidateClaimProfiles(JsonArray claimProfiles, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in claimProfiles.Select((node, index) => (node, index)))
        {
            if (profile.node is not JsonObject item)
            {
                errors.Add($"claimProfiles[{profile.index}] must be an object.");
                continue;
            }

            var profileId = GetStringOrDefault(item, "profileId");
            if (!ClaimProfileIds.Contains(profileId, StringComparer.Ordinal))
            {
                errors.Add($"Unsupported claim profile: {profileId}.");
            }

            if (!string.IsNullOrWhiteSpace(profileId) && !seen.Add(profileId))
            {
                errors.Add($"Claim profile {profileId} is duplicated.");
            }

            RequireNonEmpty(item, "label", errors);
            RequireEnum(
                item,
                "productMode",
                new HashSet<string>(["HushVoting! Direct", "HushVoting! Veritas", "HushVoting! Enterprise"], StringComparer.Ordinal),
                errors);
            RequireEnum(
                item,
                "governanceEffect",
                new HashSet<string>(["non_binding", "binding"], StringComparer.Ordinal),
                errors);
            RequireEnum(
                item,
                "bindingStatus",
                new HashSet<string>(["Non-Binding", "Binding"], StringComparer.Ordinal),
                errors);
            RequireEnum(
                item,
                "thresholdProfile",
                new HashSet<string>(["direct", "3/5", "7/10", "8/13", "n/k"], StringComparer.Ordinal),
                errors);
            RequireEnum(
                item,
                "profileClass",
                new HashSet<string>(["standard", "enterprise"], StringComparer.Ordinal),
                errors);
            RequireEnum(
                item,
                "gateSeverity",
                new HashSet<string>(["green", "amber", "red"], StringComparer.Ordinal),
                errors);
            RequireEnum(
                item,
                "gateStatus",
                new HashSet<string>(["passed", "with_warnings", "not_observed", "future_gated", "blocked"], StringComparer.Ordinal),
                errors);

            var claimLevel = GetStringOrDefault(item, "claimLevel");
            if (!ClaimLevels.Contains(claimLevel, StringComparer.Ordinal))
            {
                errors.Add($"{profileId}.claimLevel is invalid.");
            }

            if (item["isNonBindingElection"] is not JsonValue nonBindingValue ||
                !nonBindingValue.TryGetValue<bool>(out var isNonBindingElection))
            {
                errors.Add($"{profileId}.isNonBindingElection must be boolean.");
            }
            else
            {
                var governanceEffect = GetStringOrDefault(item, "governanceEffect");
                var bindingStatus = GetStringOrDefault(item, "bindingStatus");
                if (governanceEffect == "binding" && (isNonBindingElection || bindingStatus != "Binding"))
                {
                    errors.Add($"{profileId} binding profile must use bindingStatus Binding and isNonBindingElection false.");
                }

                if (governanceEffect == "non_binding" && (!isNonBindingElection || bindingStatus != "Non-Binding"))
                {
                    errors.Add($"{profileId} non-binding profile must use bindingStatus Non-Binding and isNonBindingElection true.");
                }
            }

            RequireNonEmpty(item, "claimWording", errors);
            RequireArray(item, "evidenceRefs", errors);
            RequireArray(item, "requiredEvidence", errors);
            var verifierWarningCount = RequireInt(item, "verifierWarningCount", errors);
            var verifierWarnings = RequireArray(item, "verifierWarnings", errors);
            if (verifierWarningCount < 0)
            {
                errors.Add($"{profileId}.verifierWarningCount cannot be negative.");
            }

            if (verifierWarnings is not null)
            {
                if (verifierWarningCount != verifierWarnings.Count)
                {
                    errors.Add($"{profileId}.verifierWarningCount must match verifierWarnings length.");
                }

                foreach (var warning in verifierWarnings.Select((node, index) => (node, index)))
                {
                    if (warning.node is not JsonObject warningItem)
                    {
                        errors.Add($"{profileId}.verifierWarnings[{warning.index}] must be an object.");
                        continue;
                    }

                    RequireNonEmpty(warningItem, "checkCode", errors);
                    RequireNonEmpty(warningItem, "resultCode", errors);
                    RequireNonEmpty(warningItem, "message", errors);
                    RequireNonEmpty(warningItem, "evidenceRef", errors);
                }
            }

            var gateStatus = GetStringOrDefault(item, "gateStatus");
            var gateSeverity = GetStringOrDefault(item, "gateSeverity");
            if (gateStatus == "with_warnings")
            {
                if (gateSeverity != "amber")
                {
                    errors.Add($"{profileId}.gateSeverity must be amber when gateStatus is with_warnings.");
                }

                if (verifierWarningCount == 0)
                {
                    errors.Add($"{profileId}.gateStatus with_warnings requires verifier warnings.");
                }
            }
            else if (verifierWarningCount > 0)
            {
                errors.Add($"{profileId}.verifierWarnings must not be hidden behind gateStatus {gateStatus}.");
            }
        }
    }

    private static void ValidateBlockers(JsonArray blockers, List<string> errors)
    {
        foreach (var blocker in blockers.Select((node, index) => (node, index)))
        {
            if (blocker.node is not JsonObject item)
            {
                errors.Add($"blockers[{blocker.index}] must be an object.");
                continue;
            }

            RequirePattern(item, "blockerId", BlockerIdPattern, errors);
            RequireNonEmpty(item, "description", errors);
            RequireNonEmpty(item, "featureId", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "dimensionIds", errors);
            RequireEnum(item, "severity", new HashSet<string>(["green", "amber", "red"], StringComparer.Ordinal), errors);
            RequireEnum(item, "status", new HashSet<string>(["open", "resolved", "superseded"], StringComparer.Ordinal), errors);
            if (GetStringOrDefault(item, "severity") is "amber" or "red" &&
                string.IsNullOrWhiteSpace(GetStringOrDefault(item, "resolutionCriteria")))
            {
                errors.Add($"{GetStringOrDefault(item, "blockerId")} must include resolution criteria.");
            }
        }
    }

    private static Dictionary<string, JsonObject> ValidateEvidence(JsonArray evidenceItems, List<string> errors)
    {
        var evidenceById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var evidence in evidenceItems.Select((node, index) => (node, index)))
        {
            if (evidence.node is not JsonObject item)
            {
                errors.Add($"evidenceItems[{evidence.index}] must be an object.");
                continue;
            }

            var evidenceId = GetStringOrDefault(item, "evidenceId");
            RequirePattern(item, "evidenceId", EvidenceIdPattern, errors);
            if (!string.IsNullOrWhiteSpace(evidenceId))
            {
                evidenceById[evidenceId] = item;
            }

            RequireNonEmpty(item, "parentEpic", errors);
            RequireNonEmpty(item, "featureId", errors);
            RequireNonEmpty(item, "sourceGapRow", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "dimensionIds", errors);
            RequireNonEmpty(item, "electionScope", errors);
            RequireNonEmpty(item, "releaseScope", errors);
            RequireEnum(item, "visibility", new HashSet<string>(["internal", "restricted_reviewer", "public_safe"], StringComparer.Ordinal), errors);
            RequireEnum(item, "status", EvidenceStates, errors);
            RequireNonEmpty(item, "owner", errors);
            RequireArray(item, "artifactRefs", errors);
            RequireArray(item, "checkResults", errors);
            RequireObject(item, "freshness", errors);
            RequireEnum(item, "claimEffect", ClaimEffects, errors);
            RequireObject(item, "signoffs", errors);
            RequireArray(item, "relatedExceptionIds", errors);
            RequireArray(item, "relatedBlockerIds", errors);

            var status = GetStringOrDefault(item, "status");
            if (status is "observed" or "accepted" && item["producedAt"] is null)
            {
                errors.Add($"{evidenceId}.producedAt is required for observed or accepted evidence.");
            }

            if (status == "accepted")
            {
                ValidateSignoffs(GetRequiredObject(item, "signoffs"), $"{evidenceId}.signoffs", errors);
            }

            ValidateArtifactRefs(RequireArray(item, "artifactRefs", errors), $"{evidenceId}.artifactRefs", errors);
        }

        return evidenceById;
    }

    private static void ValidateArtifactRefs(JsonArray? artifactRefs, string path, List<string> errors)
    {
        if (artifactRefs is null)
        {
            return;
        }

        foreach (var artifactRef in artifactRefs.Select((node, index) => (node, index)))
        {
            if (artifactRef.node is not JsonObject item)
            {
                errors.Add($"{path}[{artifactRef.index}] must be an object.");
                continue;
            }

            RequireNonEmpty(item, "artifactId", errors);
            RequireNonEmpty(item, "relativePath", errors);
            RequireFixed(item, "hashAlgorithm", "SHA-256", errors);
            RequirePattern(item, "sha256Hash", HexSha256Pattern, errors);
            RequireNonEmpty(item, "mediaType", errors);
            if (RequireInt(item, "sizeBytes", errors) < 0)
            {
                errors.Add($"{path}[{artifactRef.index}].sizeBytes must be positive.");
            }
        }
    }

    private static void ValidateScoreChanges(
        JsonArray scoreChanges,
        IReadOnlyDictionary<string, JsonObject> evidenceById,
        List<string> errors)
    {
        foreach (var scoreChange in scoreChanges.Select((node, index) => (node, index)))
        {
            if (scoreChange.node is not JsonObject item)
            {
                errors.Add($"scoreChanges[{scoreChange.index}] must be an object.");
                continue;
            }

            RequirePattern(item, "scoreChangeId", ScoreChangeIdPattern, errors);
            RequireNonEmpty(item, "dimensionId", errors);
            RequireEnum(item, "direction", new HashSet<string>(["increase", "decrease"], StringComparer.Ordinal), errors);
            RequireInt(item, "previousScore", errors);
            RequireInt(item, "proposedScore", errors);
            RequireInt(item, "acceptedScore", errors);
            var evidenceIds = RequireArray(item, "evidenceIds", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "blockerImpactBefore", errors);
            RequireArray(item, "blockerImpactAfter", errors);
            RequireNonEmpty(item, "claimImpact", errors);
            RequireNonEmpty(item, "reason", errors);
            RequireNonEmpty(item, "generatedDiff", errors);

            if (GetStringOrDefault(item, "direction") == "increase")
            {
                ValidateSignoffs(RequireObject(item, "signoffs", errors), $"{GetStringOrDefault(item, "scoreChangeId")}.signoffs", errors);
                foreach (var evidenceId in evidenceIds?.Select(x => x?.GetValue<string>()) ?? [])
                {
                    if (evidenceId is null ||
                        !evidenceById.TryGetValue(evidenceId, out var evidence) ||
                        GetStringOrDefault(evidence, "status") != "accepted")
                    {
                        errors.Add($"{GetStringOrDefault(item, "scoreChangeId")} increases score using non-accepted evidence {evidenceId}.");
                    }
                }
            }
        }
    }

    private static void ValidateExceptions(JsonArray exceptions, List<string> errors)
    {
        foreach (var exception in exceptions.Select((node, index) => (node, index)))
        {
            if (exception.node is not JsonObject item)
            {
                errors.Add($"exceptions[{exception.index}] must be an object.");
                continue;
            }

            RequirePattern(item, "exceptionId", ExceptionIdPattern, errors);
            RequireEnum(item, "type", new HashSet<string>(["skipped", "unavailable", "deferred", "stale_invalidated", "client_declined"], StringComparer.Ordinal), errors);
            RequireNonEmpty(item, "status", errors);
            RequireNonEmpty(item, "reason", errors);
            RequireNonEmpty(item, "owner", errors);
            RequireEnum(item, "severity", new HashSet<string>(["warn", "downgrade", "block"], StringComparer.Ordinal), errors);
        }
    }

    private static void ValidateSignoffPolicy(JsonObject signoffPolicy, List<string> errors)
    {
        var requiredRoles = RequireArray(signoffPolicy, "requiredRoles", errors);
        if (requiredRoles is not null)
        {
            var roles = requiredRoles.Select(x => x?.GetValue<string>()).ToArray();
            if (!roles.Contains("engineering", StringComparer.Ordinal) ||
                !roles.Contains("operations_product", StringComparer.Ordinal))
            {
                errors.Add("signoffPolicy.requiredRoles must contain engineering and operations_product.");
            }
        }

        if (signoffPolicy["allowSamePersonTwoHat"]?.GetValue<bool>() != true)
        {
            errors.Add("signoffPolicy.allowSamePersonTwoHat must be true for v1.");
        }

        if (signoffPolicy["requiresTwoHatMarkerWhenSameSigner"]?.GetValue<bool>() != true)
        {
            errors.Add("signoffPolicy.requiresTwoHatMarkerWhenSameSigner must be true.");
        }

        if (signoffPolicy["independentDualControlClaimAllowed"]?.GetValue<bool>() != false)
        {
            errors.Add("signoffPolicy.independentDualControlClaimAllowed must be false for v1.");
        }
    }

    private static void ValidateSignoffs(JsonObject? signoffs, string path, List<string> errors)
    {
        if (signoffs is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        var engineering = RequireObject(signoffs, "engineering", errors);
        var operations = RequireObject(signoffs, "operationsProduct", errors);
        if (engineering is null || operations is null)
        {
            return;
        }

        ValidateSingleSignoff(engineering, $"{path}.engineering", "engineering", errors);
        ValidateSingleSignoff(operations, $"{path}.operationsProduct", "operations_product", errors);
        if (GetStringOrDefault(engineering, "signerId") == GetStringOrDefault(operations, "signerId") &&
            (engineering["samePersonTwoHat"]?.GetValue<bool>() != true ||
             operations["samePersonTwoHat"]?.GetValue<bool>() != true))
        {
            errors.Add($"{path} uses the same signer and must set samePersonTwoHat on both signoffs.");
        }
    }

    private static void ValidateSingleSignoff(JsonObject signoff, string path, string role, List<string> errors)
    {
        RequireFixed(signoff, "role", role, errors);
        RequireNonEmpty(signoff, "signerId", errors);
        RequireNonEmpty(signoff, "signerName", errors);
        RequireNonEmpty(signoff, "basis", errors);
        if (signoff["signedAt"] is null)
        {
            errors.Add($"{path}.signedAt is required.");
        }
    }

    private static void ValidateExample(JsonObject example, ReadinessRegisterPromotionOptions options, List<string> errors)
    {
        ValidateRegister(example, options, errors);

        var evidenceItems = RequireArray(example, "evidenceItems", errors);
        var exceptions = RequireArray(example, "exceptions", errors);
        if (evidenceItems is null)
        {
            return;
        }

        var states = evidenceItems
            .Select(x => x?.AsObject())
            .Where(x => x is not null)
            .Select(x => GetStringOrDefault(x!, "status"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredState in new[] { "accepted", "blocked", "stale", "rejected" })
        {
            if (requiredState == "stale")
            {
                var hasStale = evidenceItems
                    .Select(x => x?.AsObject())
                    .Where(x => x is not null)
                    .Any(x =>
                        x!["freshness"] is JsonObject freshness &&
                        GetStringOrDefault(freshness, "state").StartsWith("stale_", StringComparison.Ordinal));
                if (!hasStale)
                {
                    errors.Add("readiness-register.example.json must include stale evidence.");
                }

                continue;
            }

            if (!states.Contains(requiredState))
            {
                errors.Add($"readiness-register.example.json must include {requiredState} evidence.");
            }
        }

        if (exceptions is { Count: 0 })
        {
            errors.Add("readiness-register.example.json must include at least one exception.");
        }
    }

    private static List<PromotedFile> BuildPromotedFiles(JsonObject schema, JsonObject register, JsonObject example)
    {
        return
        [
            new(SchemaFileName, "restricted", EncodingWithoutBom(SerializeJson(schema)), "application/schema+json"),
            new(RegisterFileName, "internal", EncodingWithoutBom(SerializeJson(register)), "application/json"),
            new(ExampleFileName, "restricted", EncodingWithoutBom(SerializeJson(example)), "application/json"),
        ];
    }

    private static IReadOnlyList<PromotedFile> BuildProfileEvidenceProcedurePageFiles(JsonObject register) =>
        ProfileEvidenceProcedureRows
            .Select(row => new PromotedFile(
                GetProfileEvidenceProcedurePageRelativePath(row),
                "restricted",
                EncodingWithoutBom(GetProfileEvidenceProcedurePageMarkdown(register, row)),
                "text/markdown"))
            .ToArray();

    private static PromotedFile BuildExternalAuditorEntryPointPageFile(JsonObject register) =>
        new(
            GetExternalAuditorEntryPointPageRelativePath(),
            "restricted",
            EncodingWithoutBom(GetExternalAuditorEntryPointMarkdown(register)),
            "text/markdown");

    private static string GetProfileEvidenceProcedurePageRelativePath(ProfileEvidenceProcedureRow row) =>
        $"{ReadinessCheckPagesDirectory}/{row.PageFileName}";

    private static string GetExternalAuditorEntryPointPageRelativePath() =>
        $"{ReadinessCheckPagesDirectory}/{ExternalAuditorEntryPointFileName}";

    private static string GetProfileEvidenceProcedurePageMarkdown(JsonObject register, ProfileEvidenceProcedureRow row)
    {
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine($"# Readiness Check: {row.Scope}");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Check ID", row.CheckId),
            ("Scope", row.Scope),
            ("Visibility", "restricted reviewer"),
            ("Back To Scorecard", "../readiness-scorecard.md"));

        sb.AppendLine("## What Was Tested");
        sb.AppendLine();
        sb.AppendLine(row.CheckPerformed);
        sb.AppendLine();

        sb.AppendLine("## Environment Applicability");
        sb.AppendLine();
        sb.AppendLine("Disabled or developer-adjusted Development checks are not failures. They mean the check is outside the Development claim boundary and must be replaced by the Development row below while remaining required for stronger environments.");
        AppendTableHeader(sb, "Environment", "Applicability", "Required Check");
        AppendTableRow(sb, "Development", row.DevelopmentApplicability, row.DevelopmentCheck);
        AppendTableRow(sb, "PreProduction", row.PreProductionApplicability, row.CheckPerformed);
        AppendTableRow(sb, "Production", row.ProductionApplicability, row.CheckPerformed);
        sb.AppendLine();

        sb.AppendLine("## When Was Tested");
        AppendTableHeader(sb, "Evidence Set", "Timestamp Rule / Observed Time");
        AppendTableRow(sb, "Current accepted evidence baseline", row.V018Timing);
        AppendTableRow(sb, "Future readiness reports", row.FutureEvidenceTiming);
        sb.AppendLine();

        sb.AppendLine("## Evidence And Proof Sources");
        AppendTableHeader(sb, "Source Type", "Value");
        AppendTableRow(sb, "Database / Record Surface", row.DatabaseRecordSurface);
        AppendTableRow(sb, "Exported Evidence / Proof Artifacts", row.EvidenceOrProofArtifacts);
        sb.AppendLine();

        sb.AppendLine("## Check Rule");
        sb.AppendLine();
        sb.AppendLine(row.PassRule);
        sb.AppendLine();

        sb.AppendLine("## Current Evidence Result");
        sb.AppendLine();
        sb.AppendLine(row.V018EvidenceResult);
        sb.AppendLine();

        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetExternalAuditorEntryPointMarkdown(JsonObject register)
    {
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# External Auditor Entry Point");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Visibility", "restricted reviewer"),
            ("Back To Scorecard", "../readiness-scorecard.md"));

        sb.AppendLine("## How To Use This Page");
        sb.AppendLine();
        sb.AppendLine("This is the reviewer navigation map. It does not replace evidence; it tells the auditor where to start, which records and artifacts bind each workflow step, which verifier checks to run, and which mismatch blocks the claim.");
        sb.AppendLine();
        sb.AppendLine("Start with the package manifest and register version/hash, then follow the stages in election order: Protocol Omega package, circuit/proof binding, invitations/access, KMS custody, trustee ceremony, Open approval, ballots/publication, Close/Count/Tally, Finalize/Results, and no-key persistence scans. Do not accept a numeric score without matching row-level evidence.");
        sb.AppendLine();
        sb.AppendLine("Exact KMS key identifiers, auditor reader-key wrapping details, trustee raw shares, executor private keys, proof witnesses, vote secrets, and decrypt material remain restricted-only. Public-safe output may show safe references, fingerprints, hashes, and lifecycle status only.");
        sb.AppendLine();
        AppendExternalAuditorEntryPointTable(sb);
        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetScorecardMarkdown(JsonObject register, IReadOnlyList<PromotedFile> currentFiles)
    {
        var score = GetRequiredObject(register, "score");
        var generatedViews = GetRequiredObject(register, "generatedViews");
        var claimPolicy = GetRequiredObject(register, "claimPolicy");
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# HushVoting Readiness Scorecard");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Status", GetRequiredString(register, "status")),
            ("Generated At", GetRequiredString(register, "promotedAt")),
            ("Source Commit", GetRequiredString(register, "sourceCommit")),
            ("Parent Epic", GetRequiredString(register, "parentEpic")),
            ("Source Gap Register", GetRequiredString(register, "sourceGapRegister")),
            ("Publication Status", GetRequiredString(generatedViews, "publicSafePublicationStatus")));

        sb.AppendLine("## Score Summary");
        sb.AppendLine();
        sb.AppendLine($"Total score: {GetRequiredInt(score, "total")}/100");
        sb.AppendLine($"Minimum confidence threshold: {GetRequiredInt(score, "minimumConfidenceScore")}");
        sb.AppendLine($"Stronger target threshold: {GetRequiredInt(score, "strongerTargetScore")}");
        sb.AppendLine($"Strongest claim allowed by v1 policy ceiling: {GetRequiredString(claimPolicy, "strongestAllowedV1Claim")}");
        sb.AppendLine($"Current strongest allowed claim: {GetCurrentStrongestAllowedClaim(register)}");
        sb.AppendLine(GetScorecardGoNoGoResult(register));
        sb.AppendLine();

        sb.AppendLine("## Dimension Scores");
        AppendTableHeader(sb, "Dimension ID", "Dimension", "Current", "Target", "Delta To Target", "Primary Gates", "Evidence Count", "Blockers");
        foreach (var dimension in GetRequiredArray(register, "dimensions").Select(x => x!.AsObject()))
        {
            var evidenceCount = GetRequiredArray(dimension, "evidenceIds").Count;
            var current = GetRequiredInt(dimension, "currentScore");
            var target = GetRequiredInt(dimension, "targetScoreBeforeReviewPilot");
            AppendTableRow(
                sb,
                GetRequiredString(dimension, "dimensionId"),
                GetRequiredString(dimension, "name"),
                current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Max(target - current, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                JoinArray(dimension, "acceptanceGateIds"),
                evidenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                JoinArray(dimension, "blockerIds"));
        }

        sb.AppendLine();
        sb.AppendLine("## Claim Gates");
        AppendTableHeader(sb, "Claim Level", "Severity", "Status", "Allowed Wording", "Limitation Wording", "Blocked Wording", "Blocker IDs");
        foreach (var claim in GetRequiredArray(register, "claimLevels").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(claim, "claimLevel"),
                GetRequiredString(claim, "blockerSeverity"),
                GetRequiredString(claim, "status"),
                GetRequiredString(claim, "allowedWording"),
                GetRequiredString(claim, "limitationWording"),
                GetRequiredString(claim, "blockedWording"),
                JoinArray(claim, "blockerIds"));
        }

        AppendClaimProfilesSection(sb, register, includeEvidence: false);
        AppendDevelopmentProfileClarificationSection(sb, register);
        AppendExternalAuditorEntryPointSection(sb);
        AppendProfileEvidenceProcedureSection(sb);
        AppendEnvironmentOperationalChecklistSection(sb);

        sb.AppendLine();
        sb.AppendLine("## Active Blockers");
        AppendTableHeader(sb, "Blocker ID", "Claim Level", "Severity", "Status", "Feature", "Gates", "Resolution Criteria");
        foreach (var blocker in GetRequiredArray(register, "blockers").Select(x => x!.AsObject()).Where(x => GetRequiredString(x, "status") == "open"))
        {
            AppendTableRow(
                sb,
                GetRequiredString(blocker, "blockerId"),
                GetRequiredString(blocker, "claimLevel"),
                GetRequiredString(blocker, "severity"),
                GetRequiredString(blocker, "status"),
                GetRequiredString(blocker, "featureId"),
                JoinArray(blocker, "acceptanceGateIds"),
                GetRequiredString(blocker, "resolutionCriteria"));
        }

        sb.AppendLine();
        sb.AppendLine("## Resolved And Superseded Blockers");
        AppendTableHeader(sb, "Blocker ID", "Claim Level", "Severity", "Status", "Feature", "Gates", "Resolution Criteria");
        foreach (var blocker in GetRequiredArray(register, "blockers").Select(x => x!.AsObject()).Where(x => GetRequiredString(x, "status") != "open"))
        {
            AppendTableRow(
                sb,
                GetRequiredString(blocker, "blockerId"),
                GetRequiredString(blocker, "claimLevel"),
                GetRequiredString(blocker, "severity"),
                GetRequiredString(blocker, "status"),
                GetRequiredString(blocker, "featureId"),
                JoinArray(blocker, "acceptanceGateIds"),
                GetRequiredString(blocker, "resolutionCriteria"));
        }

        sb.AppendLine();
        sb.AppendLine("## Score Changes");
        AppendTableHeader(sb, "Score Change ID", "Dimension ID", "Direction", "Previous", "Proposed", "Accepted", "Evidence IDs", "Reason");
        foreach (var scoreChange in GetRequiredArray(register, "scoreChanges").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(scoreChange, "scoreChangeId"),
                GetRequiredString(scoreChange, "dimensionId"),
                GetRequiredString(scoreChange, "direction"),
                GetRequiredInt(scoreChange, "previousScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredInt(scoreChange, "proposedScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredInt(scoreChange, "acceptedScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                JoinArray(scoreChange, "evidenceIds"),
                GetRequiredString(scoreChange, "reason"));
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence Status");
        AppendTableHeader(sb, "Evidence ID", "Feature", "Gates", "Dimensions", "Status", "Visibility", "Freshness", "Claim Effect");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(evidence, "evidenceId"),
                GetRequiredString(evidence, "featureId"),
                JoinArray(evidence, "acceptanceGateIds"),
                JoinArray(evidence, "dimensionIds"),
                GetRequiredString(evidence, "status"),
                GetRequiredString(evidence, "visibility"),
                GetRequiredString(GetRequiredObject(evidence, "freshness"), "state"),
                GetRequiredString(evidence, "claimEffect"));
        }

        sb.AppendLine();
        sb.AppendLine("## Exceptions And Rejections");
        sb.AppendLine();
        sb.AppendLine("Exceptions:");
        AppendTableHeader(sb, "Exception ID", "Type", "Status", "Severity", "Reason");
        foreach (var exception in GetRequiredArray(register, "exceptions").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(exception, "exceptionId"),
                GetRequiredString(exception, "type"),
                GetRequiredString(exception, "status"),
                GetRequiredString(exception, "severity"),
                GetRequiredString(exception, "reason"));
        }

        sb.AppendLine();
        sb.AppendLine("Rejected evidence:");
        AppendTableHeader(sb, "Evidence ID", "Feature", "Reason");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()).Where(x => GetRequiredString(x, "status") == "rejected"))
        {
            AppendTableRow(sb, GetRequiredString(evidence, "evidenceId"), GetRequiredString(evidence, "featureId"), GetRequiredString(evidence, "residualRisk"));
        }

        sb.AppendLine();
        sb.AppendLine("## Residual Risk");
        AppendTableHeader(sb, "Dimension ID", "Residual Risk", "Related Evidence", "Related Blockers");
        foreach (var dimension in GetRequiredArray(register, "dimensions").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(dimension, "dimensionId"),
                GetRequiredString(dimension, "residualRisk"),
                JoinArray(dimension, "evidenceIds"),
                JoinArray(dimension, "blockerIds"));
        }

        sb.AppendLine();
        sb.AppendLine("## Signoff Summary");
        AppendTableHeader(sb, "Evidence/Score Item", "Engineering Signer", "Operations/Product Signer", "Same Person / Two Hats", "Signed At");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            AppendSignoffRow(sb, GetRequiredString(evidence, "evidenceId"), GetRequiredObject(evidence, "signoffs"));
        }

        foreach (var scoreChange in GetRequiredArray(register, "scoreChanges").Select(x => x!.AsObject()))
        {
            AppendSignoffRow(sb, GetRequiredString(scoreChange, "scoreChangeId"), GetRequiredObject(scoreChange, "signoffs"));
        }

        sb.AppendLine();
        sb.AppendLine("## Generated Artifacts");
        AppendTableHeader(sb, "Artifact", "Visibility", "SHA-256", "Size Bytes");
        foreach (var file in currentFiles.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            AppendTableRow(sb, file.RelativePath, file.Visibility, ComputeSha256Hex(file.Bytes), file.Bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetRestrictedReviewerExtractMarkdown(JsonObject register)
    {
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# HushVoting Restricted Readiness Reviewer Extract");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Status", GetRequiredString(register, "status")),
            ("Generated Time", GetRequiredString(register, "promotedAt")),
            ("Source Commit", GetRequiredString(register, "sourceCommit")),
            ("Reviewer Scope", "restricted reviewer navigation; full private artifact access remains controlled"));

        sb.AppendLine("## Readiness Score");
        sb.AppendLine();
        sb.AppendLine($"Total readiness score: {GetRequiredInt(GetRequiredObject(register, "score"), "total")}/100");
        AppendTableHeader(sb, "Dimension ID", "Dimension", "Score", "Target");
        foreach (var dimension in GetRequiredArray(register, "dimensions").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(dimension, "dimensionId"),
                GetRequiredString(dimension, "name"),
                GetRequiredInt(dimension, "currentScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredInt(dimension, "targetScoreBeforeReviewPilot").ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        sb.AppendLine();
        sb.AppendLine("## Claim And Blocker Status");
        AppendTableHeader(sb, "Claim Level", "Severity", "Status", "Blockers");
        foreach (var claim in GetRequiredArray(register, "claimLevels").Select(x => x!.AsObject()))
        {
            AppendTableRow(sb, GetRequiredString(claim, "claimLevel"), GetRequiredString(claim, "blockerSeverity"), GetRequiredString(claim, "status"), JoinArray(claim, "blockerIds"));
        }

        sb.AppendLine();
        AppendTableHeader(sb, "Blocker ID", "Feature", "Gates", "Resolution Criteria");
        foreach (var blocker in GetRequiredArray(register, "blockers").Select(x => x!.AsObject()))
        {
            AppendTableRow(sb, GetRequiredString(blocker, "blockerId"), GetRequiredString(blocker, "featureId"), JoinArray(blocker, "acceptanceGateIds"), GetRequiredString(blocker, "resolutionCriteria"));
        }

        AppendClaimProfilesSection(sb, register, includeEvidence: true);
        AppendDevelopmentProfileClarificationSection(sb, register);
        AppendExternalAuditorEntryPointSection(sb);
        AppendProfileEvidenceProcedureSection(sb);
        AppendEnvironmentOperationalChecklistSection(sb);

        sb.AppendLine();
        sb.AppendLine("## Evidence Index");
        AppendTableHeader(sb, "Evidence ID", "Feature", "Gate", "Dimension", "Visibility", "Restricted Ref", "SHA-256", "Status", "Freshness");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            var firstArtifact = GetRequiredArray(evidence, "artifactRefs").Select(x => x?.AsObject()).FirstOrDefault(x => x is not null);
            AppendTableRow(
                sb,
                GetRequiredString(evidence, "evidenceId"),
                GetRequiredString(evidence, "featureId"),
                JoinArray(evidence, "acceptanceGateIds"),
                JoinArray(evidence, "dimensionIds"),
                GetRequiredString(evidence, "visibility"),
                firstArtifact is null ? "controlled-access-index" : GetRequiredString(firstArtifact, "relativePath"),
                firstArtifact is null ? "not-applicable" : GetRequiredString(firstArtifact, "sha256Hash"),
                GetRequiredString(evidence, "status"),
                GetRequiredString(GetRequiredObject(evidence, "freshness"), "state"));
        }

        sb.AppendLine();
        sb.AppendLine("## Score-Change History");
        AppendTableHeader(sb, "Score Change ID", "Dimension", "Accepted", "Reason");
        foreach (var scoreChange in GetRequiredArray(register, "scoreChanges").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(scoreChange, "scoreChangeId"),
                GetRequiredString(scoreChange, "dimensionId"),
                GetRequiredInt(scoreChange, "acceptedScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredString(scoreChange, "reason"));
        }

        sb.AppendLine();
        sb.AppendLine("## Signoff Summary");
        AppendTableHeader(sb, "Item", "Engineering", "Operations/Product", "Same Person / Two Hats", "Signed At");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            AppendSignoffRow(sb, GetRequiredString(evidence, "evidenceId"), GetRequiredObject(evidence, "signoffs"));
        }

        sb.AppendLine();
        sb.AppendLine("## Exceptions, Stale Evidence, Rejections, And Superseded Evidence");
        sb.AppendLine();
        sb.AppendLine("Exceptions, stale evidence, rejected evidence, and superseded evidence are listed by stable id for controlled review.");
        sb.AppendLine();

        sb.AppendLine("## Public-Safe Summary Preview");
        sb.AppendLine();
        sb.AppendLine(GetPublicSafeSummaryBody(register));
        sb.AppendLine();

        sb.AppendLine("## Omitted Private Artifacts");
        AppendTableHeader(sb, "Artifact Category", "Reason Omitted", "How Reviewer Requests Access");
        AppendTableRow(sb, "Raw support logs", "May contain private support context", "Request controlled evidence export from the readiness owner");
        AppendTableRow(sb, "Raw anomaly detail", "May expose private election/customer information", "Request EPIC-014 governed evidence package");
        AppendTableRow(sb, "Operational deployment detail", "Could weaken security posture", "Request restricted operations walkthrough");
        sb.AppendLine();

        sb.AppendLine("## Controlled Evidence Access");
        sb.AppendLine();
        sb.AppendLine("Reviewer access is handled by the readiness owner using controlled private artifact paths referenced by the manifest.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetPublicSafeSummaryMarkdown(JsonObject register)
    {
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# HushVoting Public-Safe Readiness Summary");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Generated At", GetRequiredString(register, "promotedAt")),
            ("Publication Status", GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus")));
        sb.Append(GetPublicSafeSummaryBody(register));
        return NormalizeLineEndings(sb.ToString());
    }

    private static void AppendEnvironmentOperationalChecklistSection(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("## Environment Operational Checklists");
        sb.AppendLine();
        sb.AppendLine("Development Direct claim profiles use the machine checklist only. OPS-002 access-control snapshot, OPS-006 backup/restore, and OPS-008 auditor-room access-log controls move to PreProduction/Production, where they are split into machine and human sub-checklists. PreProduction is optional: if it exists, it can accept an immutable production candidate and Production only needs an activation/delta addendum; if it does not exist, Production runs the full readiness workflow directly. Human checklist entries record observations or signed attestations; claim blocking is decided by the promotion policy, not by editing the checklist.");
        AppendTableHeader(sb, "Environment", "Sub-Checklist", "Stage Status", "Responsibility", "Evidence / Checks", "Claim Impact");
        foreach (var row in EnvironmentOperationalChecklistRows)
        {
            AppendTableRow(
                sb,
                row.Environment,
                row.SubChecklist,
                row.StageStatus,
                row.Responsibility,
                row.EvidenceOrChecks,
                row.ClaimImpact);
        }
    }

    private static void AppendExternalAuditorEntryPointSection(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("## External Auditor Entry Point");
        sb.AppendLine();
        sb.AppendLine($"Use [{ExternalAuditorEntryPointFileName}]({GetExternalAuditorEntryPointPageRelativePath()}) as the restricted reviewer navigation map. It identifies the records, artifacts, verifier checks, and blocking mismatches that let an external auditor walk the election with their own evidence review.");
        sb.AppendLine();
        AppendExternalAuditorEntryPointTable(sb);
    }

    private static void AppendExternalAuditorEntryPointTable(StringBuilder sb)
    {
        AppendTableHeader(sb, "Audit Stage", "Question", "Primary Records", "Artifacts / Proofs", "Verifier Checks", "Blocking Condition");
        foreach (var row in ExternalAuditorEntryPointRows)
        {
            AppendTableRow(
                sb,
                row.Stage,
                row.Question,
                row.PrimaryRecords,
                row.ArtifactsOrProofs,
                row.VerifierChecks,
                row.BlockingCondition);
        }
    }

    private static void AppendProfileEvidenceProcedureSection(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("## Profile Evidence Verification Procedure");
        sb.AppendLine();
        sb.AppendLine("A claim profile gate is not a standalone checkmark. A passed profile must be backed by field-level evidence checks against the database-backed records and exported proof artifacts below. Profiles marked not_observed or future_gated use the same rows as unmet requirements before they can pass.");
        AppendTableHeader(sb, "Page", "Scope", "Development Mode", "Check Performed", "Database / Record Surface", "Evidence / Proof Artifacts", "Pass Rule");
        foreach (var row in ProfileEvidenceProcedureRows)
        {
            AppendTableRow(
                sb,
                $"[{row.CheckId}]({GetProfileEvidenceProcedurePageRelativePath(row)})",
                row.Scope,
                row.DevelopmentApplicability,
                row.CheckPerformed,
                row.DatabaseRecordSurface,
                row.EvidenceOrProofArtifacts,
                row.PassRule);
        }
    }

    private static void AppendDevelopmentProfileClarificationSection(StringBuilder sb, JsonObject register)
    {
        if (!string.Equals(GetRequiredString(register, "registerVersion"), DevelopmentProfileClarificationTargetVersion, StringComparison.Ordinal))
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("## v0.1.9 Development And Production Boundary Clarification");
        sb.AppendLine();
        sb.AppendLine("RDY-REG-v0.1.9 is a clarification and reviewer-hardening publication over the accepted RDY-REG-v0.1.8 score. It does not raise the numeric score or silently promote Veritas profile gates without a signed exported verifier package.");
        sb.AppendLine();
        AppendTableHeader(sb, "Topic", "Development / Non-Binding Rehearsal", "Binding / Production Expectation");
        AppendTableRow(
            sb,
            "SelectedProfileDevOnly",
            "Expected for non-binding readable rehearsal profiles such as dkg-dev-3of5. The flag means the selected profile is a development/rehearsal profile.",
            "Must be false for binding Veritas 500. Binding trustee elections resolve to dkg-prod-3of5, not dkg-dev-3of5.");
        AppendTableRow(
            sb,
            "ContactCodeProviderReadiness",
            "The persisted enum value is DevOnly, but the claim meaning is development/rehearsal provider accepted inside a non-high-assurance verifier profile.",
            "Production/high-assurance claims require Ready. DevOnly becomes a blocker outside the development/rehearsal boundary.");
        AppendTableRow(
            sb,
            "ProtocolPackageExternalReviewStatus",
            "NotReviewed means no independent external reviewer conclusion has been imported. Development verifier profiles accept this as a non-claim when SP-09 shape is valid.",
            "Production or external-review claims require SP-09 reviewer evidence and a catalog/binding status that reflects the reviewed conclusion.");
        AppendTableRow(
            sb,
            "Deployment proof family",
            "Development-runtime self-attestation is accepted for local rehearsal scope and must remain visibly scoped to that environment.",
            "Production deployment/build completeness requires production deployment proof, release identity, operational evidence, and repeatable activation/rollback evidence.");
    }

    private static string GetPublicSafeSummaryBody(JsonObject register)
    {
        var sb = new StringBuilder();
        var publicationStatus = GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus");
        var strongestAllowedClaim = GetCurrentStrongestAllowedClaim(register);
        var internalAudit95Accepted = IsInternalAudit95Accepted(register);
        var isDevelopmentProfileClarification = string.Equals(
            GetRequiredString(register, "registerVersion"),
            DevelopmentProfileClarificationTargetVersion,
            StringComparison.Ordinal);
        sb.AppendLine("## Current Public-Safe Status");
        sb.AppendLine();
        sb.AppendLine(publicationStatus);
        sb.AppendLine();
        sb.AppendLine("## Approved Public-Safe Claim Wording");
        sb.AppendLine();
        if (strongestAllowedClaim == "production_organizational_rollout")
        {
            sb.AppendLine("HushVoting may be discussed for limited organizational rollout with explicit limitations. It is not presented as public or state election readiness, customer authority approval, external certification, or a complete meeting-management product.");
        }
        else if (strongestAllowedClaim == "friendly_organization_pilot")
        {
            sb.AppendLine("HushVoting may be discussed for controlled friendly-organization pilot use with explicit limitations. It is not presented as production rollout software, public/state election software, legal sufficiency validation, or independent certification.");
        }
        else
        {
            sb.AppendLine("HushVoting is being prepared for internal non-binding rehearsal use only. Pilot, production, and public election readiness claims remain unavailable until the remaining readiness blockers are resolved and accepted.");
        }

        sb.AppendLine();
        sb.AppendLine("## Known Limitations");
        sb.AppendLine();
        if (strongestAllowedClaim == "production_organizational_rollout")
        {
            sb.AppendLine("- Limited organizational rollout use must keep residual risks, customer-owned governance responsibilities, and external prerequisites visible.");
            sb.AppendLine("- Repeated operating history, customer-site variance, public/state prerequisites, customer authority review, and external validation remain limitations.");
        }
        else if (strongestAllowedClaim == "friendly_organization_pilot")
        {
            sb.AppendLine("- Friendly-organization pilot use must remain controlled, bounded, and privately reviewed.");
            sb.AppendLine(internalAudit95Accepted
                ? "- Hush-owned internal-audit-95 hardening is accepted in this register; rehearsal evidence, binding-election proof generation, proof verification, customer governance, and external review remain downstream execution gates."
                : "- Hush-owned 95+ hardening, rehearsal evidence, binding-election proof generation, and proof-verification evidence remain future execution gates.");
            if (isDevelopmentProfileClarification)
            {
                sb.AppendLine("- RDY-REG-v0.1.9 clarifies that development/rehearsal flags are accepted only inside the development evidence boundary; production and external-review claims still require their production evidence.");
            }
        }
        else
        {
            sb.AppendLine("- Internal rehearsal use must be labelled non-binding.");
            sb.AppendLine("- Pilot readiness remains blocked until the minimum confidence band and remaining pilot-critical evidence gates are satisfied.");
        }

        if (strongestAllowedClaim == "production_organizational_rollout")
        {
            sb.AppendLine("- Public or state election readiness is not claimed in this version.");
        }
        else
        {
            sb.AppendLine(internalAudit95Accepted
                ? "- Production rollout remains a downstream execution gate after internal-audit-95 promotion and still requires rehearsal, binding-election proof validation, customer governance, and external review."
                : "- Production rollout is a future execution gate after Hush-owned 95+ hardening is complete.");
            sb.AppendLine("- Public/state election readiness is an external boundary outside this internal audit report.");
        }
        sb.AppendLine();
        sb.AppendLine("## Non-Claims");
        sb.AppendLine();
        sb.AppendLine("- This summary does not certify deployment, authorize public elections, or validate customer governance obligations.");
        sb.AppendLine("- This summary does not publish private readiness scoring or restricted evidence.");
        sb.AppendLine();
        sb.AppendLine("## Public-Safe Evidence Categories");
        sb.AppendLine();
        sb.AppendLine("- Protocol package and verifier documentation.");
        sb.AppendLine("- Operational readiness categories under active development.");
        sb.AppendLine("- Controlled reviewer extracts available through private review channels.");
        sb.AppendLine();
        sb.AppendLine("## Contact / Review Path");
        sb.AppendLine();
        sb.AppendLine("Contact the HushVoting readiness owner for controlled reviewer access and current readiness package details.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetScorecardGoNoGoResult(JsonObject register)
    {
        var internalAudit95Accepted = IsInternalAudit95Accepted(register);
        return GetCurrentStrongestAllowedClaim(register) switch
        {
            "production_organizational_rollout" =>
                "Current go/no-go result: limited organizational rollout is allowed with limitations; public/state election readiness remains an external boundary.",
            "friendly_organization_pilot" =>
                internalAudit95Accepted
                    ? "Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; internal-audit-95 hardening is accepted, while production rollout remains downstream-gated and public/state election readiness remains an external boundary."
                    : "Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout is future-gated by the 95+ hardening plan and public/state election readiness remains an external boundary.",
            "internal_non_binding_rehearsal" =>
                "Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked.",
            "internal_development" =>
                "Current go/no-go result: internal development tracking is allowed; rehearsal and stronger readiness claims remain unavailable.",
            _ =>
                "Current go/no-go result: no readiness claim is currently allowed.",
        };
    }

    private static bool IsInternalAudit95Accepted(JsonObject register)
    {
        if (register["score"] is not JsonObject score)
        {
            return false;
        }

        return GetIntOrDefault(score, "strongerTargetScore") >= InternalAudit95ReadinessPlan.TargetScore &&
            GetIntOrDefault(score, "total") >= InternalAudit95ReadinessPlan.TargetScore;
    }

    private static void AppendGeneratedHeader(StringBuilder sb)
    {
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
    }

    private static void AppendMetadataTable(StringBuilder sb, params (string Label, string Value)[] rows)
    {
        AppendTableHeader(sb, "Field", "Value");
        foreach (var row in rows)
        {
            AppendTableRow(sb, row.Label, row.Value);
        }

        sb.AppendLine();
    }

    private static void AppendTableHeader(StringBuilder sb, params string[] columns)
    {
        sb.AppendLine("| " + string.Join(" | ", columns) + " |");
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");
    }

    private static void AppendTableRow(StringBuilder sb, params string[] values)
    {
        sb.AppendLine("| " + string.Join(" | ", values.Select(EscapeMarkdownTableValue)) + " |");
    }

    private static void AppendSignoffRow(StringBuilder sb, string itemId, JsonObject signoffs)
    {
        var engineering = GetRequiredObject(signoffs, "engineering");
        var operations = GetRequiredObject(signoffs, "operationsProduct");
        AppendTableRow(
            sb,
            itemId,
            GetRequiredString(engineering, "signerName"),
            GetRequiredString(operations, "signerName"),
            GetBoolOrDefault(engineering, "samePersonTwoHat") || GetBoolOrDefault(operations, "samePersonTwoHat") ? "yes" : "no",
            GetRequiredString(engineering, "signedAt"));
    }

    private static void AppendClaimProfilesSection(StringBuilder sb, JsonObject register, bool includeEvidence)
    {
        if (register["claimProfiles"] is not JsonArray claimProfiles || claimProfiles.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("## HushVoting Claim Profiles");
        if (includeEvidence)
        {
            AppendTableHeader(
                sb,
                "Profile",
                "Product Mode",
                "Binding Status",
                "Non-Binding Election",
                "Threshold",
                "Severity",
                "Gate Status",
                "Claim Level",
                "Verifier Warnings",
                "Evidence Refs",
                "Required Evidence");
            foreach (var profile in claimProfiles.Select(x => x!.AsObject()))
            {
                AppendTableRow(
                    sb,
                    GetRequiredString(profile, "label"),
                    GetRequiredString(profile, "productMode"),
                    GetRequiredString(profile, "bindingStatus"),
                    GetRequiredBool(profile, "isNonBindingElection").ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                    GetRequiredString(profile, "thresholdProfile"),
                    GetRequiredString(profile, "gateSeverity"),
                    GetRequiredString(profile, "gateStatus"),
                    GetRequiredString(profile, "claimLevel"),
                    JoinClaimProfileWarnings(profile),
                    JoinArray(profile, "evidenceRefs"),
                    JoinArray(profile, "requiredEvidence"));
            }

            return;
        }

        AppendTableHeader(
            sb,
            "Profile",
            "Product Mode",
            "Binding Status",
            "Non-Binding Election",
            "Threshold",
            "Severity",
            "Gate Status",
            "Claim Level",
            "Verifier Warnings",
            "Claim Wording",
            "Limitation Wording");
        foreach (var profile in claimProfiles.Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(profile, "label"),
                GetRequiredString(profile, "productMode"),
                GetRequiredString(profile, "bindingStatus"),
                GetRequiredBool(profile, "isNonBindingElection").ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                GetRequiredString(profile, "thresholdProfile"),
                GetRequiredString(profile, "gateSeverity"),
                GetRequiredString(profile, "gateStatus"),
                GetRequiredString(profile, "claimLevel"),
                JoinClaimProfileWarnings(profile),
                GetRequiredString(profile, "claimWording"),
                GetRequiredString(profile, "limitationWording"));
        }
    }

    private static string EscapeMarkdownTableValue(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string JoinArray(JsonObject item, string propertyName) =>
        string.Join(", ", GetRequiredArray(item, propertyName).Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string JoinClaimProfileWarnings(JsonObject profile)
    {
        var warningCount = GetIntOrDefault(profile, "verifierWarningCount");
        if (warningCount == 0 || profile["verifierWarnings"] is not JsonArray warnings)
        {
            return "none";
        }

        return string.Join(
            "; ",
            warnings
                .Select(node => node?.AsObject())
                .Where(warning => warning is not null)
                .Select(warning =>
                    $"{GetStringOrDefault(warning, "checkCode")} {GetStringOrDefault(warning, "resultCode")}"));
    }

    private static JsonObject? FindClaimLevel(JsonObject register, string claimLevel) =>
        GetRequiredArray(register, "claimLevels")
            .Select(node => node?.AsObject())
            .FirstOrDefault(claim => claim is not null && GetStringOrDefault(claim, "claimLevel") == claimLevel);

    private static JsonObject? FindDimension(JsonObject register, string dimensionId) =>
        GetRequiredArray(register, "dimensions")
            .Select(node => node?.AsObject())
            .FirstOrDefault(dimension => dimension is not null && GetStringOrDefault(dimension, "dimensionId") == dimensionId);

    private static JsonObject? FindBlocker(JsonObject register, string blockerId) =>
        GetRequiredArray(register, "blockers")
            .Select(node => node?.AsObject())
            .FirstOrDefault(blocker => blocker is not null && GetStringOrDefault(blocker, "blockerId") == blockerId);

    private static void ValidateGeneratedViews(IReadOnlyList<PromotedFile> promotedFiles, List<string> errors)
    {
        var publicSummary = Encoding.UTF8.GetString(promotedFiles.Single(x => x.RelativePath == PublicSafeSummaryFileName).Bytes);
        foreach (var forbidden in PublicForbiddenTerms)
        {
            if (publicSummary.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"public-safe-summary.md contains forbidden public-safe term: {forbidden}.");
            }
        }

        if (!publicSummary.Contains("## Current Public-Safe Status", StringComparison.Ordinal) ||
            !publicSummary.Contains("## Non-Claims", StringComparison.Ordinal))
        {
            errors.Add("public-safe-summary.md is missing required sections.");
        }

        var restricted = Encoding.UTF8.GetString(promotedFiles.Single(x => x.RelativePath == RestrictedReviewerExtractFileName).Bytes);
        foreach (var forbidden in new[] { "BEGIN PRIVATE KEY", "password=", "secret=", "credential=" })
        {
            if (restricted.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"restricted-reviewer-extract.md contains forbidden secret marker: {forbidden}.");
            }
        }
    }

    private static JsonObject BuildManifest(
        JsonObject register,
        DateTimeOffset generatedAt,
        IReadOnlyList<PromotedFile> files,
        string archiveFileName,
        long archiveSizeBytes,
        string archiveHash,
        string? manifestHash)
    {
        var fileNodes = new JsonArray();
        foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            fileNodes.Add(new JsonObject
            {
                ["relativePath"] = file.RelativePath,
                ["visibility"] = file.Visibility,
                ["sha256Hash"] = ComputeSha256Hex(file.Bytes),
                ["hashAlgorithm"] = "SHA-256",
                ["mediaType"] = file.MediaType,
                ["sizeBytes"] = file.Bytes.Length,
            });
        }

        return new JsonObject
        {
            ["manifestVersion"] = "1.0",
            ["registerId"] = GetRequiredString(register, "registerId"),
            ["registerVersion"] = GetRequiredString(register, "registerVersion"),
            ["registerVersionId"] = GetRequiredString(register, "registerVersionId"),
            ["status"] = GetRequiredString(register, "status"),
            ["generatedAt"] = generatedAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["sourceCommit"] = GetRequiredString(register, "sourceCommit"),
            ["totalScore"] = GetRequiredInt(GetRequiredObject(register, "score"), "total"),
            ["strongestAllowedClaim"] = GetCurrentStrongestAllowedClaim(register),
            ["strongestAllowedV1PolicyCeiling"] = GetRequiredString(GetRequiredObject(register, "claimPolicy"), "strongestAllowedV1Claim"),
            ["publicationStatus"] = GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus"),
            ["archive"] = new JsonObject
            {
                ["fileName"] = archiveFileName,
                ["sha256Hash"] = archiveHash,
                ["hashAlgorithm"] = "SHA-256",
                ["sizeBytes"] = archiveSizeBytes,
            },
            ["files"] = fileNodes,
            ["manifestHash"] = manifestHash,
        };
    }

    private static void EnsureCatalogAllowsPromotion(
        string catalogPath,
        string registerVersionId,
        string manifestHash,
        string archiveHash)
    {
        if (!File.Exists(catalogPath))
        {
            return;
        }

        var catalog = ReadJsonObject(catalogPath, CatalogFileName);
        if (catalog["entries"] is not JsonArray entries)
        {
            return;
        }

        foreach (var entry in entries.Select(x => x?.AsObject()).Where(x => x is not null))
        {
            if (GetStringOrDefault(entry!, "registerVersionId") != registerVersionId)
            {
                continue;
            }

            if (GetStringOrDefault(entry!, "manifestHash") != manifestHash ||
                GetStringOrDefault(entry!, "archiveHash") != archiveHash)
            {
                throw new ReadinessRegisterPromotionException(
                    "Readiness register catalog already contains this version with different hashes.",
                    [registerVersionId]);
            }
        }
    }

    private static void WritePromotedArtifacts(
        ReadinessRegisterPromotionPaths paths,
        string versionOutputRoot,
        IReadOnlyList<PromotedFile> files,
        string archiveFileName,
        byte[] archiveBytes,
        JsonObject manifest,
        List<string> writtenFiles)
    {
        Directory.CreateDirectory(versionOutputRoot);
        foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var outputPath = Path.Combine(versionOutputRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, file.Bytes);
            writtenFiles.Add(outputPath);
        }

        var archivePath = Path.Combine(versionOutputRoot, archiveFileName);
        File.WriteAllBytes(archivePath, archiveBytes);
        writtenFiles.Add(archivePath);

        Directory.CreateDirectory(paths.OutputRoot);
        var catalogPath = paths.CatalogPath;
        var catalog = File.Exists(catalogPath)
            ? ReadJsonObject(catalogPath, CatalogFileName)
            : new JsonObject
            {
                ["catalogVersion"] = "1.0",
                ["registerId"] = manifest["registerId"]?.DeepClone(),
                ["entries"] = new JsonArray(),
            };

        var entries = catalog["entries"] as JsonArray ?? [];
        catalog["entries"] = entries;
        var registerVersionId = GetRequiredString(manifest, "registerVersionId");
        var existing = entries
            .Select(x => x?.AsObject())
            .FirstOrDefault(x => x is not null && GetStringOrDefault(x, "registerVersionId") == registerVersionId);
        var entryNode = new JsonObject
        {
            ["registerVersion"] = manifest["registerVersion"]?.DeepClone(),
            ["registerVersionId"] = registerVersionId,
            ["status"] = manifest["status"]?.DeepClone(),
            ["generatedAt"] = manifest["generatedAt"]?.DeepClone(),
            ["totalScore"] = manifest["totalScore"]?.DeepClone(),
            ["strongestAllowedClaim"] = manifest["strongestAllowedClaim"]?.DeepClone(),
            ["strongestAllowedV1PolicyCeiling"] = manifest["strongestAllowedV1PolicyCeiling"]?.DeepClone(),
            ["publicationStatus"] = manifest["publicationStatus"]?.DeepClone(),
            ["manifestHash"] = manifest["manifestHash"]?.DeepClone(),
            ["archiveHash"] = manifest["archive"]?["sha256Hash"]?.DeepClone(),
            ["versionPath"] = Path.GetFileName(versionOutputRoot),
        };

        if (existing is null)
        {
            entries.Add(entryNode);
        }
        else
        {
            var index = entries.IndexOf(existing);
            entries[index] = entryNode;
        }

        if (GetRequiredString(manifest, "status") is "AcceptedInternal" or "ReviewerReady")
        {
            catalog["currentRegisterVersionId"] = registerVersionId;
            catalog["currentRegisterVersion"] = manifest["registerVersion"]?.DeepClone();
            catalog["currentManifestHash"] = manifest["manifestHash"]?.DeepClone();
            catalog["currentArchiveHash"] = manifest["archive"]?["sha256Hash"]?.DeepClone();
        }

        File.WriteAllText(catalogPath, SerializeJson(catalog), new UTF8Encoding(false));
        writtenFiles.Add(catalogPath);
    }

    private static List<string> ValidateExistingPromotedArtifacts(
        ReadinessRegisterPromotionPaths paths,
        string versionOutputRoot,
        IReadOnlyList<PromotedFile> files,
        string archiveFileName,
        byte[] archiveBytes,
        JsonObject manifest,
        string manifestHash,
        string archiveHash)
    {
        var errors = new List<string>();
        foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.Combine(versionOutputRoot, file.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing promoted artifact: {file.RelativePath}");
                continue;
            }

            var actualBytes = File.ReadAllBytes(path);
            if (!actualBytes.SequenceEqual(file.Bytes))
            {
                errors.Add($"Promoted artifact mismatch: {file.RelativePath}");
            }
        }

        var archivePath = Path.Combine(versionOutputRoot, archiveFileName);
        if (!File.Exists(archivePath))
        {
            errors.Add($"Missing promoted archive: {archiveFileName}");
        }
        else if (!File.ReadAllBytes(archivePath).SequenceEqual(archiveBytes))
        {
            errors.Add($"Promoted archive mismatch: {archiveFileName}");
        }

        if (!File.Exists(paths.CatalogPath))
        {
            errors.Add($"Missing readiness register catalog: {CatalogFileName}");
        }
        else
        {
            var catalog = ReadJsonObject(paths.CatalogPath, CatalogFileName);
            var registerVersionId = GetRequiredString(manifest, "registerVersionId");
            if (GetStringOrDefault(catalog, "currentRegisterVersionId") != registerVersionId)
            {
                errors.Add($"Catalog currentRegisterVersionId must be {registerVersionId}.");
            }

            if (GetStringOrDefault(catalog, "currentManifestHash") != manifestHash)
            {
                errors.Add($"Catalog currentManifestHash must be {manifestHash}.");
            }

            if (GetStringOrDefault(catalog, "currentArchiveHash") != archiveHash)
            {
                errors.Add($"Catalog currentArchiveHash must be {archiveHash}.");
            }
        }

        return errors;
    }

    private static byte[] BuildDeterministicArchive(IReadOnlyList<PromotedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.NoCompression);
                entry.LastWriteTime = FixedZipTimestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.Bytes, 0, file.Bytes.Length);
            }
        }

        return stream.ToArray();
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SerializeJson(JsonNode node) => NormalizeLineEndings(node.ToJsonString(ReadableJsonOptions)) + "\n";

    private static byte[] EncodingWithoutBom(string value) => new UTF8Encoding(false).GetBytes(NormalizeLineEndings(value));

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static string GetPromotedFileContent(IReadOnlyList<PromotedFile> promotedFiles, string relativePath) =>
        Encoding.UTF8.GetString(promotedFiles.Single(file => file.RelativePath == relativePath).Bytes);

    private static string GetCurrentStrongestAllowedClaim(JsonObject register)
    {
        var allowedStatuses = new HashSet<string>(["allowed", "allowed_with_limitations"], StringComparer.Ordinal);
        var claimRank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["internal_development"] = 0,
            ["internal_non_binding_rehearsal"] = 1,
            ["friendly_organization_pilot"] = 2,
            ["production_organizational_rollout"] = 3,
            ["public_or_state_election"] = 4,
        };

        return GetRequiredArray(register, "claimLevels")
            .Select(x => x!.AsObject())
            .Where(claim =>
                allowedStatuses.Contains(GetRequiredString(claim, "status")) &&
                GetRequiredString(claim, "blockerSeverity") != "red")
            .OrderByDescending(claim => claimRank.GetValueOrDefault(GetRequiredString(claim, "claimLevel"), -1))
            .Select(claim => GetRequiredString(claim, "claimLevel"))
            .FirstOrDefault() ?? "none";
    }

    private static void ScaffoldMissingSourceFiles(ReadinessRegisterPromotionPaths paths)
    {
        Directory.CreateDirectory(paths.SourceRoot);
        if (!File.Exists(paths.SchemaPath))
        {
            File.WriteAllText(paths.SchemaPath, "{\n  \"$schema\": \"https://json-schema.org/draft/2020-12/schema\"\n}\n", new UTF8Encoding(false));
        }

        if (!File.Exists(paths.RegisterPath))
        {
            File.WriteAllText(paths.RegisterPath, "{\n  \"schemaVersion\": \"1.0\"\n}\n", new UTF8Encoding(false));
        }

        if (!File.Exists(paths.ExamplePath))
        {
            File.WriteAllText(paths.ExamplePath, "{\n  \"evidenceItems\": [],\n  \"exceptions\": []\n}\n", new UTF8Encoding(false));
        }
    }

    private static void RequireFixed(JsonObject item, string propertyName, string expected, List<string> errors)
    {
        if (GetStringOrDefault(item, propertyName) != expected)
        {
            errors.Add($"{propertyName} must be {expected}.");
        }
    }

    private static void RequirePattern(JsonObject item, string propertyName, Regex pattern, List<string> errors)
    {
        var value = GetStringOrDefault(item, propertyName);
        if (string.IsNullOrWhiteSpace(value) || !pattern.IsMatch(value))
        {
            errors.Add($"{propertyName} has invalid format.");
        }
    }

    private static void RequireEnum(JsonObject item, string propertyName, HashSet<string> allowed, List<string> errors)
    {
        var value = GetStringOrDefault(item, propertyName);
        if (string.IsNullOrWhiteSpace(value) || !allowed.Contains(value))
        {
            errors.Add($"{propertyName} has unsupported value {value}.");
        }
    }

    private static void RequireNonEmpty(JsonObject item, string propertyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetStringOrDefault(item, propertyName)))
        {
            errors.Add($"{propertyName} is required.");
        }
    }

    private static JsonObject? RequireObject(JsonObject item, string propertyName, List<string> errors)
    {
        if (item[propertyName] is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{propertyName} must be an object.");
        return null;
    }

    private static JsonArray? RequireArray(JsonObject item, string propertyName, List<string> errors)
    {
        if (item[propertyName] is JsonArray array)
        {
            return array;
        }

        errors.Add($"{propertyName} must be an array.");
        return null;
    }

    private static int RequireInt(JsonObject item, string propertyName, List<string> errors)
    {
        if (item[propertyName] is JsonValue value &&
            value.TryGetValue<int>(out var result))
        {
            return result;
        }

        errors.Add($"{propertyName} must be an integer.");
        return 0;
    }

    private static JsonObject GetRequiredObject(JsonObject item, string propertyName) => item[propertyName]!.AsObject();
    private static JsonArray GetRequiredArray(JsonObject item, string propertyName) => item[propertyName]!.AsArray();
    private static string GetRequiredString(JsonObject item, string propertyName) => item[propertyName]!.GetValue<string>();
    private static int GetRequiredInt(JsonObject item, string propertyName) => item[propertyName]!.GetValue<int>();
    private static bool GetRequiredBool(JsonObject item, string propertyName) => item[propertyName]!.GetValue<bool>();

    private static string GetStringOrDefault(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : string.Empty;

    private static int GetIntOrDefault(JsonObject item, string propertyName) =>
        item[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : 0;

    private static bool GetBoolOrDefault(JsonObject item, string propertyName) =>
        item[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private sealed record Feat156PromotionApplication(DateTimeOffset GeneratedAt);
    private sealed record InternalAudit95PromotionApplication(DateTimeOffset GeneratedAt);
    private sealed record DevelopmentProfileClarificationApplication(DateTimeOffset GeneratedAt);
    private sealed record OperationalChecklistRow(
        string Environment,
        string SubChecklist,
        string StageStatus,
        string Responsibility,
        string EvidenceOrChecks,
        string ClaimImpact);

    private sealed record ProfileEvidenceProcedureRow(
        string CheckId,
        string PageFileName,
        string Scope,
        string CheckPerformed,
        string DatabaseRecordSurface,
        string EvidenceOrProofArtifacts,
        string PassRule,
        string DevelopmentApplicability,
        string DevelopmentCheck,
        string PreProductionApplicability,
        string ProductionApplicability,
        string V018Timing,
        string V018EvidenceResult,
        string FutureEvidenceTiming);

    private sealed record ExternalAuditorEntryPointRow(
        string Stage,
        string Question,
        string PrimaryRecords,
        string ArtifactsOrProofs,
        string VerifierChecks,
        string BlockingCondition);

    private sealed record ClaimProfileVerifierWarning(
        string CheckCode,
        string ResultCode,
        string Message,
        string EvidenceRef);

    private sealed record PromotedFile(
        string RelativePath,
        string Visibility,
        byte[] Bytes,
        string MediaType);
}

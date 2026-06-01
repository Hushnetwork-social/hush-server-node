namespace ReadinessRegisterPromoter;

internal sealed record InternalAuditHardeningTask(
    string DimensionId,
    int TargetScore,
    string BlockerId,
    string FeatureId,
    string Description,
    string ResolutionCriteria);

internal static class InternalAudit95ReadinessPlan
{
    public const int TargetScore = 95;
    public const string TargetVersion = "v0.1.7";
    public const string PublicationStatus = "pilot_only_with_limitations";

    public static readonly InternalAuditHardeningTask[] Tasks =
    [
        new(
            "RDY-DIM-001",
            10,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM001-001",
            "FEAT-157",
            "Protocol/spec/proof package needs external-review-ready traceability, stale-reference checks, and release-bound proof-package completeness before the internal audit can claim 95+ confidence.",
            "Raise RDY-DIM-001 to 10 by publishing an auditor trace matrix, automated stale-reference validation, and a complete proof-package inventory bound to the promoted release."),
        new(
            "RDY-DIM-002",
            10,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM002-001",
            "FEAT-158",
            "Verifier sample and tamper corpus needs broader election-shape coverage and a reproducible CI verifier run before the internal audit can claim 95+ confidence.",
            "Raise RDY-DIM-002 to 10 by adding multi-shape sample elections, negative tamper cases, CI-published verifier results, and reviewer instructions that reproduce the corpus outcome."),
        new(
            "RDY-DIM-003",
            10,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM003-001",
            "FEAT-159",
            "Receipt/inclusion verification needs physical QR/camera, compact-code, browser, and mobile coverage beyond the current package-bound file channel.",
            "Raise RDY-DIM-003 to 10 by producing cross-device evidence for QR/camera, compact-code/manual lookup, desktop, mobile, and consumed-status verification paths."),
        new(
            "RDY-DIM-004",
            10,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM004-001",
            "FEAT-160",
            "Publication/counting evidence needs repeated deterministic replay across multiple election profiles and verifier-output binding before the internal audit can claim 95+ confidence.",
            "Raise RDY-DIM-004 to 10 by publishing replay evidence for multiple election shapes, package hashes, tally verification, and mismatch/tamper rejection outputs."),
        new(
            "RDY-DIM-005",
            9,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM005-001",
            "FEAT-161",
            "KMS custody lifecycle needs IAM drift, key rotation/recovery, and regional failure rehearsal evidence to move beyond a single accepted custody package.",
            "Raise RDY-DIM-005 to 9 by producing IAM drift scans, key recovery/rotation rehearsal results, and fail-closed evidence for provider or regional failure scenarios."),
        new(
            "RDY-DIM-006",
            9,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM006-001",
            "FEAT-162",
            "Trusted deployment ceremony needs repeated deployment, rollback, emergency-change, and artifact-binding evidence before an external reviewer sees enough operating confidence.",
            "Raise RDY-DIM-006 to 9 by running a second trusted deployment ceremony with rollback/emergency-change rehearsal and signed artifact-to-runtime binding."),
        new(
            "RDY-DIM-007",
            10,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM007-001",
            "FEAT-163",
            "Operational evidence package needs repeated production-like runs, backup/restore, monitoring, dependency, support, and incident-response evidence.",
            "Raise RDY-DIM-007 to 10 by producing a second production-like run package with backup/restore, monitoring alerts, dependency/support evidence, and incident-response walkthrough."),
        new(
            "RDY-DIM-008",
            9,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM008-001",
            "FEAT-164",
            "Retention/log privacy proof needs recurring automated scans and observability drift checks so new diagnostics cannot silently reintroduce correlation risk.",
            "Raise RDY-DIM-008 to 9 by adding recurring log/privacy scans, observability drift checks, and a reviewer-readable privacy regression report."),
        new(
            "RDY-DIM-009",
            9,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM009-001",
            "FEAT-165",
            "Dispute/continuity readiness needs a scenario matrix for void, failed-finalize, anomaly, replacement-publication, and verifier challenge paths.",
            "Raise RDY-DIM-009 to 9 by producing a multi-scenario continuity and dispute package with deterministic verification outputs and customer-owned remedy boundaries."),
        new(
            "RDY-DIM-010",
            9,
            "RDY-BLOCK-INTERNAL_AUDIT_95_DIM010-001",
            "FEAT-166",
            "Governance wrapper needs an auditor-facing customer handoff pack that separates Hush technical evidence from external authority, legal, and public/state prerequisites.",
            "Raise RDY-DIM-010 to 9 by publishing a restricted reviewer handoff pack with responsibility matrix, non-claims, external-prerequisite routing, and customer-governance checklist."),
    ];
}

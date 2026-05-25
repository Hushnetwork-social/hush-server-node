namespace HushShared.Elections.Verification.Model;

public static class AbnormalFinalizationVerificationIds
{
    public const string ArtifactSchemaId = "hushvoting-abnormal-finalization-evidence-v1";

    public const string OutcomeStatusCleanFinalized = "clean_finalized";
    public const string OutcomeStatusFinalizedWithAnomaly = "finalized_with_anomaly";

    public const string FinalizationModeClean = "clean_finalization";
    public const string FinalizationModeAbnormal = "abnormal_finalization";

    public const string OfficialResultSourceCopiedFromFixedUnofficial = "copied_from_fixed_unofficial_result";

    public const string EvidenceValidCheckCode = "VFY-ABNORMAL-000";
    public const string EvidenceMissingCheckCode = "VFY-ABNORMAL-001";
    public const string EvidenceInvalidCheckCode = "VFY-ABNORMAL-002";
}

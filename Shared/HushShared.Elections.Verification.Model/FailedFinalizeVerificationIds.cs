namespace HushShared.Elections.Verification.Model;

public static class FailedFinalizeVerificationIds
{
    public const string PublicStatusSchemaId = "hushvoting-failed-finalize-public-status-v1";
    public const string VerifierResultSchemaId = "hushvoting-failed-finalize-verifier-result-v1";
    public const string PackageManifestSchemaId = "hushvoting-failed-finalize-package-manifest-v1";

    public const string OutcomeStatusFailedToFinalize = "failed_to_finalize";
    public const string FinalizationModeFailedFinalization = "failed_finalization";
    public const string PackageStatusAccepted = "accepted";
    public const string OfficialResultAbsent = "official_result_absent";
    public const string CleanFinalPackageAbsent = "clean_final_package_absent";
    public const string FinalizeBoundaryAbsent = "finalize_boundary_absent";

    public const string ManifestValidCheckCode = "VFY-FAILED-FINALIZE-MAN-000";
    public const string ManifestInvalidCheckCode = "VFY-FAILED-FINALIZE-MAN-001";
    public const string ConsistencyValidCheckCode = "VFY-FAILED-FINALIZE-000";
    public const string ConsistencyInvalidCheckCode = "VFY-FAILED-FINALIZE-001";
    public const string NoCleanResultCheckCode = "VFY-FAILED-FINALIZE-002";
}

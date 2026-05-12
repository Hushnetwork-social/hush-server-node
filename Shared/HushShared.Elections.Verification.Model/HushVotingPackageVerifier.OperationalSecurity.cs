namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckOperationalSecurityEvidenceAsync(
        string packagePath,
        AuditPackageManifestRecord auditManifest,
        string profileId,
        ElectionRecordReferenceRecord electionRecord,
        CancellationToken cancellationToken)
    {
        var requiredPaths = new[]
        {
            VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
            VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
            VerificationPackageFileNames.Sp10OperationalVerifierOutput,
        };
        var missingFiles = requiredPaths
            .Where(path => !File.Exists(ResolvePackagePath(packagePath, path)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            return
            [
                CreateResult(
                    ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
                    GetRequiredOperationalEvidenceStatus(profileId, isFalseClaim: false),
                    VerificationResultCodes.OperationalSecurityEvidenceMissing,
                    string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal)
                        ? "High-assurance profile requires SP-10 operational security artifacts."
                        : "SP-10 operational security artifacts are not available.",
                    missingFiles.ToDictionary(path => path, _ => "missing", StringComparer.Ordinal)),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        var status = await ReadJsonAsync<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            cancellationToken);
        var deployment = await ReadJsonAsync<ElectionSp10OperationalDeploymentEvidenceArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
            cancellationToken);
        var custody = await ReadJsonAsync<ElectionSp10OperationalCustodyEvidenceArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
            cancellationToken);
        var verifierOutput = await ReadJsonAsync<ElectionSp10OperationalVerifierOutputArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp10OperationalVerifierOutput,
            cancellationToken);
        var releaseIntegrity = File.Exists(ResolvePackagePath(packagePath, VerificationPackageFileNames.Sp08ReleaseIntegrity))
            ? await ReadJsonAsync<ElectionSp08ReleaseIntegrityArtifactRecord>(
                packagePath,
                VerificationPackageFileNames.Sp08ReleaseIntegrity,
                cancellationToken)
            : null;

        ValidateSp10PackageMembership(results, auditManifest, requiredPaths);
        ValidateSp10Shape(results, status, deployment, custody, verifierOutput, profileId, electionRecord);
        ValidateSp10ContractRules(results, status, auditManifest.PackageView);

        results.Add(BuildSp10DeploymentProfileResult(status));
        results.Add(BuildSp10ReleaseDeploymentBindingResult(profileId, status, deployment, releaseIntegrity));
        results.Add(BuildSp10AccessSnapshotResult(packagePath, auditManifest, profileId, status));
        results.Add(BuildSp10CustodyModeResult(profileId, status, custody));
        results.Add(BuildSp10ExecutorKeyLifecycleResult(profileId, status, custody));
        results.Add(BuildSp10ForbiddenMaterialResult(status, deployment, custody, verifierOutput));
        results.Add(BuildSp10BackupRestoreResult(packagePath, auditManifest, profileId, status));
        results.Add(BuildSp10IncidentDeclarationResult(packagePath, auditManifest, profileId, status));
        results.Add(BuildSp10AuditorRoomResult(packagePath, auditManifest, profileId, status));

        return results;
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckRegulatoryClaimEvidenceAsync(
        string packagePath,
        AuditPackageManifestRecord auditManifest,
        CancellationToken cancellationToken)
    {
        var claimPath = ResolvePackagePath(packagePath, VerificationPackageFileNames.Sp11RegulatoryClaimState);
        var claimFileExists = File.Exists(claimPath);
        var claimManifestEntryExists = auditManifest.Entries.Any(x =>
            string.Equals(x.Path, VerificationPackageFileNames.Sp11RegulatoryClaimState, StringComparison.Ordinal));
        if (!claimFileExists && !claimManifestEntryExists)
        {
            return [];
        }

        if (!claimFileExists)
        {
            return
            [
                CreateResult(
                    ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.RegulatoryClaimNotLegalApproval,
                    "SP-11 regulatory claim artifact is declared but missing."),
            ];
        }

        var results = new List<VerifierCheckResultRecord>();
        var claim = await ReadJsonAsync<ElectionSp11RegulatoryClaimStateArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp11RegulatoryClaimState,
            cancellationToken);

        ValidateRegulatoryClaimPackageMembership(results, auditManifest);
        results.Add(BuildRegulatoryShapeResult(claim));
        results.Add(BuildRegulatoryAllowedByRegisterResult(claim));
        results.Add(BuildRegulatoryBlockedCertificationResult(claim));

        if (ElectionSp11RegulatoryRules.IsTrackerStale(claim, DateTimeOffset.UtcNow))
        {
            results.Add(CreateResult(
                ElectionSp11ProfileIds.StaleTrackerWarningCheckCode,
                VerificationCheckStatus.Warn,
                VerificationResultCodes.RegulatoryTrackerStale,
                "SP-11 regulatory tracker next-review date is stale; this package must not support new regulatory claims.",
                new Dictionary<string, string>
                {
                    ["source_checked_at"] = claim.SourceCheckedAt.ToString("O"),
                    ["next_review_at"] = claim.NextReviewAt.ToString("O"),
                }));
        }
        else
        {
            results.Add(CreateResult(
                ElectionSp11ProfileIds.StaleTrackerWarningCheckCode,
                VerificationCheckStatus.Pass,
                VerificationResultCodes.RegulatoryClaimShapeValid,
                "SP-11 regulatory tracker source and next-review dates are current for this package.",
                new Dictionary<string, string>
                {
                    ["source_checked_at"] = claim.SourceCheckedAt.ToString("O"),
                    ["next_review_at"] = claim.NextReviewAt.ToString("O"),
                }));
        }

        results.Add(BuildRegulatoryRestrictedWorkpaperBoundaryResult(claim, auditManifest));
        return results;
    }

    private static void ValidateSp10PackageMembership(
        List<VerifierCheckResultRecord> results,
        AuditPackageManifestRecord auditManifest,
        IReadOnlyList<string> requiredPaths)
    {
        var manifestPaths = auditManifest.Entries
            .Select(x => x.Path)
            .ToHashSet(StringComparer.Ordinal);
        var missingEntries = requiredPaths
            .Where(path => !manifestPaths.Contains(path))
            .ToArray();
        if (missingEntries.Length == 0)
        {
            return;
        }

        results.Add(CreateResult(
            ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
            VerificationCheckStatus.Fail,
            VerificationResultCodes.OperationalSecurityEvidenceMissing,
            "SP-10 operational security files must be listed in the audit package manifest.",
            missingEntries.ToDictionary(path => path, _ => "missing_manifest_entry", StringComparer.Ordinal)));
    }

    private static void ValidateSp10Shape(
        List<VerifierCheckResultRecord> results,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        ElectionSp10OperationalDeploymentEvidenceArtifactRecord deployment,
        ElectionSp10OperationalCustodyEvidenceArtifactRecord custody,
        ElectionSp10OperationalVerifierOutputArtifactRecord verifierOutput,
        string profileId,
        ElectionRecordReferenceRecord electionRecord)
    {
        if (!string.Equals(status.ElectionId, electionRecord.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(deployment.ElectionId, electionRecord.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(custody.ElectionId, electionRecord.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(verifierOutput.ElectionId, electionRecord.ElectionId, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ElectionIdMismatch,
                "SP-10 operational security election id does not match the package election id."));
        }

        if (!string.Equals(status.Schema, ElectionSp10ProfileIds.OperationalSecuritySummarySchema, StringComparison.Ordinal) ||
            !string.Equals(deployment.Schema, ElectionSp10ProfileIds.OperationalDeploymentEvidenceSchema, StringComparison.Ordinal) ||
            !string.Equals(custody.Schema, ElectionSp10ProfileIds.OperationalCustodyEvidenceSchema, StringComparison.Ordinal) ||
            !string.Equals(verifierOutput.Schema, ElectionSp10ProfileIds.OperationalVerifierOutputSchema, StringComparison.Ordinal) ||
            !string.Equals(status.ProgramVersion, ElectionSp10ProfileIds.OperationalSecurityProgramVersion, StringComparison.Ordinal) ||
            !string.Equals(deployment.ProgramVersion, ElectionSp10ProfileIds.OperationalSecurityProgramVersion, StringComparison.Ordinal) ||
            !string.Equals(custody.ProgramVersion, ElectionSp10ProfileIds.OperationalSecurityProgramVersion, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityEvidenceMissing,
                "SP-10 operational security schema or program version is invalid."));
        }

        if (!string.Equals(status.DeploymentProfileId, deployment.DeploymentProfileId, StringComparison.Ordinal) ||
            !string.Equals(status.EvidenceState, deployment.EvidenceState, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityProfileDeclared,
                "SP-10 deployment evidence does not match the operational security summary."));
        }

        if (!string.Equals(verifierOutput.VerifierProfileId, profileId, StringComparison.Ordinal))
        {
            results.Add(CreateResult(
                ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.VerifierProfilePackageMismatch,
                "SP-10 verifier output profile does not match the requested verifier profile."));
        }

        var missingCheckCodes = ElectionSp10ProfileIds.OperationalCheckCodes
            .Where(code => verifierOutput.Results.All(x => !string.Equals(x.CheckCode, code, StringComparison.Ordinal)))
            .ToArray();
        if (missingCheckCodes.Length > 0)
        {
            results.Add(CreateResult(
                ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityEvidenceMissing,
                "SP-10 verifier output is missing required OPS check codes.",
                missingCheckCodes.ToDictionary(code => code, _ => "missing", StringComparer.Ordinal)));
        }
    }

    private static void ValidateSp10ContractRules(
        List<VerifierCheckResultRecord> results,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        VerificationPackageView packageView)
    {
        foreach (var error in ElectionSp10OperationalSecurityRules.Validate(status, packageView))
        {
            results.Add(MapSp10ValidationError(error));
        }
    }

    private static VerifierCheckResultRecord BuildSp10DeploymentProfileResult(
        ElectionSp10OperationalSecurityStatusArtifactRecord status)
    {
        var valid = string.Equals(
                status.DeploymentProfileId,
                ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
                StringComparison.Ordinal) &&
            ElectionSp10OperationalSecurityRules.IsSupportedEvidenceState(status.EvidenceState);

        return CreateResult(
            ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
            valid ? VerificationCheckStatus.Pass : VerificationCheckStatus.Fail,
            valid
                ? VerificationResultCodes.OperationalSecurityProfileDeclared
                : VerificationResultCodes.OperationalSecurityEvidenceMissing,
            valid
                ? "SP-10 managed deployment profile is declared without implying FEAT-106 readiness."
                : "SP-10 deployment profile or evidence state is unsupported.",
            new Dictionary<string, string>
            {
                ["deployment_profile_id"] = status.DeploymentProfileId,
                ["evidence_state"] = status.EvidenceState,
            });
    }

    private static VerifierCheckResultRecord BuildSp10ReleaseDeploymentBindingResult(
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        ElectionSp10OperationalDeploymentEvidenceArtifactRecord deployment,
        ElectionSp08ReleaseIntegrityArtifactRecord? releaseIntegrity)
    {
        var missingBinding =
            string.IsNullOrWhiteSpace(status.ReleaseManifestHash) ||
            string.IsNullOrWhiteSpace(status.ImmutableDeploymentRef) ||
            string.IsNullOrWhiteSpace(deployment.ReleaseManifestHash) ||
            string.IsNullOrWhiteSpace(deployment.ImmutableDeploymentRef);
        var mismatchedBinding =
            !string.Equals(status.ReleaseManifestHash, deployment.ReleaseManifestHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(status.ImmutableDeploymentRef, deployment.ImmutableDeploymentRef, StringComparison.Ordinal);
        var releaseMismatch = releaseIntegrity is not null &&
            !string.Equals(status.ReleaseManifestHash, releaseIntegrity.ReleaseManifestHash, StringComparison.OrdinalIgnoreCase);
        var mutableDeploymentRef = !string.IsNullOrWhiteSpace(status.ImmutableDeploymentRef) &&
            ElectionSp08ReleaseIntegrityRules.IsMutableOrLocalReference(status.ImmutableDeploymentRef);
        var hasEvidence = !missingBinding && !mismatchedBinding && !releaseMismatch && !mutableDeploymentRef;

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.ReleaseDeploymentBindingCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityReleaseBindingMissing,
            "SP-10 deployment evidence is bound to the SP-08 release manifest and immutable deployment reference.",
            "SP-10 deployment evidence is missing, mutable, or inconsistent with SP-08 release integrity.",
            new Dictionary<string, string>
            {
                ["release_manifest_hash"] = status.ReleaseManifestHash ?? string.Empty,
                ["immutable_deployment_ref"] = status.ImmutableDeploymentRef ?? string.Empty,
                ["release_binding_matches_sp08"] = (!releaseMismatch).ToString(),
            });
    }

    private static VerifierCheckResultRecord BuildSp10AccessSnapshotResult(
        string packagePath,
        AuditPackageManifestRecord auditManifest,
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status)
    {
        var restrictedFileExists = auditManifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor &&
            File.Exists(ResolvePackagePath(packagePath, VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot));
        var hasEvidence = !string.IsNullOrWhiteSpace(status.AccessSnapshotHashOrRestrictedRef) || restrictedFileExists;

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.AccessControlSnapshotCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityAccessSnapshotMissing,
            "SP-10 access-control snapshot evidence is present or referenced.",
            "SP-10 access-control snapshot evidence is missing.",
            BuildOptionalEvidence("access_snapshot_ref", status.AccessSnapshotHashOrRestrictedRef));
    }

    private static VerifierCheckResultRecord BuildSp10CustodyModeResult(
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        ElectionSp10OperationalCustodyEvidenceArtifactRecord custody)
    {
        var hasEvidence =
            ElectionSp10ProfileIds.CustodyModeSet.Contains(status.CustodyMode ?? string.Empty) &&
            string.Equals(status.CustodyMode, custody.CustodyMode, StringComparison.Ordinal);

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.CustodyModeDeclaredCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityCustodyModeMissing,
            "SP-10 custody mode is declared for the governance mode.",
            "SP-10 custody mode is missing, unsupported, or inconsistent.",
            BuildOptionalEvidence("custody_mode", status.CustodyMode));
    }

    private static VerifierCheckResultRecord BuildSp10ExecutorKeyLifecycleResult(
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        ElectionSp10OperationalCustodyEvidenceArtifactRecord custody)
    {
        var hasEvidence =
            string.Equals(
                status.ExecutorKeyLifecycle,
                ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1,
                StringComparison.Ordinal) &&
            string.Equals(status.ExecutorKeyLifecycle, custody.ExecutorKeyLifecycle, StringComparison.Ordinal);

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.ExecutorKeyLifecycleCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityExecutorKeyLifecycleMissing,
            "SP-10 executor key lifecycle is declared as ephemeral in-memory handling.",
            "SP-10 executor key lifecycle evidence is missing or unsupported.",
            BuildOptionalEvidence("executor_key_lifecycle", status.ExecutorKeyLifecycle));
    }

    private static VerifierCheckResultRecord BuildSp10ForbiddenMaterialResult(
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        ElectionSp10OperationalDeploymentEvidenceArtifactRecord deployment,
        ElectionSp10OperationalCustodyEvidenceArtifactRecord custody,
        ElectionSp10OperationalVerifierOutputArtifactRecord verifierOutput)
    {
        var forbiddenBoundaryFields = VerificationPrivacyBoundary.FindForbiddenPublicFields(
            status.PublicPrivacyBoundary
                .Concat(deployment.PublicPrivacyBoundary)
                .Concat(custody.PublicPrivacyBoundary));
        var forbiddenWording = new List<string>();
        AddForbiddenOperationalWording(forbiddenWording, "feat106_readiness_caveat", status.Feat106ReadinessCaveat);
        AddForbiddenOperationalWording(forbiddenWording, "primary_issue", status.PrimaryIssue);
        foreach (var result in verifierOutput.Results)
        {
            AddForbiddenOperationalWording(forbiddenWording, result.CheckCode, result.Message);
        }

        if (status.DoesNotCompleteFeat106Readiness &&
            forbiddenBoundaryFields.Count == 0 &&
            forbiddenWording.Count == 0)
        {
            return CreateResult(
                ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode,
                VerificationCheckStatus.Pass,
                VerificationResultCodes.OperationalSecurityEvidenceValid,
                "SP-10 public privacy boundary and wording exclude forbidden operational, readiness, legal, and certification material.");
        }

        var evidence = forbiddenBoundaryFields
            .ToDictionary(field => field, _ => "forbidden_boundary_field", StringComparer.OrdinalIgnoreCase);
        foreach (var phrase in forbiddenWording)
        {
            evidence[phrase] = "forbidden_wording";
        }

        if (!status.DoesNotCompleteFeat106Readiness)
        {
            evidence["does_not_complete_feat106_readiness"] = "false";
        }

        return CreateResult(
            ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode,
            VerificationCheckStatus.Fail,
            VerificationResultCodes.OperationalSecurityForbiddenMaterial,
            "SP-10 public material contains forbidden operational data or false readiness, legal, public-election, or certification wording.",
            evidence);
    }

    private static VerifierCheckResultRecord BuildSp10BackupRestoreResult(
        string packagePath,
        AuditPackageManifestRecord auditManifest,
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status)
    {
        var restrictedFileExists = auditManifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor &&
            File.Exists(ResolvePackagePath(packagePath, VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence));
        var hasEvidence = !string.IsNullOrWhiteSpace(status.BackupRestoreHashOrRestrictedRef) || restrictedFileExists;

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.BackupRestoreEvidenceCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityBackupRestoreMissing,
            "SP-10 backup/restore evidence is referenced.",
            "SP-10 backup/restore evidence is missing.",
            BuildOptionalEvidence("backup_restore_ref", status.BackupRestoreHashOrRestrictedRef));
    }

    private static VerifierCheckResultRecord BuildSp10IncidentDeclarationResult(
        string packagePath,
        AuditPackageManifestRecord auditManifest,
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status)
    {
        var restrictedFileExists = auditManifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor &&
            File.Exists(ResolvePackagePath(packagePath, VerificationPackageFileNames.RestrictedSp10IncidentEvidence));
        var hasEvidence =
            ElectionSp10ProfileIds.IncidentStatusSet.Contains(status.IncidentStatus ?? string.Empty) ||
            restrictedFileExists;

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.IncidentDeclarationCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityIncidentDeclarationMissing,
            "SP-10 incident/no-incident declaration is present.",
            "SP-10 incident/no-incident declaration is missing or unsupported.",
            BuildOptionalEvidence("incident_status", status.IncidentStatus));
    }

    private static VerifierCheckResultRecord BuildSp10AuditorRoomResult(
        string packagePath,
        AuditPackageManifestRecord auditManifest,
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status)
    {
        var restrictedFileExists = auditManifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor &&
            File.Exists(ResolvePackagePath(packagePath, VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog));
        var hasEvidence = !string.IsNullOrWhiteSpace(status.AuditorRoomAccessLogHashOrRestrictedRef) || restrictedFileExists;

        return BuildSp10EvidenceResult(
            profileId,
            status,
            ElectionSp10ProfileIds.AuditorRoomAccessLogCheckCode,
            hasEvidence,
            VerificationResultCodes.OperationalSecurityAuditorRoomMissing,
            "SP-10 auditor-room access-log evidence is referenced.",
            "SP-10 auditor-room access-log evidence is missing.",
            BuildOptionalEvidence("auditor_room_access_log_ref", status.AuditorRoomAccessLogHashOrRestrictedRef));
    }

    private static VerifierCheckResultRecord BuildSp10EvidenceResult(
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        string checkCode,
        bool hasEvidence,
        string missingResultCode,
        string passMessage,
        string blockedMessage,
        IReadOnlyDictionary<string, string>? evidence = null)
    {
        var checkStatus = GetOperationalEvidenceCheckStatus(profileId, status, hasEvidence);
        var resultCode = checkStatus == VerificationCheckStatus.Pass
            ? VerificationResultCodes.OperationalSecurityEvidenceValid
            : hasEvidence
                ? ElectionSp10OperationalSecurityRules.GetPrimaryResultCode(status.EvidenceState)
                : missingResultCode;
        return CreateResult(
            checkCode,
            checkStatus,
            resultCode,
            checkStatus == VerificationCheckStatus.Pass
                ? passMessage
                : blockedMessage,
            evidence);
    }

    private static VerificationCheckStatus GetOperationalEvidenceCheckStatus(
        string profileId,
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        bool hasEvidence)
    {
        var highAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        var evidenceAvailable = ElectionSp10OperationalSecurityRules.IsHighAssuranceOperationalClaimAllowed(
            status.EvidenceState);
        if (!hasEvidence)
        {
            return highAssurance || evidenceAvailable
                ? VerificationCheckStatus.Fail
                : VerificationCheckStatus.Warn;
        }

        if (evidenceAvailable)
        {
            return VerificationCheckStatus.Pass;
        }

        return highAssurance
            ? VerificationCheckStatus.Fail
            : VerificationCheckStatus.Warn;
    }

    private static VerificationCheckStatus GetRequiredOperationalEvidenceStatus(
        string profileId,
        bool isFalseClaim) =>
        isFalseClaim || string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal)
            ? VerificationCheckStatus.Fail
            : VerificationCheckStatus.Warn;

    private static VerifierCheckResultRecord MapSp10ValidationError(string error)
    {
        var lower = error.ToLowerInvariant();
        if (lower.Contains("public", StringComparison.Ordinal) ||
            lower.Contains("restricted", StringComparison.Ordinal) ||
            lower.Contains("privacy boundary", StringComparison.Ordinal) ||
            lower.Contains("readiness", StringComparison.Ordinal) ||
            lower.Contains("certification", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityForbiddenMaterial,
                error);
        }

        if (lower.Contains("release manifest", StringComparison.Ordinal) ||
            lower.Contains("deployment reference", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.ReleaseDeploymentBindingCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityReleaseBindingMissing,
                error);
        }

        if (lower.Contains("access-control", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.AccessControlSnapshotCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityAccessSnapshotMissing,
                error);
        }

        if (lower.Contains("custody mode", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.CustodyModeDeclaredCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityCustodyModeMissing,
                error);
        }

        if (lower.Contains("executor", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.ExecutorKeyLifecycleCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityExecutorKeyLifecycleMissing,
                error);
        }

        if (lower.Contains("backup/restore", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.BackupRestoreEvidenceCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityBackupRestoreMissing,
                error);
        }

        if (lower.Contains("incident", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.IncidentDeclarationCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityIncidentDeclarationMissing,
                error);
        }

        if (lower.Contains("auditor-room", StringComparison.Ordinal))
        {
            return CreateResult(
                ElectionSp10ProfileIds.AuditorRoomAccessLogCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.OperationalSecurityAuditorRoomMissing,
                error);
        }

        return CreateResult(
            ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode,
            VerificationCheckStatus.Fail,
            VerificationResultCodes.OperationalSecurityEvidenceMissing,
            error);
    }

    private static void AddForbiddenOperationalWording(
        List<string> forbidden,
        string evidenceKey,
        string? text)
    {
        if (ElectionSp10OperationalSecurityRules.ContainsForbiddenClaimPhrase(text))
        {
            forbidden.Add(evidenceKey);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildOptionalEvidence(
        string key,
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [key] = value };

    private static void ValidateRegulatoryClaimPackageMembership(
        List<VerifierCheckResultRecord> results,
        AuditPackageManifestRecord auditManifest)
    {
        if (auditManifest.Entries.Any(x =>
                string.Equals(x.Path, VerificationPackageFileNames.Sp11RegulatoryClaimState, StringComparison.Ordinal)))
        {
            return;
        }

        results.Add(CreateResult(
            ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode,
            VerificationCheckStatus.Fail,
            VerificationResultCodes.RegulatoryClaimNotLegalApproval,
            "SP-11 regulatory claim state exists but is not listed in the audit package manifest."));
    }

    private static VerifierCheckResultRecord BuildRegulatoryShapeResult(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim)
    {
        var errors = new List<string>();
        if (!string.Equals(claim.Schema, ElectionSp11ProfileIds.RegulatoryClaimStateSchema, StringComparison.Ordinal) ||
            !string.Equals(claim.TrackerVersion, ElectionSp11ProfileIds.RegulatoryTrackerVersion, StringComparison.Ordinal) ||
            !ElectionSp11RegulatoryRules.IsSupportedClaimState(claim.ClaimState))
        {
            errors.Add("shape");
        }

        if (claim.IsLegalAdvice ||
            ElectionSp11RegulatoryRules.ContainsForbiddenClaimPhrase(claim.AllowedWording))
        {
            errors.Add("legal_or_forbidden_wording");
        }

        if (errors.Count == 0)
        {
            return CreateResult(
                ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode,
                VerificationCheckStatus.Pass,
                VerificationResultCodes.RegulatoryClaimShapeValid,
                "SP-11 regulatory claim artifact has the expected shape and non-legal-advice boundary.",
                new Dictionary<string, string>
                {
                    ["jurisdiction_id"] = claim.JurisdictionId,
                    ["claim_id"] = claim.ClaimId,
                    ["claim_state"] = claim.ClaimState,
                });
        }

        return CreateResult(
            ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode,
            VerificationCheckStatus.Fail,
            VerificationResultCodes.RegulatoryClaimNotLegalApproval,
            "SP-11 regulatory claim shape, tracker version, state, or non-legal-advice boundary is invalid.",
            errors.ToDictionary(error => error, _ => "invalid", StringComparer.Ordinal));
    }

    private static VerifierCheckResultRecord BuildRegulatoryAllowedByRegisterResult(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim)
    {
        var allowed =
            string.Equals(claim.ClaimState, ElectionSp11ProfileIds.ClaimStateAllowedNow, StringComparison.Ordinal) ||
            string.Equals(claim.ClaimState, ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation, StringComparison.Ordinal);
        var authorityEvidenceMissing = claim.RequiresAuthorityEvidence &&
            string.IsNullOrWhiteSpace(claim.AuthorityEvidenceRef);

        if (allowed && !authorityEvidenceMissing)
        {
            return CreateResult(
                ElectionSp11ProfileIds.ClaimAllowedByRegisterCheckCode,
                VerificationCheckStatus.Pass,
                VerificationResultCodes.RegulatoryClaimAllowedByRegister,
                "SP-11 regulatory claim is allowed by the register for the declared organizational-election scope.",
                new Dictionary<string, string>
                {
                    ["claim_state"] = claim.ClaimState,
                    ["source_ref"] = claim.SourceRef,
                });
        }

        return CreateResult(
            ElectionSp11ProfileIds.ClaimAllowedByRegisterCheckCode,
            VerificationCheckStatus.Fail,
            authorityEvidenceMissing
                ? VerificationResultCodes.RegulatoryClaimBlockedCertification
                : VerificationResultCodes.RegulatoryClaimNotLegalApproval,
            authorityEvidenceMissing
                ? "SP-11 claim requires authority evidence before it can be allowed by the register."
                : "SP-11 regulatory claim is not currently allowed by the register.",
            new Dictionary<string, string>
            {
                ["claim_state"] = claim.ClaimState,
                ["requires_authority_evidence"] = claim.RequiresAuthorityEvidence.ToString(),
            });
    }

    private static VerifierCheckResultRecord BuildRegulatoryBlockedCertificationResult(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim)
    {
        var blocked =
            string.Equals(
                claim.ClaimState,
                ElectionSp11ProfileIds.ClaimStateBlockedUntilCertification,
                StringComparison.Ordinal) ||
            (claim.RequiresAuthorityEvidence && string.IsNullOrWhiteSpace(claim.AuthorityEvidenceRef)) ||
            ElectionSp11RegulatoryRules.ContainsForbiddenClaimPhrase(claim.AllowedWording);

        return CreateResult(
            ElectionSp11ProfileIds.BlockedCertificationClaimCheckCode,
            blocked ? VerificationCheckStatus.Fail : VerificationCheckStatus.Pass,
            blocked
                ? VerificationResultCodes.RegulatoryClaimBlockedCertification
                : VerificationResultCodes.RegulatoryClaimShapeValid,
            blocked
                ? "SP-11 certification, authority approval, or public-election parity claim is blocked."
                : "SP-11 claim does not assert certification, authority approval, or public-election parity.",
            new Dictionary<string, string>
            {
                ["claim_state"] = claim.ClaimState,
                ["requires_authority_evidence"] = claim.RequiresAuthorityEvidence.ToString(),
                ["authority_evidence_ref"] = claim.AuthorityEvidenceRef ?? string.Empty,
            });
    }

    private static VerifierCheckResultRecord BuildRegulatoryRestrictedWorkpaperBoundaryResult(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim,
        AuditPackageManifestRecord auditManifest)
    {
        var forbiddenBoundaryFields = VerificationPrivacyBoundary.FindForbiddenPublicFields(claim.PublicPrivacyBoundary);
        var publicRestrictedEntries = auditManifest.PackageView == VerificationPackageView.PublicAnonymous
            ? claim.PublicEvidenceFiles
                .Where(VerificationPrivacyBoundary.IsRestrictedArtifactPath)
                .Concat(string.IsNullOrWhiteSpace(claim.RestrictedWorkpaperRef)
                    ? []
                    : [claim.RestrictedWorkpaperRef])
                .Concat(claim.RestrictedEvidenceFiles)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        if (forbiddenBoundaryFields.Count == 0 && publicRestrictedEntries.Length == 0)
        {
            return CreateResult(
                ElectionSp11ProfileIds.RestrictedWorkpaperBoundaryCheckCode,
                VerificationCheckStatus.Pass,
                VerificationResultCodes.RegulatoryClaimShapeValid,
                "SP-11 public claim state does not expose restricted jurisdiction workpapers.");
        }

        var evidence = forbiddenBoundaryFields
            .ToDictionary(field => field, _ => "forbidden_boundary_field", StringComparer.OrdinalIgnoreCase);
        foreach (var entry in publicRestrictedEntries)
        {
            evidence[entry] = "restricted_workpaper_or_evidence";
        }

        return CreateResult(
            ElectionSp11ProfileIds.RestrictedWorkpaperBoundaryCheckCode,
            VerificationCheckStatus.Fail,
            VerificationResultCodes.RegulatoryRestrictedWorkpaperBoundary,
            "SP-11 public claim state exposes restricted jurisdiction workpaper material.",
            evidence);
    }
}

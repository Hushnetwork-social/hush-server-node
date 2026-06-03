param(
    [string]$WorkspaceRoot,
    [string]$Mode = "check-only",
    [string]$SourceInput,
    [string]$OutputRoot,
    [string]$GeneratedAt,
    [switch]$ValidateOnly,
    [switch]$PublicOnly
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot "RetentionLogPrivacyRecurringScanPromoter\RetentionLogPrivacyRecurringScanPromoter.csproj"
$argsList = @("--project", $project, "--", "--mode", $Mode)

if ($WorkspaceRoot) {
    $argsList += @("--workspace-root", $WorkspaceRoot)
}

if ($SourceInput) {
    $argsList += @("--source-input", $SourceInput)
}

if ($OutputRoot) {
    $argsList += @("--output-root", $OutputRoot)
}

if ($GeneratedAt) {
    $argsList += @("--generated-at", $GeneratedAt)
}

if ($ValidateOnly) {
    $argsList += "--validate-only"
}

if ($PublicOnly) {
    $argsList += "--public-only"
}

dotnet run @argsList
exit $LASTEXITCODE

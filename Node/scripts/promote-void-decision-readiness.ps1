param(
    [string]$WorkspaceRoot,
    [string]$Mode = "check-only",
    [string]$SourceInput,
    [string]$OutputRoot,
    [string]$GeneratedAt,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot "VoidDecisionReadinessPromoter\VoidDecisionReadinessPromoter.csproj"
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

dotnet run @argsList

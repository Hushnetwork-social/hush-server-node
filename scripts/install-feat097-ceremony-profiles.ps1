param(
    [Parameter(Mandatory = $true)]
    [string]$Provenance,

    [Parameter(Mandatory = $true)]
    [string]$GeneratedBy,

    [string]$WorkspaceRoot
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToUpperInvariant()
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) "..\\..")).Path
}

$workspaceRoot = (Resolve-Path $WorkspaceRoot).Path
$catalogPath = Join-Path $workspaceRoot "hush-server-node\Node\HushServerNode\ceremony-profiles\omega-v1.0.0\approved-ceremony-profiles.json"
$releaseDir = Join-Path $workspaceRoot "hush-memory-bank\Features\03_IN_PROGRESS\FEAT-097-election-key-ceremony-share-lifecycle"
$releasePath = Join-Path $releaseDir "approved-ceremony-profile-release.json"

if (-not (Test-Path $catalogPath)) {
    throw "Ceremony profile catalog not found: $catalogPath"
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$releaseManifest = [ordered]@{
    version = "omega-v1.0.0"
    provenance = $Provenance
    generatedBy = $GeneratedBy
    files = @(
        [ordered]@{
            relativePath = "hush-server-node/Node/HushServerNode/ceremony-profiles/omega-v1.0.0/approved-ceremony-profiles.json"
            sha256 = Get-Sha256Hex -Path $catalogPath
        }
    )
}

$releaseManifest | ConvertTo-Json -Depth 5 | Set-Content -Path $releasePath -Encoding UTF8

Write-Host "[OK] Wrote FEAT-097 ceremony profile release manifest." -ForegroundColor Green
Write-Host "Catalog: $catalogPath"
Write-Host "Release manifest: $releasePath"
Write-Host ""
Write-Host "Next step:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File hush-server-node/scripts/validate-feat097-ceremony-profiles.ps1"

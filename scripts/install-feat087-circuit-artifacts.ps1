param(
    [Parameter(Mandatory = $true)]
    [string]$ClientWasmSource,

    [Parameter(Mandatory = $true)]
    [string]$ClientZkeySource,

    [Parameter(Mandatory = $true)]
    [string]$ServerVerificationKeySource,

    [Parameter(Mandatory = $true)]
    [string]$Provenance,

    [Parameter(Mandatory = $true)]
    [string]$TrustedSetup,

    [Parameter(Mandatory = $true)]
    [string]$GeneratedBy,

    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [switch]$RebuildE2EImage
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToUpperInvariant()
}

function Ensure-FileExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Required source file not found: $Path"
    }
}

function Copy-Artifact {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -Force -Path $Source -Destination $Destination
}

$workspaceRoot = (Resolve-Path $WorkspaceRoot).Path
$clientWasmSource = (Resolve-Path $ClientWasmSource).Path
$clientZkeySource = (Resolve-Path $ClientZkeySource).Path
$serverVerificationKeySource = (Resolve-Path $ServerVerificationKeySource).Path

Ensure-FileExists -Path $clientWasmSource
Ensure-FileExists -Path $clientZkeySource
Ensure-FileExists -Path $serverVerificationKeySource

$clientCircuitDir = Join-Path $workspaceRoot "hush-web-client\public\circuits\omega-v1.0.0"
$serverCircuitDir = Join-Path $workspaceRoot "hush-server-node\Node\HushServerNode\circuits\omega-v1.0.0"
$manifestDir = Join-Path $workspaceRoot "hush-memory-bank\Features\03_IN_PROGRESS\FEAT-087-reactions-privacy-preserving-semantics"
$manifestPath = Join-Path $manifestDir "approved-circuit-artifact-release.json"

$clientWasmDestination = Join-Path $clientCircuitDir "reaction.wasm"
$clientZkeyDestination = Join-Path $clientCircuitDir "reaction.zkey"
$serverVerificationKeyDestination = Join-Path $serverCircuitDir "verification_key.json"

Copy-Artifact -Source $clientWasmSource -Destination $clientWasmDestination
Copy-Artifact -Source $clientZkeySource -Destination $clientZkeyDestination
Copy-Artifact -Source $serverVerificationKeySource -Destination $serverVerificationKeyDestination

New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$manifest = [ordered]@{
    version = "omega-v1.0.0"
    provenance = $Provenance
    trustedSetup = $TrustedSetup
    generatedBy = $GeneratedBy
    files = @(
        [ordered]@{
            relativePath = "hush-web-client/public/circuits/omega-v1.0.0/reaction.wasm"
            sha256 = Get-Sha256Hex -Path $clientWasmDestination
        },
        [ordered]@{
            relativePath = "hush-web-client/public/circuits/omega-v1.0.0/reaction.zkey"
            sha256 = Get-Sha256Hex -Path $clientZkeyDestination
        },
        [ordered]@{
            relativePath = "hush-server-node/Node/HushServerNode/circuits/omega-v1.0.0/verification_key.json"
            sha256 = Get-Sha256Hex -Path $serverVerificationKeyDestination
        }
    )
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "[OK] Installed FEAT-087 approved circuit artifacts." -ForegroundColor Green
Write-Host "Client wasm: $clientWasmDestination"
Write-Host "Client zkey: $clientZkeyDestination"
Write-Host "Server verification key: $serverVerificationKeyDestination"
Write-Host "Release manifest: $manifestPath"

if ($RebuildE2EImage) {
    $composeDirectory = Join-Path $workspaceRoot "hush-server-node\Node\HushNode.IntegrationTests"
    Push-Location $composeDirectory
    try {
        Write-Host ""
        Write-Host "Rebuilding HushWebClient E2E image..." -ForegroundColor Cyan
        docker compose -f docker-compose.e2e.yml build
    }
    finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "Next step:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File hush-server-node/scripts/validate-feat087-nondev-artifacts.ps1"

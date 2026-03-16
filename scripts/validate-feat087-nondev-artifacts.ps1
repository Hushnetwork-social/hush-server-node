param(
    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [switch]$WriteReleaseManifestDraft
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToUpperInvariant()
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Write-Status {
    param(
        [string]$Label,
        [bool]$Ok,
        [string]$Value
    )

    $prefix = if ($Ok) { "[OK]" } else { "[MISSING]" }
    $color = if ($Ok) { "Green" } else { "Red" }
    Write-Host "${prefix} ${Label}: $Value" -ForegroundColor $color
}

$workspaceRoot = (Resolve-Path $WorkspaceRoot).Path
$clientCircuitDir = Join-Path $workspaceRoot "hush-web-client\public\circuits\omega-v1.0.0"
$serverCircuitDir = Join-Path $workspaceRoot "hush-server-node\Node\HushServerNode\circuits\omega-v1.0.0"
$manifestDir = Join-Path $workspaceRoot "hush-memory-bank\Features\03_IN_PROGRESS\FEAT-087-reactions-privacy-preserving-semantics"
$templatePath = Join-Path $manifestDir "approved-circuit-artifact-release.template.json"
$manifestPath = Join-Path $manifestDir "approved-circuit-artifact-release.json"
$generatedManifestPath = Join-Path $manifestDir "approved-circuit-artifact-release.generated.json"

$clientWasmPath = Join-Path $clientCircuitDir "reaction.wasm"
$clientZkeyPath = Join-Path $clientCircuitDir "reaction.zkey"
$serverVerificationKeyPath = Join-Path $serverCircuitDir "verification_key.json"
$snarkJsPackagePath = Join-Path $workspaceRoot "hush-web-client\node_modules\snarkjs\package.json"

Write-Section "FEAT-087 Non-Dev Artifact Check"
Write-Host "WorkspaceRoot: $workspaceRoot"

$hasWasm = Test-Path $clientWasmPath
$hasZkey = Test-Path $clientZkeyPath
$hasVerificationKey = Test-Path $serverVerificationKeyPath
$hasSnarkJs = Test-Path $snarkJsPackagePath
$hasTemplate = Test-Path $templatePath
$hasManifest = Test-Path $manifestPath

Write-Status "Client wasm" $hasWasm $clientWasmPath
Write-Status "Client zkey" $hasZkey $clientZkeyPath
Write-Status "Server verification key" $hasVerificationKey $serverVerificationKeyPath
Write-Status "Installed snarkjs" $hasSnarkJs $snarkJsPackagePath
Write-Status "Release manifest template" $hasTemplate $templatePath
Write-Status "Approved release manifest" $hasManifest $manifestPath

if ($WriteReleaseManifestDraft) {
    Write-Section "Release Manifest Draft"

    if (-not ($hasWasm -and $hasZkey -and $hasVerificationKey)) {
        throw "Cannot write manifest draft until reaction.wasm, reaction.zkey, and verification_key.json all exist."
    }

    $draft = [ordered]@{
        version = "omega-v1.0.0"
        provenance = "REPLACE_WITH_APPROVED_RELEASE_PROVENANCE"
        trustedSetup = "REPLACE_WITH_PTAU_OR_CEREMONY_REFERENCE"
        generatedBy = "REPLACE_WITH_TOOLCHAIN_AND_COMMANDS"
        files = @(
            [ordered]@{
                relativePath = "hush-web-client/public/circuits/omega-v1.0.0/reaction.wasm"
                sha256 = Get-Sha256Hex -Path $clientWasmPath
            },
            [ordered]@{
                relativePath = "hush-web-client/public/circuits/omega-v1.0.0/reaction.zkey"
                sha256 = Get-Sha256Hex -Path $clientZkeyPath
            },
            [ordered]@{
                relativePath = "hush-server-node/Node/HushServerNode/circuits/omega-v1.0.0/verification_key.json"
                sha256 = Get-Sha256Hex -Path $serverVerificationKeyPath
            }
        )
    }

    $draft | ConvertTo-Json -Depth 5 | Set-Content -Path $generatedManifestPath -Encoding UTF8
    Write-Host "[OK] Wrote manifest draft: $generatedManifestPath" -ForegroundColor Green
    Write-Host "Fill provenance/trustedSetup/generatedBy, then save as approved-circuit-artifact-release.json"
}

Write-Section "Exact Validation Commands"

Write-Host "1. Server proof-path tests"
Write-Host 'dotnet test hush-server-node/Node/HushServerNode.Tests/HushServerNode.Tests.csproj --filter "FullyQualifiedName~ReactionProofPathInspectorTests|FullyQualifiedName~ReactionArtifactReleaseValidatorTests" --no-restore --verbosity minimal'
Write-Host ""
Write-Host "2. Server verifier tests"
Write-Host 'dotnet test hush-server-node/Node/Core/Reactions/HushNode.Reactions.Tests/HushNode.Reactions.Tests.csproj --filter "FullyQualifiedName~Groth16VerifierTests|FullyQualifiedName~SnarkJsVerificationKeyParserTests|FullyQualifiedName~PackedGroth16ProofAdapterTests|FullyQualifiedName~SnarkJsPublicSignalsAdapterTests" --no-restore --verbosity minimal'
Write-Host ""
Write-Host "3. Web proof-path tests"
Write-Host 'cd hush-web-client'
Write-Host 'npm test -- src/lib/zk/artifactManifest.test.ts src/app/api/reactions/circuit-status/route.test.ts src/lib/zk/proofPacking.test.ts src/lib/zk/headlessProver.test.ts src/lib/zk/prover.test.ts'
Write-Host ""
Write-Host "4. Web build"
Write-Host 'cd hush-web-client'
Write-Host 'npm run build'
Write-Host ""
Write-Host "5. FEAT-087 browser smoke"
Write-Host 'dotnet test hush-server-node/Node/HushNode.IntegrationTests/HushNode.IntegrationTests.csproj --filter "Category=HS-E2E-087-REACTION-FLOW" --no-restore --verbosity minimal'
Write-Host ""
Write-Host "6. First non-dev benchmark attempt"
Write-Host 'dotnet test hush-server-node/Node/HushNode.IntegrationTests/HushNode.IntegrationTests.csproj --filter "FullyQualifiedName~Feat087ReactionBenchmarkDryRunTests.Sequential_PublicPost_ReactionVisibility_1Voter_OneVotePerBlock_NonDevMode_FailsFastWithReadinessDetails" --no-build --verbosity minimal'
Write-Host ""
Write-Host "7. Then staged non-dev benchmark tiers"
Write-Host 'dotnet test hush-server-node/Node/HushNode.IntegrationTests/HushNode.IntegrationTests.csproj --filter "FullyQualifiedName~Feat087ReactionBenchmarkDryRunTests.Sequential_PublicPost_ReactionVisibility_10Voters_OneVotePerBlock_NonDevMode" --no-build --verbosity minimal'
Write-Host 'dotnet test hush-server-node/Node/HushNode.IntegrationTests/HushNode.IntegrationTests.csproj --filter "FullyQualifiedName~Feat087ReactionBenchmarkDryRunTests.Sequential_PublicPost_ReactionVisibility_100Voters_OneVotePerBlock_NonDevMode" --no-build --verbosity minimal'

Write-Section "Summary"

if ($hasWasm -and $hasZkey -and $hasVerificationKey -and $hasSnarkJs -and $hasManifest) {
    Write-Host "[READY] Runtime prerequisites appear installed. Run the validation commands above." -ForegroundColor Green
} else {
    Write-Host "[BLOCKED] Runtime prerequisites are still incomplete. Install the approved artifact drop first." -ForegroundColor Yellow
}

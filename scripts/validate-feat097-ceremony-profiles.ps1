param(
    [string]$WorkspaceRoot
)

$ErrorActionPreference = "Stop"

function Write-Status {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][bool]$Success,
        [string]$Detail = ""
    )

    $color = if ($Success) { "Green" } else { "Red" }
    $prefix = if ($Success) { "[OK]" } else { "[FAIL]" }
    if ([string]::IsNullOrWhiteSpace($Detail)) {
        Write-Host "$prefix $Label" -ForegroundColor $color
    }
    else {
        Write-Host "$prefix $Label - $Detail" -ForegroundColor $color
    }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToUpperInvariant()
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) "..\\..")).Path
}

$workspaceRoot = (Resolve-Path $WorkspaceRoot).Path
$releasePath = Join-Path $workspaceRoot "hush-memory-bank\Features\03_IN_PROGRESS\FEAT-097-election-key-ceremony-share-lifecycle\approved-ceremony-profile-release.json"
$catalogPath = Join-Path $workspaceRoot "hush-server-node\Node\HushServerNode\ceremony-profiles\omega-v1.0.0\approved-ceremony-profiles.json"

$hasRelease = Test-Path $releasePath
$hasCatalog = Test-Path $catalogPath

Write-Status "Release manifest present" $hasRelease $releasePath
Write-Status "Runtime catalog present" $hasCatalog $catalogPath

if (-not $hasRelease -or -not $hasCatalog) {
    throw "Required FEAT-097 ceremony profile files are missing."
}

$release = Get-Content -Path $releasePath -Raw | ConvertFrom-Json
$catalog = Get-Content -Path $catalogPath -Raw | ConvertFrom-Json

$expectedVersion = $release.version -eq "omega-v1.0.0" -and $catalog.version -eq "omega-v1.0.0"
Write-Status "Catalog and release versions" $expectedVersion "release=$($release.version); catalog=$($catalog.version)"

$expectedHash = $release.files[0].sha256
$actualHash = Get-Sha256Hex -Path $catalogPath
$hashMatches = $expectedHash -eq $actualHash
Write-Status "Catalog SHA-256 matches release manifest" $hashMatches $actualHash

$devProfile = $catalog.profiles | Where-Object { $_.profileId -eq "dkg-dev-3of5" }
$prodProfile = $catalog.profiles | Where-Object { $_.profileId -eq "dkg-prod-3of5" }

$hasPair = $null -ne $devProfile -and $null -ne $prodProfile
Write-Status "Shipped dev/prod profile pair present" $hasPair

$rolloutShapeMatches = $hasPair -and
    $devProfile.trusteeCount -eq 5 -and
    $devProfile.requiredApprovalCount -eq 3 -and
    $devProfile.devOnly -eq $true -and
    $prodProfile.trusteeCount -eq 5 -and
    $prodProfile.requiredApprovalCount -eq 3 -and
    $prodProfile.devOnly -eq $false
Write-Status "Initial 3-of-5 rollout shape" $rolloutShapeMatches

if (-not ($expectedVersion -and $hashMatches -and $hasPair -and $rolloutShapeMatches)) {
    throw "FEAT-097 ceremony profile validation failed."
}

Write-Host ""
Write-Host "Suggested focused test commands:" -ForegroundColor Cyan
Write-Host 'cd hush-server-node/Node && dotnet test HushServerNode.Tests/HushServerNode.Tests.csproj --filter "FullyQualifiedName~ElectionCeremonyProfileReleaseValidatorTests|FullyQualifiedName~ElectionQueryApplicationServiceTests|FullyQualifiedName~ElectionLifecycleServiceTests" --no-restore --verbosity normal'
Write-Host 'cd hush-server-node/Node && dotnet test HushNode.IntegrationTests/HushNode.IntegrationTests.csproj --filter "Category=FEAT-097&Category!=E2E" --no-restore --verbosity normal'

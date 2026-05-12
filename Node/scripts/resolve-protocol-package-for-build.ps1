param(
    [string]$Configuration = "Debug",
    [string]$ServerRepositoryRoot,
    [string]$WorkspaceRoot,
    [string]$OutputRoot,
    [string]$PackageId = "omega-hushvoting-v1",
    [string]$ArtifactsRelativePath = "PrivateServer_ElectronicVoting/Protocol-Omega-HushVoting-v1-Artifacts",
    [string]$GitHubRepository = "Hushnetwork-social/hush-documents",
    [string]$GitHubRef = "master",
    [string]$ReleaseTagPrefix = "ProtocolOmega-HushVoting-v1-",
    [string]$ReleaseAssetPrefix = "Protocol-Omega-HushVoting-v1-Artifacts",
    [string]$GitHubToken
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-DefaultToken {
    if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        return $GitHubToken
    }

    foreach ($name in @("HUSH_PROTOCOL_PACKAGE_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN")) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $null
}

function Get-GitHubHeaders {
    $headers = @{
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "HushServerNode-ProtocolPackageResolver"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    $token = Get-DefaultToken
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        $headers["Authorization"] = "Bearer $token"
    }

    return $headers
}

function ConvertTo-GitHubPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (($Path -replace "\\", "/").Trim("/") -split "/" |
        ForEach-Object { [Uri]::EscapeDataString($_) }) -join "/"
}

function Invoke-GitHubContents {
    param([Parameter(Mandatory = $true)][string]$Path)

    $encodedPath = ConvertTo-GitHubPath $Path
    $url = "https://api.github.com/repos/$GitHubRepository/contents/$encodedPath`?ref=$([Uri]::EscapeDataString($GitHubRef))"
    try {
        return Invoke-RestMethod -Uri $url -Headers (Get-GitHubHeaders)
    }
    catch {
        throw "Unable to read GitHub package path '$Path' from '$GitHubRepository' ref '$GitHubRef'. Check that the package has been published to GitHub and set HUSH_PROTOCOL_PACKAGE_GITHUB_TOKEN if the repository is private. $($_.Exception.Message)"
    }
}

function Invoke-GitHubReleases {
    $url = "https://api.github.com/repos/$GitHubRepository/releases?per_page=100"
    try {
        return Invoke-RestMethod -Uri $url -Headers (Get-GitHubHeaders)
    }
    catch {
        throw "Unable to read GitHub releases from '$GitHubRepository'. Check that Protocol Omega packages have been released and set HUSH_PROTOCOL_PACKAGE_GITHUB_TOKEN if the repository is private. $($_.Exception.Message)"
    }
}

function Save-GitHubReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]$Asset,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $headers = Get-GitHubHeaders
    $headers["Accept"] = "application/octet-stream"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TargetPath) | Out-Null

    try {
        Invoke-WebRequest -Uri $Asset.url -Headers $headers -OutFile $TargetPath -UseBasicParsing | Out-Null
    }
    catch {
        throw "Unable to download GitHub release asset '$($Asset.name)' from '$GitHubRepository'. $($_.Exception.Message)"
    }
}

function Save-GitHubFile {
    param(
        [Parameter(Mandatory = $true)]$FileItem,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TargetPath) | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($FileItem.download_url)) {
        Invoke-WebRequest `
            -Uri $FileItem.download_url `
            -Headers (Get-GitHubHeaders) `
            -OutFile $TargetPath `
            -UseBasicParsing | Out-Null
        return
    }

    $file = Invoke-GitHubContents $FileItem.path
    if ([string]::IsNullOrWhiteSpace($file.content)) {
        throw "GitHub file '$($FileItem.path)' did not provide a download_url or inline content."
    }

    $bytes = [Convert]::FromBase64String(($file.content -replace "\s", ""))
    [System.IO.File]::WriteAllBytes($TargetPath, $bytes)
}

function Save-GitHubDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RemotePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $items = @(Invoke-GitHubContents $RemotePath)
    foreach ($item in $items) {
        $itemTarget = Join-Path $TargetPath $item.name
        if ($item.type -eq "dir") {
            Save-GitHubDirectory -RemotePath $item.path -TargetPath $itemTarget
            continue
        }

        if ($item.type -eq "file") {
            Save-GitHubFile -FileItem $item -TargetPath $itemTarget
        }
    }
}

function ConvertTo-ProtocolPackageVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    if ($Version -notmatch "^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$") {
        return $null
    }

    return [pscustomobject]@{
        Name = $Version
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
    }
}

function Get-ApprovalStatusName {
    param($Manifest)

    $status = $Manifest.approvalStatus
    if ($null -eq $status) {
        return "Unknown"
    }

    if ($status -is [int] -or $status -is [long]) {
        switch ([int]$status) {
            0 { return "DraftPrivate" }
            1 { return "ApprovedInternal" }
            2 { return "Retired" }
            default { return "Unknown" }
        }
    }

    return [string]$status
}

function Test-IsApprovedPackage {
    param($Candidate)

    return $Candidate.ApprovalStatus -eq "ApprovedInternal" -and ($Candidate.Version.Minor % 2 -eq 0)
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    return Get-Content -Raw -Path $Path | ConvertFrom-Json
}

function Find-ServerRepositoryRoot {
    if (-not [string]::IsNullOrWhiteSpace($ServerRepositoryRoot)) {
        return Resolve-FullPath $ServerRepositoryRoot
    }

    $current = Get-Item -LiteralPath $PSScriptRoot
    while ($null -ne $current) {
        if (Test-Path (Join-Path $current.FullName "Node/HushServerNode/HushServerNode.csproj")) {
            return $current.FullName
        }

        $current = $current.Parent
    }

    throw "Unable to resolve hush-server-node repository root. Pass -ServerRepositoryRoot."
}

function Find-WorkspaceRoot {
    param([Parameter(Mandatory = $true)][string]$ResolvedServerRoot)

    if (-not [string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
        return Resolve-FullPath $WorkspaceRoot
    }

    $parent = Split-Path -Parent $ResolvedServerRoot
    if (Test-Path (Join-Path $parent "hush-documents")) {
        return $parent
    }

    return $null
}

function Get-LocalCandidates {
    param([Parameter(Mandatory = $true)][string]$ArtifactsRoot)

    if (-not (Test-Path -LiteralPath $ArtifactsRoot)) {
        return @()
    }

    $candidates = @()
    foreach ($directory in Get-ChildItem -LiteralPath $ArtifactsRoot -Directory) {
        $version = ConvertTo-ProtocolPackageVersion $directory.Name
        if ($null -eq $version) {
            continue
        }

        $manifestPath = Join-Path $directory.FullName "ProtocolOmegaPackageManifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            continue
        }

        $manifest = Read-JsonFile $manifestPath
        if ($manifest.packageId -ne $PackageId) {
            continue
        }

        $candidates += [pscustomobject]@{
            Source = "local"
            Version = $version
            VersionRoot = $directory.FullName
            Manifest = $manifest
            ApprovalStatus = Get-ApprovalStatusName $manifest
        }
    }

    return $candidates
}

function Get-GitHubCandidates {
    $items = @(Invoke-GitHubContents $ArtifactsRelativePath)
    $candidates = @()

    foreach ($item in $items | Where-Object { $_.type -eq "dir" }) {
        $version = ConvertTo-ProtocolPackageVersion $item.name
        if ($null -eq $version) {
            continue
        }

        $manifestPath = "$ArtifactsRelativePath/$($item.name)/ProtocolOmegaPackageManifest.json"
        $manifest = Invoke-GitHubContents $manifestPath
        if ($manifest.content) {
            $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($manifest.content -replace "\s", "")))
            $manifest = $json | ConvertFrom-Json
        }

        if ($manifest.packageId -ne $PackageId) {
            continue
        }

        $candidates += [pscustomobject]@{
            Source = "github"
            Version = $version
            RemotePath = "$ArtifactsRelativePath/$($item.name)"
            Manifest = $manifest
            ApprovalStatus = Get-ApprovalStatusName $manifest
        }
    }

    return $candidates
}

function Get-GitHubReleaseCandidate {
    $releaseCandidates = @()
    foreach ($release in @(Invoke-GitHubReleases)) {
        if ($release.draft -or $release.prerelease) {
            continue
        }

        $tagName = [string]$release.tag_name
        if (-not $tagName.StartsWith($ReleaseTagPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $versionName = $tagName.Substring($ReleaseTagPrefix.Length)
        $version = ConvertTo-ProtocolPackageVersion $versionName
        if ($null -eq $version) {
            continue
        }

        if (($version.Minor % 2) -ne 0) {
            continue
        }

        $assetName = "$ReleaseAssetPrefix-$versionName.zip"
        $asset = @($release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1)
        if ($asset.Count -eq 0) {
            throw "GitHub release '$tagName' does not contain required asset '$assetName'."
        }

        $releaseCandidates += [pscustomobject]@{
            Release = $release
            Version = $version
            Asset = $asset[0]
            AssetName = $assetName
            TagName = $tagName
        }
    }

    $sorted = @($releaseCandidates |
        Sort-Object `
            @{ Expression = { $_.Version.Major }; Descending = $true },
            @{ Expression = { $_.Version.Minor }; Descending = $true },
            @{ Expression = { $_.Version.Patch }; Descending = $true })

    if ($sorted.Count -eq 0) {
        throw "No Protocol Omega GitHub release with an approved even-minor version was found."
    }

    foreach ($releaseCandidate in $sorted) {
        $downloadRoot = Join-Path ([IO.Path]::GetTempPath()) "hush-protocol-package-release-$([Guid]::NewGuid().ToString('N'))"
        $zipPath = Join-Path $downloadRoot $releaseCandidate.AssetName
        $extractRoot = Join-Path $downloadRoot "extracted"
        New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
        Save-GitHubReleaseAsset -Asset $releaseCandidate.Asset -TargetPath $zipPath
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

        $versionRoot = Join-Path $extractRoot $releaseCandidate.Version.Name
        if (-not (Test-Path -LiteralPath $versionRoot)) {
            $versionRoot = $extractRoot
        }

        $manifestPath = Join-Path $versionRoot "ProtocolOmegaPackageManifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            throw "GitHub release '$($releaseCandidate.TagName)' asset '$($releaseCandidate.AssetName)' does not contain ProtocolOmegaPackageManifest.json."
        }

        $manifest = Read-JsonFile $manifestPath
        $approvalStatus = Get-ApprovalStatusName $manifest
        if ($manifest.packageId -ne $PackageId) {
            throw "GitHub release '$($releaseCandidate.TagName)' package id '$($manifest.packageId)' does not match expected '$PackageId'."
        }

        if ($manifest.packageVersion -ne $releaseCandidate.Version.Name) {
            throw "GitHub release '$($releaseCandidate.TagName)' manifest version '$($manifest.packageVersion)' does not match release version '$($releaseCandidate.Version.Name)'."
        }

        if ($approvalStatus -ne "ApprovedInternal") {
            throw "GitHub release '$($releaseCandidate.TagName)' is not approved. Manifest status is '$approvalStatus'."
        }

        return [pscustomobject]@{
            Source = "github-release"
            Version = $releaseCandidate.Version
            VersionRoot = $versionRoot
            Manifest = $manifest
            ApprovalStatus = $approvalStatus
            ReleaseTag = $releaseCandidate.TagName
            AssetName = $releaseCandidate.AssetName
        }
    }

    throw "No usable Protocol Omega GitHub release asset was found."
}

function Select-Candidate {
    param(
        [Parameter(Mandatory = $true)][array]$Candidates,
        [Parameter(Mandatory = $true)][bool]$ReleaseMode
    )

    $sorted = @($Candidates |
        Sort-Object `
            @{ Expression = { $_.Version.Major }; Descending = $true },
            @{ Expression = { $_.Version.Minor }; Descending = $true },
            @{ Expression = { $_.Version.Patch }; Descending = $true })

    if ($ReleaseMode) {
        $approved = @($sorted | Where-Object { Test-IsApprovedPackage $_ })
        if ($approved.Count -eq 0) {
            throw "No approved even-minor Protocol Omega package was found for Release builds."
        }

        return $approved[0]
    }

    if ($sorted.Count -eq 0) {
        throw "No Protocol Omega package candidates were found."
    }

    return $sorted[0]
}

function Write-BuildCatalog {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $approvalStatus = $Manifest.approvalStatus
    $isApproved = (Get-ApprovalStatusName $Manifest) -eq "ApprovedInternal"

    $entry = [ordered]@{
        approvalStatus = $approvalStatus
        approvedAt = $Manifest.releasedAt
        isLatestForCompatibleProfiles = $true
        externalReviewStatus = $Manifest.externalReviewStatus
        packageId = $Manifest.packageId
        packageVersion = $Manifest.packageVersion
        specPackageHash = $Manifest.specPackageHash
        proofPackageHash = $Manifest.proofPackageHash
        releaseManifestHash = $Manifest.releaseManifestHash
        compatibleProfileIds = $Manifest.compatibleProfileIds
        specAccessLocations = $Manifest.specAccessLocations
        proofAccessLocations = $Manifest.proofAccessLocations
        isApprovedForElectionOpen = $isApproved
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TargetPath) | Out-Null
    ConvertTo-Json -InputObject @($entry) -Depth 20 | Set-Content -Path $TargetPath -Encoding UTF8
}

function Write-SelectionMetadata {
    param(
        [Parameter(Mandatory = $true)]$Candidate,
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $metadata = [ordered]@{
        resolvedAt = (Get-Date).ToUniversalTime().ToString("O")
        buildConfiguration = $Configuration
        mode = $Mode
        source = $Candidate.Source
        packageId = $Candidate.Manifest.packageId
        packageVersion = $Candidate.Manifest.packageVersion
        approvalStatus = $Candidate.ApprovalStatus
        externalReviewStatus = $Candidate.Manifest.externalReviewStatus
        specPackageHash = $Candidate.Manifest.specPackageHash
        proofPackageHash = $Candidate.Manifest.proofPackageHash
        releaseManifestHash = $Candidate.Manifest.releaseManifestHash
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TargetPath) | Out-Null
    $metadata | ConvertTo-Json -Depth 10 | Set-Content -Path $TargetPath -Encoding UTF8
}

function Test-ResolvedOutputMatchesCandidate {
    param(
        [Parameter(Mandatory = $true)]$Candidate,
        [Parameter(Mandatory = $true)][string]$ResolvedOutputRoot
    )

    $metadataPath = Join-Path $ResolvedOutputRoot "SelectedProtocolPackage.json"
    $catalogPath = Join-Path $ResolvedOutputRoot "ApprovedProtocolPackageCatalog.json"
    $versionRoot = Join-Path $ResolvedOutputRoot $Candidate.Version.Name
    $manifestPath = Join-Path $versionRoot "ProtocolOmegaPackageManifest.json"

    if (-not (Test-Path -LiteralPath $metadataPath) -or
        -not (Test-Path -LiteralPath $catalogPath) -or
        -not (Test-Path -LiteralPath $versionRoot) -or
        -not (Test-Path -LiteralPath $manifestPath)) {
        return $false
    }

    try {
        $metadata = Read-JsonFile $metadataPath
        $catalog = @(Read-JsonFile $catalogPath)
    }
    catch {
        return $false
    }

    $catalogEntry = @($catalog | Where-Object {
        $_.packageId -eq $Candidate.Manifest.packageId -and
        $_.packageVersion -eq $Candidate.Manifest.packageVersion
    } | Select-Object -First 1)

    if ($catalogEntry.Count -eq 0 -or
        $catalogEntry[0].isLatestForCompatibleProfiles -ne $true -or
        $catalogEntry[0].specPackageHash -ne $Candidate.Manifest.specPackageHash -or
        $catalogEntry[0].proofPackageHash -ne $Candidate.Manifest.proofPackageHash -or
        $catalogEntry[0].releaseManifestHash -ne $Candidate.Manifest.releaseManifestHash) {
        return $false
    }

    return $metadata.packageVersion -eq $Candidate.Manifest.packageVersion -and
        $metadata.specPackageHash -eq $Candidate.Manifest.specPackageHash -and
        $metadata.proofPackageHash -eq $Candidate.Manifest.proofPackageHash -and
        $metadata.releaseManifestHash -eq $Candidate.Manifest.releaseManifestHash
}

$serverRoot = Find-ServerRepositoryRoot
$serverRootFull = Resolve-FullPath $serverRoot
$isRelease = $Configuration -eq "Release"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $serverRootFull "Node/Release/ProtocolPackages"
}

$outputRootFull = Resolve-FullPath $OutputRoot
$serverRootWithSeparator = $serverRootFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRootFull.StartsWith($serverRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Protocol package build output must stay inside the hush-server-node repository. OutputRoot='$outputRootFull'."
}

if ($isRelease) {
    Write-Host "Resolving Protocol Omega package for Release from GitHub releases in '$GitHubRepository'."
    $candidate = Get-GitHubReleaseCandidate

    if (Test-ResolvedOutputMatchesCandidate -Candidate $candidate -ResolvedOutputRoot $outputRootFull) {
        Write-Host "Protocol Omega package $($candidate.Version.Name) is already resolved in '$outputRootFull'. No copy needed."
        exit 0
    }

    if (Test-Path -LiteralPath $outputRootFull) {
        Remove-Item -LiteralPath $outputRootFull -Recurse -Force
    }

    $versionTarget = Join-Path $outputRootFull $candidate.Version.Name
    New-Item -ItemType Directory -Force -Path $versionTarget | Out-Null
    Copy-Item -Path (Join-Path $candidate.VersionRoot "*") -Destination $versionTarget -Recurse -Force
    Write-BuildCatalog -Manifest $candidate.Manifest -TargetPath (Join-Path $outputRootFull "ApprovedProtocolPackageCatalog.json")
    Write-SelectionMetadata -Candidate $candidate -Mode "release-github-release-approved" -TargetPath (Join-Path $outputRootFull "SelectedProtocolPackage.json")
    Write-Host "Resolved Release Protocol Omega package $($candidate.Version.Name) ($($candidate.ApprovalStatus)) into '$outputRootFull'."
    exit 0
}

$workspaceRootFull = Find-WorkspaceRoot -ResolvedServerRoot $serverRootFull
if ([string]::IsNullOrWhiteSpace($workspaceRootFull)) {
    Write-Warning "Skipping Debug Protocol Omega package resolution because a sibling hush-documents repository was not found."
    exit 0
}

$localArtifactsRoot = Join-Path (Join-Path $workspaceRootFull "hush-documents") $ArtifactsRelativePath.Replace("/", [IO.Path]::DirectorySeparatorChar)
Write-Host "Resolving Protocol Omega package for Debug from local artifacts '$localArtifactsRoot'."
$localCandidates = @(Get-LocalCandidates -ArtifactsRoot $localArtifactsRoot)
if ($localCandidates.Count -eq 0) {
    Write-Warning "Skipping Debug Protocol Omega package resolution because no local package artifacts were found."
    exit 0
}

$candidate = Select-Candidate -Candidates $localCandidates -ReleaseMode $false
if (Test-ResolvedOutputMatchesCandidate -Candidate $candidate -ResolvedOutputRoot $outputRootFull) {
    Write-Host "Protocol Omega package $($candidate.Version.Name) is already resolved in '$outputRootFull'. No copy needed."
    exit 0
}

if (Test-Path -LiteralPath $outputRootFull) {
    Remove-Item -LiteralPath $outputRootFull -Recurse -Force
}

$versionTarget = Join-Path $outputRootFull $candidate.Version.Name
New-Item -ItemType Directory -Force -Path $versionTarget | Out-Null
Copy-Item -Path (Join-Path $candidate.VersionRoot "*") -Destination $versionTarget -Recurse -Force
Write-BuildCatalog -Manifest $candidate.Manifest -TargetPath (Join-Path $outputRootFull "ApprovedProtocolPackageCatalog.json")
Write-SelectionMetadata -Candidate $candidate -Mode "debug-local-development" -TargetPath (Join-Path $outputRootFull "SelectedProtocolPackage.json")
Write-Host "Resolved Debug Protocol Omega package $($candidate.Version.Name) ($($candidate.ApprovalStatus)) into '$outputRootFull'."

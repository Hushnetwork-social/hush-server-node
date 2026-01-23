# run-tests.ps1
# Runs all Node tests with unified output to TestResults/TestRun_{timestamp}/
#
# Usage:
#   .\scripts\run-tests.ps1                    # Run all tests
#   .\scripts\run-tests.ps1 -Filter "E2E"      # Run only E2E tests
#   .\scripts\run-tests.ps1 -Project "HushNode.Caching.Tests"  # Run specific project

param(
    [string]$Filter = "",
    [string]$Project = "",
    [switch]$NoBuild = $false
)

$ErrorActionPreference = "Stop"

# Generate timestamp and create output folder
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$nodeDir = Join-Path $PSScriptRoot ".."
$testResultsBase = Join-Path $nodeDir "TestResults"
$testResultsDir = Join-Path $testResultsBase "TestRun_$timestamp"
New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null

Write-Host "[Tests] Output folder: $testResultsDir" -ForegroundColor Cyan

# Set environment variable for E2E tests to use the same folder
$env:HUSH_TEST_OUTPUT_DIR = $testResultsDir

# Build command
$nodeDir = Join-Path $PSScriptRoot ".."
$cmd = "dotnet test"

if ($Project) {
    $projectPath = Get-ChildItem -Path $nodeDir -Recurse -Filter "$Project.csproj" | Select-Object -First 1
    if ($projectPath) {
        $cmd += " `"$($projectPath.FullName)`""
    } else {
        Write-Host "[Tests] Project not found: $Project" -ForegroundColor Red
        exit 1
    }
}

if ($NoBuild) {
    $cmd += " --no-build"
}

if ($Filter) {
    $cmd += " --filter `"$Filter`""
}

$cmd += " --logger `"trx;LogFileName=test-results.trx`""
$cmd += " --results-directory `"$testResultsDir`""

Write-Host "[Tests] Running: $cmd" -ForegroundColor Yellow
Write-Host ""

# Run tests and capture output
Push-Location $nodeDir
try {
    $consoleLogPath = Join-Path $testResultsDir "console-output.log"

    # Use Invoke-Expression with Tee-Object to capture and display output
    Invoke-Expression $cmd 2>&1 | Tee-Object -FilePath $consoleLogPath

    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
    # Clear environment variable
    Remove-Item Env:HUSH_TEST_OUTPUT_DIR -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "[Tests] Results saved to: $testResultsDir" -ForegroundColor Cyan
Write-Host "[Tests]   - test-results.trx" -ForegroundColor Gray
Write-Host "[Tests]   - console-output.log" -ForegroundColor Gray

# List E2E scenario folders if any
$scenarioFolders = Get-ChildItem -Path $testResultsDir -Directory -ErrorAction SilentlyContinue
if ($scenarioFolders) {
    Write-Host "[Tests]   - E2E Scenarios:" -ForegroundColor Gray
    foreach ($folder in $scenarioFolders) {
        Write-Host "[Tests]     - $($folder.Name)/" -ForegroundColor Gray
    }
}

exit $exitCode

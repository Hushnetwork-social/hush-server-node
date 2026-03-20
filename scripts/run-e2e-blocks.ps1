param(
    [string]$BlockList,
    [int]$PollSeconds = 15,
    [int]$StaleMinutes = 5,
    [int]$MaxMinutes = 5,
    [bool]$NoBuild = $true,
    [int]$RetryOnEarlyStallCount = 1,
    [switch]$StopOnFailure = $true
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspaceRoot "Node\HushNode.IntegrationTests\HushNode.IntegrationTests.csproj"
$testResultsRoot = Join-Path $workspaceRoot "TestResults"

$allBlocks = @(
    @{ Name = "Infrastructure"; Filter = 'Category=Infrastructure&Category!=LONG_RUNNING' },
    @{ Name = "FoundationWalkthrough"; Filter = 'Category=FoundationWalkthrough&Category!=LONG_RUNNING' },
    @{ Name = "GroupJoin"; Filter = 'Category=GroupJoin&Category!=LONG_RUNNING' },
    @{ Name = "GroupTitleChange"; Filter = 'Category=GroupTitleChange&Category!=LONG_RUNNING' },
    @{ Name = "OwnMessageUnread"; Filter = 'Category=OwnMessageUnread&Category!=LONG_RUNNING' },
    @{ Name = "MessageRetry"; Filter = 'Category=MessageRetry&Category!=LONG_RUNNING' },
    @{ Name = "FEAT-059"; Filter = 'Category=E2E&Category=FEAT-059&Category!=LONG_RUNNING' },
    @{ Name = "FEAT-062"; Filter = 'Category=E2E&Category=FEAT-062&Category!=LONG_RUNNING' },
    @{ Name = "CDRS-E2E-READ-CLEARS"; Filter = 'Category=CDRS-E2E-READ-CLEARS' },
    @{ Name = "CDRS-E2E-POST-READ-UNREAD"; Filter = 'Category=CDRS-E2E-POST-READ-UNREAD' },
    @{ Name = "CDRS-E2E-SORT-STABLE"; Filter = 'Category=CDRS-E2E-SORT-STABLE' },
    @{ Name = "CDRS-E2E-BETWEEN-READ-AND-SYNC"; Filter = 'Category=CDRS-E2E-BETWEEN-READ-AND-SYNC' },
    @{ Name = "CDRS-E2E-CONVERGE-HIGHEST"; Filter = 'Category=CDRS-E2E-CONVERGE-HIGHEST' },
    @{ Name = "IdentityNameChange"; Filter = 'Category=IdentityNameChange&Category!=LONG_RUNNING' },
    @{ Name = "HS-E2E-084-NAV"; Filter = 'Category=HS-E2E-084-NAV' },
    @{ Name = "HS-E2E-085-CIRCLE-REMOVAL"; Filter = 'Category=HS-E2E-085-CIRCLE-REMOVAL' },
    @{ Name = "HS-E2E-085-BOOTSTRAP-STABLE"; Filter = 'Category=HS-E2E-085-BOOTSTRAP-STABLE' },
    @{ Name = "HS-E2E-086-AUTHOR"; Filter = 'Category=HS-E2E-086-AUTHOR' },
    @{ Name = "HS-E2E-087-ROLLBACK"; Filter = 'Category=HS-E2E-087-ROLLBACK' },
    @{ Name = "HS-E2E-087-DENIED-PERMALINK"; Filter = 'Category=HS-E2E-087-DENIED-PERMALINK' },
    @{ Name = "HS-E2E-087-PUBLIC-PERMALINK"; Filter = 'Category=HS-E2E-087-PUBLIC-PERMALINK' },
    @{ Name = "HS-E2E-087-REACTION-FLOW"; Filter = 'Category=HS-E2E-087-REACTION-FLOW' },
    @{ Name = "HS-E2E-089-GUEST-CTA"; Filter = 'Category=HS-E2E-089-GUEST-CTA' },
    @{ Name = "HS-E2E-090-FOLLOW"; Filter = 'Category=HS-E2E-090-FOLLOW&Category!=LONG_RUNNING' }
)

if ($BlockList) {
    $normalizedBlockNames = @(
        ($BlockList -split '[,\s]+') |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ }
    )

    $selectedBlocks = @(
        foreach ($name in $normalizedBlockNames) {
            $block = $allBlocks | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if (-not $block) {
                throw "Unknown block '$name'."
            }
            $block
        }
    )
}
else {
    $selectedBlocks = @($allBlocks)
}

function Get-LatestRunInfo {
    param(
        [string]$RootPath
    )

    if (-not (Test-Path $RootPath)) {
        return $null
    }

    $latestRun = Get-ChildItem $RootPath -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $latestRun) {
        return $null
    }

    $latestChild = Get-ChildItem $latestRun.FullName -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    [pscustomobject]@{
        RunName = $latestRun.Name
        RunPath = $latestRun.FullName
        RunLastWrite = $latestRun.LastWriteTime
        LatestFile = if ($latestChild) { $latestChild.Name } else { $null }
        LatestFilePath = if ($latestChild) { $latestChild.FullName } else { $null }
        LatestFileWrite = if ($latestChild) { $latestChild.LastWriteTime } else { $latestRun.LastWriteTime }
    }
}

function Stop-ProcessTree {
    param(
        [int]$ProcessId
    )

    try {
        taskkill /PID $ProcessId /T /F | Out-Null
    }
    catch {
        try {
            Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        }
        catch {
        }
    }
}

function Stop-StaleIntegrationTestHosts {
    param(
        [string]$WorkspacePath
    )

    $patterns = @(
        'HushNode.IntegrationTests',
        'testhost'
    )

    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -in @('dotnet.exe', 'testhost.exe') -and
            $_.CommandLine -and
            $_.CommandLine -like "*HushNode.IntegrationTests*"
        }

    foreach ($proc in $processes) {
        Write-Host "[cleanup] Stopping stale process $($proc.Name) pid=$($proc.ProcessId)"
        try {
            taskkill /PID $proc.ProcessId /T /F | Out-Null
        }
        catch {
        }
    }
}

function Start-BlockProcess {
    param(
        [string]$ProjectPath,
        [string]$Filter,
        [string]$WorkingDirectory,
        [bool]$NoBuild,
        [int]$MaxMinutes
    )

    $arguments = @(
        "test"
        $ProjectPath
        "--filter"
        $Filter
        "--verbosity"
        "minimal"
        "--blame-hang"
        "--blame-hang-timeout"
        ("{0}m" -f $MaxMinutes)
    )

    if ($NoBuild) {
        $arguments += "--no-build"
    }
    else {
        $arguments += "--no-restore"
    }

    Start-Process -FilePath "dotnet" -ArgumentList $arguments -PassThru -WorkingDirectory $WorkingDirectory
}

Write-Host "E2E block runner starting..."
Write-Host "Project: $projectPath"
Write-Host "Blocks: $($selectedBlocks.Count)"
Write-Host "BlockNames: $($selectedBlocks.Name -join ', ')"
Write-Host "PollSeconds: $PollSeconds"
Write-Host "StaleMinutes: $StaleMinutes"
Write-Host "MaxMinutes: $MaxMinutes"
Write-Host "NoBuild: $NoBuild"
Write-Host "RetryOnEarlyStallCount: $RetryOnEarlyStallCount"

foreach ($block in $selectedBlocks) {
    Write-Host ""
    Write-Host "===== START $($block.Name) ====="
    Write-Host "Filter: $($block.Filter)"

    $attempt = 0
    $completed = $false

    while (-not $completed) {
        $attempt++

        Stop-StaleIntegrationTestHosts -WorkspacePath $workspaceRoot
        Start-Sleep -Seconds 2

        $beforeRun = Get-LatestRunInfo -RootPath $testResultsRoot
        $startTime = Get-Date
        $process = Start-BlockProcess -ProjectPath $projectPath -Filter $block.Filter -WorkingDirectory $workspaceRoot -NoBuild $NoBuild -MaxMinutes $MaxMinutes
        $lastReportedRunName = $null
        $staleTriggered = $false
        $firstObservedRun = $null
        $sawArtifact = $false

        if ($attempt -gt 1) {
            Write-Warning "[$($block.Name)] Retry attempt $attempt of $($RetryOnEarlyStallCount + 1)."
        }

        while (-not $process.HasExited) {
            Start-Sleep -Seconds $PollSeconds
            $process.Refresh()

            $latestRun = Get-LatestRunInfo -RootPath $testResultsRoot
            $elapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)

            if ($latestRun -and (($beforeRun -and $latestRun.RunName -ne $beforeRun.RunName) -or (-not $beforeRun))) {
                if (-not $firstObservedRun) {
                    $firstObservedRun = $latestRun
                }

                $inactiveMinutes = [math]::Round(((Get-Date) - $latestRun.LatestFileWrite).TotalMinutes, 2)

                if ($latestRun.RunName -ne $lastReportedRunName) {
                    Write-Host "[$($block.Name)] Run started: $($latestRun.RunName)"
                    $lastReportedRunName = $latestRun.RunName
                }

                if ($latestRun.LatestFile) {
                    $sawArtifact = $true
                }

                Write-Host "[$($block.Name)] elapsed=${elapsed}s run=$($latestRun.RunName) latest=$($latestRun.LatestFile) lastWrite=$($latestRun.LatestFileWrite.ToString('HH:mm:ss')) idle=${inactiveMinutes}m"

                if ($elapsed -ge ($MaxMinutes * 60)) {
                    Write-Warning "[$($block.Name)] Maximum runtime of $MaxMinutes minute(s) reached. Aborting active dotnet test process."
                    Stop-ProcessTree -ProcessId $process.Id
                    $staleTriggered = $true
                    break
                }

                if ($inactiveMinutes -ge $StaleMinutes) {
                    Write-Warning "[$($block.Name)] No TestResults activity for $inactiveMinutes minute(s). Aborting active dotnet test process."
                    Stop-ProcessTree -ProcessId $process.Id
                    $staleTriggered = $true
                    break
                }
            }
            else {
                Write-Host "[$($block.Name)] elapsed=${elapsed}s waiting for TestRun creation..."

                if ($elapsed -ge ($MaxMinutes * 60)) {
                    Write-Warning "[$($block.Name)] Maximum runtime of $MaxMinutes minute(s) reached before TestRun creation. Aborting active dotnet test process."
                    Stop-ProcessTree -ProcessId $process.Id
                    $staleTriggered = $true
                    break
                }

                if ($elapsed -ge ($StaleMinutes * 60)) {
                    Write-Warning "[$($block.Name)] No TestRun folder created within $StaleMinutes minute(s). Aborting active dotnet test process."
                    Stop-ProcessTree -ProcessId $process.Id
                    $staleTriggered = $true
                    break
                }
            }
        }

        try {
            $process.WaitForExit()
        }
        catch {
        }

        $exitCode = if ($staleTriggered) { 124 } else { $process.ExitCode }
        $endElapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)
        $afterRun = Get-LatestRunInfo -RootPath $testResultsRoot

        Write-Host "===== END $($block.Name) exit=$exitCode elapsed=${endElapsed}s ====="

        if ($afterRun -and (($beforeRun -and $afterRun.RunName -ne $beforeRun.RunName) -or (-not $beforeRun))) {
            Write-Host "[$($block.Name)] Final run folder: $($afterRun.RunName)"
            if ($afterRun.LatestFilePath -and (Test-Path $afterRun.LatestFilePath)) {
                Write-Host "[$($block.Name)] Last artifact: $($afterRun.LatestFilePath)"
            }
        }

        $canRetryEarlyStall = $exitCode -eq 124 -and -not $sawArtifact -and $attempt -le $RetryOnEarlyStallCount
        if ($canRetryEarlyStall) {
            Write-Warning "[$($block.Name)] Early stall detected before first artifact. Retrying block in a fresh dotnet test process."
            continue
        }

        if ($exitCode -ne 0 -and $StopOnFailure) {
            throw "Block '$($block.Name)' failed or stalled with exit code $exitCode."
        }

        $completed = $true
    }
}

Write-Host ""
Write-Host "E2E block runner completed."

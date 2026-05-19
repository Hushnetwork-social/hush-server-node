param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptRoot "OperationalEvidencePromoter\OperationalEvidencePromoter.csproj"

dotnet run --project $projectPath -- @Arguments
exit $LASTEXITCODE

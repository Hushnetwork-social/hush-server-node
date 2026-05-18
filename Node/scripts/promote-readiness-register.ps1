param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$PromoterArgs
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "ReadinessRegisterPromoter\ReadinessRegisterPromoter.csproj"

dotnet run --project $project -- @PromoterArgs
exit $LASTEXITCODE

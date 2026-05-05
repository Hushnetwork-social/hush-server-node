param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$PromoterArgs
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "ProtocolPackagePromoter\ProtocolPackagePromoter.csproj"

dotnet run --project $project -- @PromoterArgs
exit $LASTEXITCODE

param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:JUSTYBASE_UPDATE_SNAPSHOTS = "1"

Write-Host "Refreshing visual baselines in source/JustyBase.HeadlessTests/VisualBaselines..."
dotnet test (Join-Path $root "source\JustyBase.HeadlessTests\JustyBase.HeadlessTests.csproj") `
    -c $Configuration `
    --filter "FullyQualifiedName~VisualRegressionTests"

Remove-Item Env:\JUSTYBASE_UPDATE_SNAPSHOTS -ErrorAction SilentlyContinue
Write-Host "Review the PNG changes in git before committing them."

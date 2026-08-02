[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$requiredVariables = @(
    'NZ_DEV_DATABASE',
    'NZ_DEV_HOST',
    'NZ_DEV_USER',
    'NZ_DEV_PASSWORD',
    'NZ_DEV_PORT'
)

$missingVariables = @($requiredVariables | Where-Object {
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
})

if ($missingVariables.Count -gt 0) {
    throw "Netezza integration configuration is incomplete. Missing: $($missingVariables -join ', ')."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'source\JustyBase.IntegrationTests\JustyBase.IntegrationTests.csproj'

dotnet test $project -c Release --filter 'Category=Integration'
if ($LASTEXITCODE -ne 0) {
    throw "Netezza integration test failed with exit code $LASTEXITCODE."
}

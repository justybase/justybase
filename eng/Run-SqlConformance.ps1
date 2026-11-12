[CmdletBinding()]
param(
    [string]$CorpusPath = (Join-Path (Split-Path -Parent $PSScriptRoot) '..\JustyBase.SqlConformance')
)

# JustyBase does not have a separate SQL engine. Its editor and linter use the
# parser/linter from the sibling JustyBase.NetezzaSql project, so the shared
# corpus is applied to that project and the app host smoke verifies the same
# parser/linter is wired into the product.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$corpusRoot = (Resolve-Path $CorpusPath).Path
$libRoot = Join-Path (Split-Path -Parent $repoRoot) 'JustyBase.NetezzaSql'
$env:JUSTYBASE_SQL_CONFORMANCE_PATH = $corpusRoot

Push-Location $repoRoot
try {
    Write-Host '==> shared parser/linter corpus (JustyBase.NetezzaSql)' -ForegroundColor Cyan
    dotnet test (Join-Path $libRoot 'JustyBase.NetezzaSql.sln') `
        --filter 'Category=SqlConformance'
    if ($LASTEXITCODE -ne 0) { throw 'shared SQL conformance tests failed.' }

    Write-Host '==> JustyBase host parser/linter smoke' -ForegroundColor Cyan
    dotnet test .\source\JustyBase.Tests\JustyBase.Tests.csproj `
        --filter 'FullyQualifiedName~SqlIntelligenceParityHostTests'
    if ($LASTEXITCODE -ne 0) { throw 'JustyBase host parser/linter smoke failed.' }
}
finally {
    Pop-Location
}

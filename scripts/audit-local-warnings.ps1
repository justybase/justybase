[CmdletBinding()]
param(
    [string] $Solution = (Join-Path $PSScriptRoot '..\JustyBase.slnx'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoIncremental
)

$ErrorActionPreference = 'Stop'
$solutionPath = [System.IO.Path]::GetFullPath($Solution)
$solutionText = Get-Content -LiteralPath $solutionPath -Raw
$projectPaths = [regex]::Matches($solutionText, '<Project\s+Path="(?<path>[^"]+\.csproj)"') |
    ForEach-Object { $_.Groups['path'].Value } |
    Where-Object { $_ -notmatch '(?i)ProDataGrid' } |
    ForEach-Object { [System.IO.Path]::GetFullPath((Join-Path (Split-Path $solutionPath) $_)) }

$buildOutput = @()
$buildExitCode = 0
foreach ($projectPath in $projectPaths) {
    $buildArguments = @('build', $projectPath, '-c', $Configuration, '--no-restore', '-m:1', '-v', 'minimal')
    if ($NoIncremental) { $buildArguments += '--no-incremental' }
    $buildOutput += & dotnet @buildArguments 2>&1
    if ($LASTEXITCODE -ne 0) { $buildExitCode = $LASTEXITCODE }
}
$warningPattern = [regex]'warning\s+(?<code>[A-Z]{2,5}\d+):'
$projectPattern = [regex]'\[(?<project>[^\]]+\.csproj)\]'
$warnings = foreach ($line in $buildOutput) {
    $text = $line.ToString()
    $warning = $warningPattern.Match($text)
    $project = $projectPattern.Match($text)
    if (-not $warning.Success -or -not $project.Success) { continue }

    # ProDataGrid is an intentionally external sibling checkout. Its warnings
    # are not part of this repository's quality gate.
    if ($text -match '(?i)ProDataGrid|Avalonia\.Controls\.DataGrid') { continue }

    [pscustomobject]@{
        Code = $warning.Groups['code'].Value
        Project = [System.IO.Path]::GetFullPath($project.Groups['project'].Value)
        Message = $text
    }
}

$uniqueWarnings = @($warnings | Sort-Object Code, Project, Message -Unique)
Write-Output "Build exit code: $buildExitCode"
Write-Output "Unique local warnings: $($uniqueWarnings.Count)"
if ($uniqueWarnings.Count -gt 0) {
    Write-Output "`nBy rule:"
    $uniqueWarnings | Group-Object Code | Sort-Object Count -Descending |
        Format-Table Count, Name -AutoSize | Out-String | Write-Output
    Write-Output "By project:"
    $uniqueWarnings | Group-Object Project | Sort-Object Count -Descending |
        Select-Object -First 30 Count, Name |
        Format-Table Count, Name -AutoSize | Out-String | Write-Output
}
if ($buildExitCode -ne 0) { exit $buildExitCode }

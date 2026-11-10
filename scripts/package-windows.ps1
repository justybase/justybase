[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [Parameter(Mandatory = $true)] [string] $OutputRoot,
    [Parameter(Mandatory = $false)] [string] $GitHubToken = '',
    [Parameter(Mandatory = $false)] [switch] $Slim,
    # Slim ships its own Velopack installer (separate id/channel), so -NoVelopack
    # is only an offline/measurement escape hatch: ZIP without feed or Setup.exe.
    [Parameter(Mandatory = $false)] [switch] $NoVelopack
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$') { throw "Invalid version format: $Version (expected e.g. 1.4.0 or 1.4.0-rc.1)" }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'source\JustyBase\JustyBase.csproj'
$releaseNotes = Join-Path $root 'source\JustyBase\ReleaseNotes.md'
$icon = Join-Path $root 'source\JustyBase\Assets\Icon2.ico'
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
# Full: all database drivers + AI + animated splash (Velopack channel win).
# Slim: Netezza + SQLite + DB2 + Postgres + MySQL, no AI, static splash (Velopack channel
# win-slim). Same package id: channels are Velopack's native mechanism for
# parallel variants and UpdateManager automatically tracks the channel the
# release was built for (see https://docs.velopack.io/packaging/channels).
# Channel names are part of the nupkg filenames, so Full and Slim assets
# never collide within one GitHub release.
$variant = if ($Slim) { 'aot-self-contained-slim' } else { 'aot-self-contained' }
$channel = if ($Slim) { 'win-slim' } else { 'win' }
$packTitle = if ($Slim) { 'JustyBase Slim' } else { 'JustyBase' }
$publish = Join-Path $output "work\win-x64-$variant"
$velopack = Join-Path $output 'velopack'
$zip = Join-Path $output "JustyBase-$Version-win-x64-$variant.zip"
$setup = Join-Path $output "JustyBase-$Version-win-x64-$variant-Setup.exe"

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $releaseNotes)) { throw "Release notes not found: $releaseNotes" }
if (-not (Test-Path $icon)) { throw "Icon not found: $icon" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is required' }
if (-not $NoVelopack -and -not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'vpk is required' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio C++ Build Tools are required for Native AOT' }
$vsInstallPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($vsInstallPath)) {
    throw 'Visual Studio C++ x64 Build Tools are required for Native AOT'
}
$vcvars64 = Join-Path $vsInstallPath 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars64)) { throw "Visual Studio x64 compiler environment not found: $vcvars64" }
$vsInstallerPath = Split-Path -Parent $vswhere
$env:PATH = "$vsInstallerPath;$env:PATH"
$vcvarsOutput = & $env:ComSpec /d /c "call `"$vcvars64`" >nul 2>&1 && set"
if ($LASTEXITCODE -ne 0) { throw "Failed to initialize Visual Studio x64 compiler environment (exit code $LASTEXITCODE)" }
foreach ($line in $vcvarsOutput) {
    if ($line -match '^([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
    }
}
if (-not (Test-Path (Join-Path $env:VCToolsInstallDir 'bin\Hostx64\x64\cl.exe'))) {
    throw 'Visual Studio x64 C++ compiler was not added to the build environment'
}

Remove-Item $publish, $zip, $setup -Recurse -Force -ErrorAction SilentlyContinue
if (-not $NoVelopack) { Remove-Item $velopack -Recurse -Force -ErrorAction SilentlyContinue }
$dirsToCreate = @($publish, $output)
if (-not $NoVelopack) { $dirsToCreate += $velopack }
New-Item -ItemType Directory -Path $dirsToCreate -Force | Out-Null

$enableDb2Plugin = 'true'
$enableAccessPlugin = if ($Slim) { 'false' } else { 'true' }
$enablePostgresPlugin = 'true'
$enableOraclePlugin = if ($Slim) { 'false' } else { 'true' }
$enableMySqlPlugin = 'true'
$enableDuckDbPlugin = if ($Slim) { 'false' } else { 'true' }
$enableExcelPlugin = if ($Slim) { 'false' } else { 'true' }
$enableAi = if ($Slim) { 'false' } else { 'true' }
$enableLottie = if ($Slim) { 'false' } else { 'true' }

dotnet publish $project -r win-x64 -c Release -f net11.0 `
    "-p:EnableDb2Plugin=$enableDb2Plugin" `
    "-p:EnableAccessPlugin=$enableAccessPlugin" `
    "-p:EnablePostgresPlugin=$enablePostgresPlugin" `
    "-p:EnableOraclePlugin=$enableOraclePlugin" `
    "-p:EnableMySqlPlugin=$enableMySqlPlugin" `
    "-p:EnableDuckDbPlugin=$enableDuckDbPlugin" `
    "-p:EnableExcelPlugin=$enableExcelPlugin" `
    "-p:EnableAi=$enableAi" `
    "-p:EnableLottie=$enableLottie" `
    -p:CopilotSkipCliDownload=true `
    -p:PublishAot=true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=true `
    --self-contained true -p:DebugType=None -p:DebugSymbols=false `
    -p:UseSharedCompilation=false `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Native AOT publish failed with exit code $LASTEXITCODE" }
$runtimeRoot = Join-Path $publish 'runtimes'
if (Test-Path $runtimeRoot) {
    Get-ChildItem $runtimeRoot -Directory |
        Where-Object { $_.Name -ne 'win-x64' } |
        Remove-Item -Recurse -Force
}
Get-ChildItem $publish -Recurse -Include '*.pdb', '*.dbg' -File -ErrorAction SilentlyContinue | Remove-Item -Force

# Download the latest prerelease feed before packing so vpk can generate deltas.
# A first release, an unavailable GitHub API, or an offline local build may still
# produce a valid full package, so this step is intentionally best effort.
# Skipped entirely with -NoVelopack (Slim ships as a portable ZIP only).
$githubRepoUrl = 'https://github.com/justybase/justybase'
if (-not $NoVelopack) {
$downloadArgs = @(
    'download', 'github',
    '--repoUrl', $githubRepoUrl,
    '--outputDir', $velopack,
    '--channel', $channel,
    '--pre'
)
if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
    $downloadArgs += @('--token', $GitHubToken)
}
try {
    & vpk @downloadArgs
    if ($LASTEXITCODE -ne 0) { throw "vpk download exited with code $LASTEXITCODE" }
}
catch {
    Write-Warning "Could not download the previous GitHub release; continuing with a full package: $($_.Exception.Message)"
}

vpk pack -u JustyBase -v $Version -p $publish -e JustyBase.exe --runtime win-x64 `
    --packAuthors 'JustyBase' --packTitle $packTitle --channel $channel -o $velopack `
    -i $icon --releaseNotes $releaseNotes
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

$feed = Join-Path $velopack "releases.$channel.json"
# The nupkg filename embeds the channel (e.g. JustyBase-1.0-full.nupkg vs
# JustyBase-1.0-win-slim-full.nupkg). A delta build also leaves the downloaded
# previous full nupkg next to the new one, so match by current version.
$fullPackages = @(Get-ChildItem $velopack -Filter '*-full.nupkg' -File | Where-Object { $_.Name -like "*-$Version-*" })
if ($fullPackages.Count -ne 1) { throw "Expected exactly one $Version full nupkg in $velopack, found $($fullPackages.Count)" }
$fullPackage = $fullPackages[0]
if (-not (Test-Path $feed)) { throw "Velopack did not produce releases.$channel.json" }

$feedDocument = Get-Content $feed -Raw | ConvertFrom-Json
$feedAsset = @($feedDocument.Assets) |
    Where-Object { $_.PackageId -eq 'JustyBase' -and $_.Version -eq $Version -and $_.Type -eq 'Full' } |
    Select-Object -First 1
if ($null -eq $feedAsset) { throw "Velopack feed does not contain the full package for version $Version" }
if ($feedAsset.FileName -ne $fullPackage.Name) { throw 'Velopack feed filename does not match the generated full package' }
if ([int64]$feedAsset.Size -ne $fullPackage.Length) { throw 'Velopack feed size does not match the generated full package' }

$setupSource = Get-ChildItem $velopack -Filter '*-Setup.exe' -File | Select-Object -First 1
if ($null -eq $setupSource) { throw 'Velopack did not produce a Setup.exe' }
Copy-Item $setupSource.FullName $setup -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
if (-not (Test-Path $zip) -or (Get-Item $zip).Length -eq 0) { throw 'Windows ZIP was not created' }
$zipArchive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $debugEntry = $zipArchive.Entries | Where-Object { $_.Name -match '\.(pdb|dbg)$' } | Select-Object -First 1
    if ($null -ne $debugEntry) { throw "Debug file found in Windows ZIP: $($debugEntry.FullName)" }
    $foreignRuntimeEntry = $zipArchive.Entries |
        Where-Object { $_.FullName -match '^runtimes/([^/]+)/' -and $Matches[1] -ne 'win-x64' } |
        Select-Object -First 1
    if ($null -ne $foreignRuntimeEntry) { throw "Foreign runtime found in Windows ZIP: $($foreignRuntimeEntry.FullName)" }
}
finally {
    $zipArchive.Dispose()
}
Write-Host ''
Write-Host 'Publikacja Windows zakonczona pomyslnie.'
if (-not $NoVelopack) { Write-Host "Instalator: $setup" }
Write-Host "Archiwum:   $zip"

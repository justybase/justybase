[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [Parameter(Mandatory = $true)] [string] $OutputRoot,
    [Parameter(Mandatory = $false)] [string] $GitHubToken = '',
    [Parameter(Mandatory = $false)] [switch] $Light
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'source\JustyBase\JustyBase.csproj'
$releaseNotes = Join-Path $root 'source\JustyBase\ReleaseNotes.md'
$icon = Join-Path $root 'source\JustyBase\Assets\Icon2.ico'
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
$variant = if ($Light) { 'aot-self-contained-light' } else { 'aot-self-contained' }
$publish = Join-Path $output "work\win-x64-$variant"
$velopack = Join-Path $output 'velopack'
$zip = Join-Path $output "JustyBase-$Version-win-x64-$variant.zip"
$setup = Join-Path $output "JustyBase-$Version-win-x64-$variant-Setup.exe"

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $releaseNotes)) { throw "Release notes not found: $releaseNotes" }
if (-not (Test-Path $icon)) { throw "Icon not found: $icon" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is required' }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'vpk is required' }
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

Remove-Item $publish, $velopack, $zip, $setup -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish, $velopack, $output -Force | Out-Null

$enableAccessPlugin = if ($Light) { 'false' } else { 'true' }
$enablePostgresPlugin = if ($Light) { 'false' } else { 'true' }

dotnet publish $project -r win-x64 -c Release -f net10.0 `
    -p:EnableDb2Plugin=true `
    "-p:EnableAccessPlugin=$enableAccessPlugin" `
    "-p:EnablePostgresPlugin=$enablePostgresPlugin" `
    -p:EnableOraclePlugin=false `
    -p:EnableDuckDbPlugin=false `
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
$githubRepoUrl = 'https://github.com/justybase/justybase'
$downloadArgs = @(
    'download', 'github',
    '--repoUrl', $githubRepoUrl,
    '--outputDir', $velopack,
    '--channel', 'win',
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
    --packAuthors 'JustyBase' --packTitle 'JustyBase' --channel win -o $velopack `
    -i $icon --releaseNotes $releaseNotes
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

$feed = Join-Path $velopack 'releases.win.json'
$fullPackage = Get-ChildItem $velopack -Filter "JustyBase-$Version-full.nupkg" -File | Select-Object -First 1
if (-not (Test-Path $feed)) { throw 'Velopack did not produce releases.win.json' }
if ($null -eq $fullPackage) { throw "Velopack did not produce JustyBase-$Version-full.nupkg" }

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
Write-Host "Created $setup"
Write-Host "Created $zip"

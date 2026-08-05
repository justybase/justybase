[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [Parameter(Mandatory = $true)] [string] $OutputRoot,
    [ValidateSet('aot-netezza', 'self-contained-netezza-db2')]
    [string] $Variant = 'aot-netezza'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'source\JustyBase\JustyBase.csproj'
$releaseNotes = Join-Path $root 'source\JustyBase\ReleaseNotes.md'
$icon = Join-Path $root 'source\JustyBase\Assets\Icon2.ico'
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
$publish = Join-Path $output "work\win-x64-$Variant"
$velopack = Join-Path $output 'velopack'
$zip = Join-Path $output "JustyBase-$Version-win-x64-$Variant.zip"
$setup = Join-Path $output "JustyBase-$Version-win-x64-$Variant-Setup.exe"

$enableAot = $Variant -eq 'aot-netezza'
$enableDb2 = $Variant -eq 'self-contained-netezza-db2'

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $releaseNotes)) { throw "Release notes not found: $releaseNotes" }
if (-not (Test-Path $icon)) { throw "Icon not found: $icon" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is required' }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'vpk is required' }

Remove-Item $publish, $velopack, $zip, $setup -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish, $velopack, $output -Force | Out-Null

dotnet publish $project -r win-x64 -c Release -f net10.0 `
    -p:EnableAOT=$($enableAot.ToString().ToLowerInvariant()) `
    -p:EnableDb2Plugin=$($enableDb2.ToString().ToLowerInvariant()) `
    -p:PublishAot=$($enableAot.ToString().ToLowerInvariant()) `
    --self-contained true -p:DebugType=None -p:DebugSymbols=false `
    -p:UseSharedCompilation=false `
    -p:UseLocalJustyBaseLibraries=false `
    -p:Version=$Version -o $publish
$runtimeRoot = Join-Path $publish 'runtimes'
if (Test-Path $runtimeRoot) {
    Get-ChildItem $runtimeRoot -Directory |
        Where-Object { $_.Name -ne 'win-x64' } |
        Remove-Item -Recurse -Force
}
Get-ChildItem $publish -Recurse -Include '*.pdb', '*.dbg' -File -ErrorAction SilentlyContinue | Remove-Item -Force

vpk pack -u JustyBase -v $Version -p $publish -e JustyBase.exe `
    --packAuthors 'JustyBase' --packTitle 'JustyBase' -o $velopack `
    -i $icon --releaseNotes $releaseNotes

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

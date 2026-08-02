[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [Parameter(Mandatory = $true)] [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'source\JustyBase\JustyBase.csproj'
$releaseNotes = Join-Path $root 'source\JustyBase\ReleaseNotes.md'
$icon = Join-Path $root 'source\JustyBase\Assets\Icon2.ico'
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
$publish = Join-Path $output 'work\win-x64'
$velopack = Join-Path $output 'velopack'
$zip = Join-Path $output "JustyBase-$Version-win-x64.zip"
$setup = Join-Path $output "JustyBase-$Version-win-x64-Setup.exe"

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $releaseNotes)) { throw "Release notes not found: $releaseNotes" }
if (-not (Test-Path $icon)) { throw "Icon not found: $icon" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is required' }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'vpk is required' }

Remove-Item $publish, $velopack, $zip, $setup -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish, $velopack, $output -Force | Out-Null

dotnet publish $project -r win-x64 -c Release -f net10.0 `
    -p:EnableAOT=true -p:DebugType=None -p:DebugSymbols=false `
    -p:UseLocalJustyBaseLibraries=false `
    -p:Version=$Version -o $publish
Get-ChildItem $publish -Include '*.pdb', '*.dbg' -File -ErrorAction SilentlyContinue | Remove-Item -Force

vpk pack -u JustyBase -v $Version -p $publish -e JustyBase.exe `
    --packAuthors 'JustyBase' --packTitle 'JustyBase' -o $velopack `
    -i $icon --releaseNotes $releaseNotes

$setupSource = Get-ChildItem $velopack -Filter '*-Setup.exe' -File | Select-Object -First 1
if ($null -eq $setupSource) { throw 'Velopack did not produce a Setup.exe' }
Copy-Item $setupSource.FullName $setup -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
if (-not (Test-Path $zip) -or (Get-Item $zip).Length -eq 0) { throw 'Windows ZIP was not created' }
Write-Host "Created $setup"
Write-Host "Created $zip"

[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'
}

$packagesRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
if (-not (Test-Path $packagesRoot)) {
    throw "NuGet packages folder not found: $packagesRoot. Run 'dotnet restore JustyBase.slnx' first."
}

# Production projects only: the application, its plugins/drivers, the editor and
# the sibling Netezza/UCanAccess libraries that ship with the application.
$excludedProjectSegment = '\\(obj|bin|tests|test|Benchmarks|samples|eng|JustyBase\.Tests|JustyBase\.HeadlessTests|JustyBase\.IntegrationTests|JustyBase\.LiveTests|JustyBase\.Screenshot)\\'

$projectRoots = @(
    (Join-Path $repoRoot 'source'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.NetezzaSqlParser.csproj'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.Netezza'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.NetezzaDdl'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.NetezzaCatalogSql'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.Core'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.ImportExport'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.Ai'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.Ai.Contracts'),
    (Join-Path $repoRoot '..\JustyBase.NetezzaSql\JustyBase.Ai.Embedded'),
    (Join-Path $repoRoot '..\JustyBase.UCanAccessCs\src')
)

$projects = New-Object System.Collections.Generic.List[string]
foreach ($root in $projectRoots) {
    if (-not (Test-Path $root)) {
        Write-Warning "Skipping missing production project root: $root"
        continue
    }

    if (Test-Path $root -PathType Leaf) {
        if ($root -notmatch $excludedProjectSegment) {
            $projects.Add((Resolve-Path $root).Path)
        }
        continue
    }

    foreach ($project in Get-ChildItem -Path $root -Recurse -File -Filter *.csproj) {
        if ($project.FullName -match $excludedProjectSegment) {
            continue
        }
        $projects.Add($project.FullName)
    }
}

$projects = $projects | Sort-Object -Unique
if ($projects.Count -eq 0) {
    throw 'No production projects found.'
}

$resolved = @{}
$missingAssets = New-Object System.Collections.Generic.List[string]
foreach ($project in $projects) {
    $assetsPath = Join-Path (Split-Path -Parent $project) 'obj\project.assets.json'
    if (-not (Test-Path $assetsPath)) {
        $missingAssets.Add($project)
        continue
    }

    $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') {
            continue
        }

        # First-party packages are our own code, not a third-party notice item.
        if ($library.Name -like 'JustyBase.*') {
            continue
        }

        $parts = $library.Name -split '/', 2
        $resolved[$library.Name] = [pscustomobject]@{ Id = $parts[0]; Version = $parts[1] }
    }
}

if ($missingAssets.Count -gt 0) {
    throw ("Missing project.assets.json for:`n  " + ($missingAssets -join "`n  ") + "`nRun 'dotnet restore JustyBase.slnx' first.")
}

function Get-PackageLicense {
    param([string]$Id, [string]$Version)

    $nuspecPath = Join-Path $packagesRoot (Join-Path $Id.ToLowerInvariant() (Join-Path $Version ($Id.ToLowerInvariant() + '.nuspec')))
    if (-not (Test-Path $nuspecPath)) {
        throw "Missing nuspec for $Id $Version at $nuspecPath. Run 'dotnet restore JustyBase.slnx' first."
    }

    $nuspec = [xml](Get-Content $nuspecPath -Raw)
    $metadata = $nuspec.package.metadata
    $license = $metadata.license
    $licenseText = $null
    if ($license) {
        if ($license.type -eq 'expression' -and $license.InnerText) {
            $licenseText = $license.InnerText.Trim()
        }
        elseif ($license.type -eq 'file') {
            $licenseFile = if ($license.InnerText) { $license.InnerText.Trim() } else { 'LICENSE' }
            $licenseText = "See package license file ($licenseFile)"
        }
        elseif ($license.InnerText) {
            $licenseText = $license.InnerText.Trim()
        }
    }
    if (-not $licenseText -and $metadata.licenseUrl) {
        $licenseText = $metadata.licenseUrl.Trim()
    }
    if (-not $licenseText) {
        $licenseText = 'Not declared in package metadata'
    }

    $projectUrl = $metadata.projectUrl
    if (-not $projectUrl -and $metadata.repository -and $metadata.repository.url) {
        $projectUrl = $metadata.repository.url
    }

    [pscustomobject]@{
        Id = $Id
        Version = $Version
        License = $licenseText
        ProjectUrl = if ($projectUrl) { $projectUrl.Trim() } else { '' }
    }
}

function Format-Cell {
    param([string]$Value)
    if (-not $Value) { return '-' }
    return $Value.Replace('|', '\|')
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($entry in ($resolved.Values | Sort-Object Id, Version)) {
    $rows.Add((Get-PackageLicense -Id $entry.Id -Version $entry.Version))
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('# Third-party notices')
[void]$builder.AppendLine()
[void]$builder.AppendLine('JustyBase includes the third-party NuGet packages listed below. The inventory is generated')
[void]$builder.AppendLine('from the production dependency graphs (application, plugins, drivers, editor and the required')
[void]$builder.AppendLine('Netezza/UCanAccess libraries). Test, benchmark and screenshot projects are excluded.')
[void]$builder.AppendLine()
[void]$builder.AppendLine('Regenerate with:')
[void]$builder.AppendLine()
[void]$builder.AppendLine('```powershell')
[void]$builder.AppendLine('pwsh ./scripts/generate-third-party-notices.ps1')
[void]$builder.AppendLine('```')
[void]$builder.AppendLine()
[void]$builder.AppendLine('Do not edit this file manually. CI fails when it is out of date.')
[void]$builder.AppendLine()
[void]$builder.AppendLine("Packages: $($rows.Count). Licenses are taken from each package's nuspec metadata.")
[void]$builder.AppendLine()
[void]$builder.AppendLine('| Package | Version | License | Project |')
[void]$builder.AppendLine('| --- | --- | --- | --- |')
foreach ($row in $rows) {
    $packageCell = '[{0}](https://www.nuget.org/packages/{0}/{1})' -f $row.Id, $row.Version
    [void]$builder.AppendLine(('| {0} | {1} | {2} | {3} |' -f $packageCell, $row.Version, (Format-Cell $row.License), (Format-Cell $row.ProjectUrl)))
}
[void]$builder.AppendLine()
[void]$builder.AppendLine('## License notes')
[void]$builder.AppendLine()
[void]$builder.AppendLine('- `MySql.Data` is **GPL-2.0-only WITH Universal-FOSS-exception-1.0**. It is used only by the optional MySQL driver. The FOSS exception covers distribution as part of this MIT-licensed project; review the exception before shipping a non-FOSS build.')
[void]$builder.AppendLine('- `Oracle.ManagedDataAccess.Core` ships under the Oracle Universal Permissive License; the package license file is authoritative.')
[void]$builder.AppendLine('- `ProDataGrid` and its formula-engine assemblies are **MIT**. They are resolved from nuget.org only (`nuget.config` pins the feed).')
[void]$builder.AppendLine()
[void]$builder.AppendLine('## Components not distributed as NuGet packages')
[void]$builder.AppendLine()
[void]$builder.AppendLine('- IBM Netezza server software and its client libraries are not bundled; the application connects over the network.')
[void]$builder.AppendLine('- Oracle Instant Client is not bundled; Oracle connections require a separately installed, licensed client.')
[void]$builder.AppendLine('- The Microsoft Access Database Engine and the Java UCanAccess runtime are not bundled; Access support uses the managed `JustyBase.UCanAccessCs` provider.')
[void]$builder.AppendLine('- `llama-server` and GGUF model files are downloaded on demand for optional local AI features and are not part of the installer.')
[void]$builder.AppendLine()

$generated = $builder.ToString()
$generated = $generated -replace "`r`n", "`n"

if ($Check) {
    if (-not (Test-Path $OutputPath)) {
        Write-Error "Missing $OutputPath. Run 'pwsh ./scripts/generate-third-party-notices.ps1'."
        exit 1
    }

    $current = (Get-Content $OutputPath -Raw) -replace "`r`n", "`n"
    if ($current -ne $generated) {
        Write-Error "$OutputPath is out of date. Run 'pwsh ./scripts/generate-third-party-notices.ps1' and commit the result."
        exit 1
    }

    Write-Host "$OutputPath is up to date ($($rows.Count) packages)."
    exit 0
}

[System.IO.File]::WriteAllText($OutputPath, $generated, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $OutputPath ($($rows.Count) packages)."

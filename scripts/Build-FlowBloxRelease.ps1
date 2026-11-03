<#
.SYNOPSIS
Builds the existing WiX installer with the version derived from a release tag.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'Get-FlowBloxReleaseVersion.ps1') -Tag $Tag

& (Join-Path $PSScriptRoot 'update-version.ps1') -Version $version
& (Join-Path $PSScriptRoot 'Build-FlowBloxMsi.ps1')

$msiName = "FlowBlox_${version}.0_x64.msi"
$msiPath = Join-Path $repoRoot "artifacts/installer/msi/$msiName"
if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
    throw "The release installer was not produced: $msiPath"
}

$checksum = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$msiPath.sha256", "$checksum  $msiName`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Release $Tag built: $msiPath"

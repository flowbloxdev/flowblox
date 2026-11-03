<#
.SYNOPSIS
Creates a local annotated release tag; never fetches, pushes, or publishes.
.DESCRIPTION
Increments minor by default. Major and patch increments require an explicit switch.
#>
[CmdletBinding(DefaultParameterSetName = 'Minor')]
param(
    [Parameter(ParameterSetName = 'Major')]
    [switch]$Major,
    [Parameter(ParameterSetName = 'Patch')]
    [switch]$Patch,
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$Arguments)

    & git -C $RepositoryPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

if ((Invoke-Git -Arguments @('branch', '--show-current')) -cne 'main') {
    throw 'Create release tags from main. Switch to the intended, checked main commit first.'
}
if (Invoke-Git -Arguments @('status', '--porcelain')) {
    throw 'The working tree must be clean before creating a release tag. Commit or preserve your changes first.'
}

$versions = @(Invoke-Git -Arguments @('tag', '--list', 'v*') | ForEach-Object {
    if ($_ -cmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        $versionText = & (Join-Path $PSScriptRoot 'Get-FlowBloxReleaseVersion.ps1') -Tag $_
        [version]$versionText
    }
})
if ($versions.Count -gt 0) {
    $current = $versions | Sort-Object -Descending | Select-Object -First 1
}
else {
    [xml]$project = Get-Content -LiteralPath (Join-Path $RepositoryPath 'FlowBlox.Avalonia/FlowBlox.Avalonia.csproj') -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode) {
        throw 'No release tag or active application Version was found.'
    }
    $versionText = & (Join-Path $PSScriptRoot 'Get-FlowBloxReleaseVersion.ps1') -Tag "v$($versionNode.InnerText)"
    $current = [version]$versionText
}

if ($Major) {
    $nextTag = "v$($current.Major + 1).0.0"
}
elseif ($Patch) {
    $nextTag = "v$($current.Major).$($current.Minor).$($current.Build + 1)"
}
else {
    $nextTag = "v$($current.Major).$($current.Minor + 1).0"
}
& (Join-Path $PSScriptRoot 'Get-FlowBloxReleaseVersion.ps1') -Tag $nextTag | Out-Null
$targetCommit = Invoke-Git -Arguments @('rev-parse', 'HEAD')
Invoke-Git -Arguments @('tag', '-a', $nextTag, '-m', "FlowBlox $($nextTag.Substring(1))")
Write-Host "Created local tag $nextTag at $targetCommit. Nothing was pushed or published."
return $nextTag

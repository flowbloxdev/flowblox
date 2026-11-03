<#
.SYNOPSIS
Tests local-only release tagging in a disposable repository without a remote.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptsDirectory
$fixture = Join-Path $repoRoot ('TestResults/release-automation/tags-' + [guid]::NewGuid().ToString('N'))
$projectDirectory = Join-Path $fixture 'FlowBlox.Avalonia'
New-Item -ItemType Directory -Path $projectDirectory -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $projectDirectory 'FlowBlox.Avalonia.csproj'),
    '<Project><PropertyGroup><Version>0.0.0</Version></PropertyGroup></Project>')

function Invoke-FixtureGit {
    param([string[]]$Arguments)

    & git -C $fixture @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git command failed: $($Arguments[0])"
    }
}

Invoke-FixtureGit -Arguments @('init', '-b', 'main') | Out-Null
Invoke-FixtureGit -Arguments @('config', 'user.name', 'FlowBlox release test')
Invoke-FixtureGit -Arguments @('config', 'user.email', 'release-test@example.invalid')
Invoke-FixtureGit -Arguments @('config', 'tag.gpgSign', 'false')
Invoke-FixtureGit -Arguments @('commit', '--allow-empty', '-m', 'Initial fixture commit') | Out-Null
Invoke-FixtureGit -Arguments @('add', '.')
Invoke-FixtureGit -Arguments @('commit', '-m', 'Fixture application version') | Out-Null
$originalCommit = Invoke-FixtureGit -Arguments @('rev-parse', 'HEAD')
$helper = Join-Path $scriptsDirectory 'New-FlowBloxReleaseTag.ps1'

foreach ($case in @(
    @{ Expected = 'v0.1.0'; Options = @{} },
    @{ Expected = 'v0.2.0'; Options = @{} },
    @{ Expected = 'v0.2.1'; Options = @{ Patch = $true } },
    @{ Expected = 'v1.0.0'; Options = @{ Major = $true } }
)) {
    $options = $case.Options
    $tag = & $helper -RepositoryPath $fixture @options
    if ($tag -cne $case.Expected) {
        throw "Expected tag $($case.Expected), got '$tag'."
    }
    if ((Invoke-FixtureGit -Arguments @('cat-file', '-t', $tag)) -cne 'tag') {
        throw 'The release tag was not annotated.'
    }
    if ((Invoke-FixtureGit -Arguments @('rev-parse', "$tag^{}")) -cne $originalCommit) {
        throw 'The release tag points to an unexpected commit.'
    }
}

function Assert-TagRejected {
    $before = @(Invoke-FixtureGit -Arguments @('tag', '--list'))
    $rejected = $false
    try {
        & $helper -RepositoryPath $fixture | Out-Null
    }
    catch {
        $rejected = $true
    }
    $after = @(Invoke-FixtureGit -Arguments @('tag', '--list'))
    if (-not $rejected -or ($before -join ',') -cne ($after -join ',')) {
        throw 'An unsafe tag operation was not rejected without creating a tag.'
    }
}

$dirtyFile = Join-Path $fixture 'uncommitted.txt'
[System.IO.File]::WriteAllText($dirtyFile, 'Uncommitted change')
Assert-TagRejected
Remove-Item -LiteralPath $dirtyFile
Invoke-FixtureGit -Arguments @('switch', '--quiet', '-c', 'feature/test')
Assert-TagRejected
Invoke-FixtureGit -Arguments @('switch', '--quiet', 'main')
Invoke-FixtureGit -Arguments @('tag', '-a', 'v1.255.0', '-m', 'Minor limit fixture')
Assert-TagRejected

if ((Invoke-FixtureGit -Arguments @('rev-parse', 'HEAD')) -cne $originalCommit -or
    (Invoke-FixtureGit -Arguments @('status', '--porcelain')) -or
    (Invoke-FixtureGit -Arguments @('remote'))) {
    throw 'Release tagging changed the checkout or required a remote.'
}
Write-Host 'Local release tag checks passed: minor default, explicit patch/major, annotated tags, clean main guard, and no automatic major rollover.'

<#
.SYNOPSIS
Checks release version validation and version propagation in isolated project fixtures.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptsDirectory
$validator = Join-Path $scriptsDirectory 'Get-FlowBloxReleaseVersion.ps1'

foreach ($tag in @('v0.0.0', 'v1.2.3', 'v255.255.65535')) {
    $version = & $validator -Tag $tag
    if ($version -cne $tag.Substring(1)) {
        throw "Incorrect version derived from '$tag'."
    }
}

foreach ($tag in @('1.2.3', 'V1.2.3', 'v01.2.3', 'v1.2', 'v1.2.3.4', 'v1.2.3-rc.1',
                  'v256.0.0', 'v1.256.0', 'v1.2.65536', 'v999999999999.0.0')) {
    $rejected = $false
    try {
        & $validator -Tag $tag | Out-Null
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Invalid release tag '$tag' was accepted."
    }
}

$fixture = Join-Path $repoRoot ('TestResults/release-automation/' + [guid]::NewGuid().ToString('N'))
$fixtureScripts = Join-Path $fixture 'scripts'
New-Item -ItemType Directory -Path $fixtureScripts -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $scriptsDirectory 'update-version.ps1') -Destination $fixtureScripts

$projects = @(
    'FlowBlox.Avalonia', 'FlowBlox.AIAssistant', 'FlowBlox.CLI', 'FlowBlox.Core',
    'FlowBlox.RunnerHost', 'FlowBlox.WorkerService', 'FlowBlox.UICore', 'FlowBlox.SequenceDetection'
)
foreach ($project in $projects) {
    $projectDirectory = Join-Path $fixture $project
    New-Item -ItemType Directory -Path $projectDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $projectDirectory "$project.csproj"),
        '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>0.1.0</Version></PropertyGroup></Project>')
}

& (Join-Path $fixtureScripts 'update-version.ps1') -Version '1.2.3' | Out-Null
foreach ($project in $projects) {
    [xml]$xml = Get-Content -LiteralPath (Join-Path $fixture "$project/$project.csproj") -Raw
    foreach ($entry in @{ Version = '1.2.3'; AssemblyVersion = '1.2.3.0'; FileVersion = '1.2.3.0' }.GetEnumerator()) {
        $node = $xml.SelectSingleNode("/Project/PropertyGroup/$($entry.Key)")
        if ($null -eq $node -or $node.InnerText -cne $entry.Value) {
            throw "Version propagation failed for $project/$($entry.Key)."
        }
    }
}

$rejected = $false
try {
    & (Join-Path $fixtureScripts 'update-version.ps1') -Version '1.2.4' -Root (Join-Path $fixture 'missing') | Out-Null
}
catch {
    $rejected = $true
}
if (-not $rejected) {
    throw 'A missing project did not stop version propagation.'
}

# Exercise the orchestration without building/publishing the real checkout or invoking GitHub.
foreach ($scriptName in @('Get-FlowBloxReleaseVersion.ps1', 'Build-FlowBloxRelease.ps1', 'Build-FlowBloxMsi.ps1')) {
    Copy-Item -LiteralPath (Join-Path $scriptsDirectory $scriptName) -Destination $fixtureScripts
}
$installerDirectory = Join-Path $fixture 'FlowBlox.Installer'
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $installerDirectory 'FlowBlox.Installer.wixproj'), '<Project />')
$releaseFixtureState = [pscustomobject]@{ FailPublish = $false }
function dotnet {
    $arguments = @($args)
    $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'publish') {
        if ($releaseFixtureState.FailPublish) {
            $global:LASTEXITCODE = 1
            return
        }
        $outputDirectory = $arguments[[array]::IndexOf($arguments, '-o') + 1]
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        $name = [System.IO.Path]::GetFileNameWithoutExtension($arguments[1])
        if ($name -eq 'FlowBlox.Avalonia') { $name = 'FlowBlox' }
        [System.IO.File]::WriteAllText((Join-Path $outputDirectory "$name.exe"), "fixture $name")
        [System.IO.File]::WriteAllText((Join-Path $outputDirectory 'shared.dll'), 'identical shared dependency')
    }
    elseif ($arguments[0] -eq 'build') {
        $outputDirectory = ($arguments | Where-Object { $_ -like '/p:OutputPath=*' }).Substring('/p:OutputPath='.Length)
        $outputName = ($arguments | Where-Object { $_ -like '/p:OutputName=*' }).Substring('/p:OutputName='.Length)
        [System.IO.File]::WriteAllText((Join-Path $outputDirectory "$outputName.msi"), 'fixture installer')
    }
    else {
        throw "Unexpected dotnet operation in release fixture: $($arguments[0])"
    }
}

& (Join-Path $fixtureScripts 'Build-FlowBloxRelease.ps1') -Tag 'v1.2.3'
$fixtureMsi = Join-Path $fixture 'artifacts/installer/msi/FlowBlox_1.2.3.0_x64.msi'
$expectedChecksum = (Get-FileHash -LiteralPath $fixtureMsi -Algorithm SHA256).Hash.ToLowerInvariant()
$actualChecksum = [System.IO.File]::ReadAllText("$fixtureMsi.sha256")
if ($actualChecksum -cne "$expectedChecksum  FlowBlox_1.2.3.0_x64.msi`n") {
    throw 'The release checksum did not describe the generated installer.'
}

$releaseFixtureState.FailPublish = $true
$rejected = $false
try {
    & (Join-Path $fixtureScripts 'Build-FlowBloxRelease.ps1') -Tag 'v1.2.4'
}
catch {
    $rejected = $true
}
if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $fixture 'artifacts/installer/msi/FlowBlox_1.2.4.0_x64.msi.sha256'))) {
    throw 'A failed application publish was not stopped before producing release assets.'
}
$global:LASTEXITCODE = 0

Write-Host 'Release automation checks passed: tag validation, MSI bounds, version propagation, packaging orchestration, checksums, and failure handling.'

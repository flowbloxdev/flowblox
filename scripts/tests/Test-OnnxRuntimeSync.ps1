<#
.SYNOPSIS
Checks ONNX runtime bootstrap and restore failures in isolated fixtures without downloading packages.
#>
#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixtureRoot = Join-Path $repoRoot ('TestResults/onnx-sync/' + [guid]::NewGuid().ToString('N'))
$originalPackagesPath = $env:NUGET_PACKAGES
$fixtureState = @{
    CachePath = (Join-Path $fixtureRoot 'configured cache with spaces')
    LocalsCalls = 0
    RestoreCalls = 0
    RestoreExitCode = 0
}

function New-SyncFixture([string]$name) {
    $directory = Join-Path $fixtureRoot $name
    $resources = Join-Path $directory 'FlowBloxResources'
    $core = Join-Path $directory 'FlowBlox.Core'
    New-Item -ItemType Directory -Path $resources, $core -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'FlowBloxResources/Sync-OnnxRuntimes.ps1') -Destination $resources
    [System.IO.File]::WriteAllText((Join-Path $core 'FlowBlox.Core.csproj'), @'
<Project><ItemGroup>
<PackageReference Include="Microsoft.ML.OnnxRuntime.Managed" Version="1.23.0" />
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI.Managed" Version="0.14.1" />
</ItemGroup></Project>
'@)
    return $directory
}

function dotnet {
    $arguments = @($args)
    $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'nuget') {
        $fixtureState.LocalsCalls++
        if ($arguments -notcontains '--force-english-output') {
            throw 'NuGet cache lookup must request stable English output.'
        }
        'Welcome to .NET!'
        'First-run SDK output before the NuGet response.'
        "global-packages: $($fixtureState.CachePath)"
        return
    }
    if ($arguments[0] -ne 'restore') {
        throw "Unexpected dotnet operation: $($arguments[0])"
    }

    $fixtureState.RestoreCalls++
    $packagesIndex = [array]::IndexOf($arguments, '--packages')
    if ($packagesIndex -lt 0 -or $arguments[$packagesIndex + 1] -ne $fixtureState.CachePath) {
        throw 'Restore did not use the resolved package cache, including its spaces.'
    }
    if ($fixtureState.RestoreExitCode -ne 0) {
        $global:LASTEXITCODE = $fixtureState.RestoreExitCode
        return
    }

    [xml]$project = Get-Content -LiteralPath $arguments[1] -Raw
    foreach ($reference in $project.Project.ItemGroup.PackageReference) {
        $id = [string]$reference.Include
        $packageDirectory = Join-Path $fixtureState.CachePath ($id.ToLowerInvariant() + '/' + $reference.Version)
        New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
        $dependencyId = switch ($id) {
            'Microsoft.ML.OnnxRuntimeGenAI' { 'Microsoft.ML.OnnxRuntime' }
            'Microsoft.ML.OnnxRuntimeGenAI.DirectML' { 'Microsoft.ML.OnnxRuntime.DirectML' }
            'Microsoft.ML.OnnxRuntimeGenAI.Cuda' { 'Microsoft.ML.OnnxRuntime.Gpu' }
            default { $null }
        }
        if ($dependencyId) {
            [System.IO.File]::WriteAllText((Join-Path $packageDirectory "$id.nuspec"),
                "<package><metadata><dependencies><dependency id='$dependencyId' version='[1.23.0]' /></dependencies></metadata></package>")
        }
        $nativeFileName = if ($id -like 'Microsoft.ML.OnnxRuntimeGenAI*') { 'onnxruntime-genai.dll' } else { 'onnxruntime.dll' }
        foreach ($rid in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')) {
            $nativeDirectory = Join-Path $packageDirectory "runtimes/$rid/native"
            New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $nativeDirectory $nativeFileName), "$id/$rid fixture")
        }
    }
}

try {
    $env:NUGET_PACKAGES = $null
    $fresh = New-SyncFixture 'fresh checkout with spaces'
    if (Test-Path -LiteralPath $fixtureState.CachePath) {
        throw 'Fresh-run fixture unexpectedly has a cache.'
    }
    & (Join-Path $fresh 'FlowBloxResources/Sync-OnnxRuntimes.ps1')
    if ($fixtureState.RestoreCalls -ne 1 -or $fixtureState.LocalsCalls -ne 1) {
        throw 'A nonexistent configured cache was not bootstrapped by one restore.'
    }
    foreach ($runtime in @('onnxruntimes', 'onnxruntimesgenai')) {
        $output = Join-Path $fresh "FlowBloxResources/data/$runtime"
        $manifest = Get-Content -LiteralPath (Join-Path $output 'runtime-manifest.json') -Raw | ConvertFrom-Json
        if ($manifest.onnxRuntimeVersion -ne '1.23.0') {
            throw 'Runtime manifest was not written after successful synchronization.'
        }
        foreach ($provider in @('cpu', 'directml', 'cuda', 'openvino')) {
            if (-not (Test-Path -LiteralPath (Join-Path $output "$provider/win-x64/onnxruntime.dll"))) {
                throw "Missing synchronized ORT library: $runtime/$provider"
            }
            if ($runtime -eq 'onnxruntimesgenai' -and
                -not (Test-Path -LiteralPath (Join-Path $output "$provider/win-x64/onnxruntime-genai.dll"))) {
                throw "Missing synchronized GenAI library: $provider"
            }
        }
    }

    $nativeFile = Join-Path $fresh 'FlowBloxResources/data/onnxruntimes/cpu/win-x64/onnxruntime.dll'
    $timestamp = (Get-Item -LiteralPath $nativeFile).LastWriteTimeUtc
    & (Join-Path $fresh 'FlowBloxResources/Sync-OnnxRuntimes.ps1')
    if ($fixtureState.RestoreCalls -ne 1 -or (Get-Item -LiteralPath $nativeFile).LastWriteTimeUtc -ne $timestamp) {
        throw 'An incremental sync restored packages or recopied an unchanged runtime.'
    }

    $fixtureState.CachePath = Join-Path $fixtureRoot 'explicit environment cache with spaces'
    $env:NUGET_PACKAGES = $fixtureState.CachePath
    $explicit = New-SyncFixture 'explicit cache'
    & (Join-Path $explicit 'FlowBloxResources/Sync-OnnxRuntimes.ps1')
    if ($fixtureState.RestoreCalls -ne 2 -or $fixtureState.LocalsCalls -ne 2) {
        throw 'NUGET_PACKAGES was ignored or queried through dotnet instead of used directly.'
    }

    $fixtureState.CachePath = Join-Path $fixtureRoot 'failed restore cache'
    $env:NUGET_PACKAGES = $fixtureState.CachePath
    $fixtureState.RestoreExitCode = 17
    $failed = New-SyncFixture 'restore failure'
    $rejected = $false
    try {
        & (Join-Path $failed 'FlowBloxResources/Sync-OnnxRuntimes.ps1')
    }
    catch {
        if ($_.Exception.Message -notlike '*dotnet restore returned exit code 17*') { throw }
        $rejected = $true
    }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $failed 'FlowBloxResources/data'))) {
        throw 'A failed restore was not stopped before runtime outputs were prepared.'
    }
    Write-Host 'ONNX sync checks passed: fresh/configured caches, first-run SDK output, explicit restore path, native outputs, incremental copying, and restore failure.'
}
finally {
    $env:NUGET_PACKAGES = $originalPackagesPath
    $global:LASTEXITCODE = 0
}

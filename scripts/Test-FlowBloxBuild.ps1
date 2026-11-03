<#
.SYNOPSIS
Builds the shipping applications and runs isolated unit tests without native runtime packaging.
#>
param(
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'TestResults/ci',
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try {
    $projects = @(
        'FlowBlox.Avalonia/FlowBlox.Avalonia.csproj',
        'FlowBlox.CLI/FlowBlox.CLI.csproj',
        'FlowBlox.WorkerService/FlowBlox.WorkerService.csproj',
        'FlowBloxTest/FlowBloxTest.csproj'
    )
    foreach ($project in $projects) {
        if (-not $NoRestore) {
            Invoke-DotNet -Arguments @('restore', $project)
        }
        Invoke-DotNet -Arguments @(
            'build', $project, '--no-restore', '-c', $Configuration,
            '-p:SkipFlowBloxAppDir=true', '-p:ContinuousIntegrationBuild=true'
        )
    }

    Invoke-DotNet -Arguments @(
        'test', 'FlowBloxTest/FlowBloxTest.csproj', '--no-build', '--no-restore',
        '-c', $Configuration, '--filter', 'TestCategory=UnitTest',
        '--logger', 'trx;LogFileName=unit-tests.trx', '--results-directory', $ResultsDirectory
    )

    # A misspelled category must not silently turn CI green with zero executed tests.
    [xml]$testResults = Get-Content -LiteralPath (Join-Path $ResultsDirectory 'unit-tests.trx') -Raw
    $counters = $testResults.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters -or [int]$counters.executed -eq 0) {
        throw 'The unit-test run did not execute any tests.'
    }
}
finally {
    Pop-Location
}

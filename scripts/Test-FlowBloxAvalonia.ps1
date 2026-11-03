param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ProjectFile
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$project = Join-Path $repoRoot 'FlowBlox.Avalonia.IntegrationTests\FlowBlox.Avalonia.IntegrationTests.csproj'
$resultDirectory = Join-Path $repoRoot 'FlowBlox.Avalonia.IntegrationTests\TestResults'
if (!$ProjectFile) { $ProjectFile = Join-Path $repoRoot 'FlowBlox.Build\data\example_project\Recursion.fbprj' }
if (!(Test-Path -LiteralPath $ProjectFile)) { throw "Project not found: $ProjectFile" }
& dotnet build $project -c $Configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Avalonia integration runner build failed.' }
$projectPath = (Resolve-Path -LiteralPath $ProjectFile).Path
$executable = Join-Path $repoRoot "FlowBlox.Avalonia.IntegrationTests\bin\$Configuration\net10.0-windows10.0.19041.0\FlowBlox.Avalonia.IntegrationTests.exe"
$runnerArguments = '"{0}" "{1}"' -f $resultDirectory, $projectPath
$process = Start-Process -FilePath $executable -ArgumentList $runnerArguments -WindowStyle Hidden -PassThru
try {
    if (!$process.WaitForExit(60000)) {
        $process.Kill()
        throw 'Avalonia integration test exceeded 60 seconds.'
    }
    $reportPath = Join-Path $resultDirectory 'smoke-result.json'
    if (!(Test-Path -LiteralPath $reportPath)) { throw "Integration runner exited with code $($process.ExitCode) without producing a report." }
    $report = Get-Content $reportPath -Raw | ConvertFrom-Json
    if (!$report.Passed) { throw $report.Error }
    if ($process.ExitCode -ne 0) { throw "Integration runner exited with code $($process.ExitCode)." }
    Write-Output "Passed $($report.Checks.Count) Avalonia/WPF integration checks. Report: $resultDirectory\smoke-result.json"
}
finally {
    $process.Dispose()
}

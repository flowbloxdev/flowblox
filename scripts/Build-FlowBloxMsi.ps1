param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$InstallerProjectPath = "FlowBlox.Installer\FlowBlox.Installer.wixproj",
    [string]$FlowBloxProjectPath = "FlowBlox.Avalonia\FlowBlox.Avalonia.csproj",
    [string]$PublishDir = "artifacts\installer\publish\win-x64",
    [string]$MsiOutputDir = "artifacts\installer\msi",
    [switch]$NoRestore,
    [switch]$NoPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path

$flowBloxProject = Join-Path $repoRoot $FlowBloxProjectPath
$installerProject = Join-Path $repoRoot $InstallerProjectPath
$publishDirResolved = Join-Path $repoRoot $PublishDir
$publishStagingRoot = Join-Path $repoRoot "artifacts\installer\staging\$RuntimeIdentifier"
$msiOutputDirResolved = Join-Path $repoRoot $MsiOutputDir

$applicationProjects = @(
    @{ Name = "FlowBlox.Avalonia"; Path = $flowBloxProject; Framework = $null },
    @{ Name = "FlowBlox.CLI"; Path = (Join-Path $repoRoot "FlowBlox.CLI\FlowBlox.CLI.csproj"); Framework = $null },
    @{ Name = "FlowBlox.RunnerHost"; Path = (Join-Path $repoRoot "FlowBlox.RunnerHost\FlowBlox.RunnerHost.csproj"); Framework = $null },
    @{ Name = "FlowBlox.WorkerService"; Path = (Join-Path $repoRoot "FlowBlox.WorkerService\FlowBlox.WorkerService.csproj"); Framework = "net10.0-windows10.0.19041.0" }
)

function Assert-SafeOutputDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $rootPath = [System.IO.Path]::GetPathRoot($fullPath).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($fullPath) -or $fullPath -eq $rootPath -or $fullPath -eq $repoRoot.TrimEnd('\', '/')) {
        throw "Refusing to replace unsafe output directory '$Path'."
    }
}

function Merge-PublishOutput {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][string]$ProjectName
    )

    foreach ($sourceFile in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File) {
        $relativePath = $sourceFile.FullName.Substring($SourceDirectory.Length).TrimStart('\', '/')
        $targetFile = Join-Path $TargetDirectory $relativePath
        $targetParent = Split-Path -Parent $targetFile
        if (-not (Test-Path -LiteralPath $targetParent)) {
            New-Item -Path $targetParent -ItemType Directory -Force | Out-Null
        }

        if (Test-Path -LiteralPath $targetFile) {
            $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
            $targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash
            if ($sourceHash -ne $targetHash) {
                throw "Publish conflict for '$relativePath': project '$ProjectName' produced content that differs from another application project."
            }

            continue
        }

        Copy-Item -LiteralPath $sourceFile.FullName -Destination $targetFile
    }
}

foreach ($applicationProject in $applicationProjects) {
    if (-not (Test-Path $applicationProject.Path)) {
        throw "Application project not found: $($applicationProject.Path)"
    }
}

if (-not (Test-Path $installerProject)) {
    throw "Installer project not found: $installerProject"
}

[xml]$flowBloxXml = Get-Content $flowBloxProject
$assemblyVersionNode = $flowBloxXml.SelectSingleNode("/Project/PropertyGroup/AssemblyVersion")
$versionNode = $flowBloxXml.SelectSingleNode("/Project/PropertyGroup/Version")

$productVersion = if ($assemblyVersionNode -and -not [string]::IsNullOrWhiteSpace($assemblyVersionNode.InnerText)) {
    $assemblyVersionNode.InnerText
}
elseif ($versionNode -and -not [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    "$($versionNode.InnerText).0"
}
else {
    "1.0.0.0"
}

if (-not $NoPublish) {
    Assert-SafeOutputDirectory -Path $publishStagingRoot
    Assert-SafeOutputDirectory -Path $publishDirResolved
    if (Test-Path -LiteralPath $publishStagingRoot) {
        Remove-Item -LiteralPath $publishStagingRoot -Recurse -Force
    }

    $mergedPublishDir = Join-Path $publishStagingRoot "merged"
    New-Item -Path $mergedPublishDir -ItemType Directory -Force | Out-Null

    foreach ($applicationProject in $applicationProjects) {
        $projectPublishDir = Join-Path $publishStagingRoot $applicationProject.Name
        Write-Host "Publishing $($applicationProject.Name) for the shared MSI payload..."
        $publishArgs = @(
            "publish", $applicationProject.Path,
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "--self-contained", "false",
            "-o", $projectPublishDir
        )

        if ($applicationProject.Framework) {
            $publishArgs += @("-f", $applicationProject.Framework)
        }
        if ($applicationProject.Name -eq 'FlowBlox.Avalonia') {
            # RunnerHost is published separately with the selected RID below.
            $publishArgs += "-p:PublishRunnerHost=false"
        }
        if ($NoRestore) {
            $publishArgs += "--no-restore"
        }

        Write-Host "dotnet $($publishArgs -join ' ')"
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for '$($applicationProject.Name)' with exit code $LASTEXITCODE."
        }

        Merge-PublishOutput -SourceDirectory $projectPublishDir -TargetDirectory $mergedPublishDir -ProjectName $applicationProject.Name
    }

    if (Test-Path -LiteralPath $publishDirResolved) {
        Remove-Item -LiteralPath $publishDirResolved -Recurse -Force
    }
    New-Item -Path $publishDirResolved -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $mergedPublishDir '*') -Destination $publishDirResolved -Recurse -Force
}

if (-not (Test-Path $publishDirResolved)) {
    throw "Publish directory not found: $publishDirResolved"
}

foreach ($executable in @('FlowBlox.exe', 'FlowBlox.CLI.exe', 'FlowBlox.RunnerHost.exe', 'FlowBlox.WorkerService.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirResolved $executable) -PathType Leaf)) {
        throw "Required application executable missing from MSI payload: $executable. Rebuild without -NoPublish to refresh the payload."
    }
}

$licenseFile = Join-Path $repoRoot "LICENSE.txt"
if (Test-Path $licenseFile) {
    Copy-Item -Path $licenseFile -Destination (Join-Path $publishDirResolved "LICENSE.txt") -Force
}
else {
    Write-Warning "LICENSE.txt not found at repository root. MSI payload will not include the project license file."
}

if (-not (Test-Path $msiOutputDirResolved)) {
    New-Item -Path $msiOutputDirResolved -ItemType Directory | Out-Null
}

$outputName = "FlowBlox_${productVersion}_x64"

Write-Host "Building MSI via WiX..."
$buildArgs = @(
    "build", $installerProject,
    "-c", $Configuration,
    "/p:ProductVersion=$productVersion",
    "/p:SourceDir=$publishDirResolved",
    "/p:OutputPath=$msiOutputDirResolved\\",
    "/p:OutputName=$outputName"
)

if ($NoRestore) {
    $buildArgs += "--no-restore"
}

Write-Host "dotnet $($buildArgs -join ' ')"
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build (WiX) failed with exit code $LASTEXITCODE."
}

$expectedMsi = Join-Path $msiOutputDirResolved "$outputName.msi"
Write-Host ""
Write-Host "MSI build completed."
Write-Host "Version: $productVersion"
Write-Host "Output:  $expectedMsi"


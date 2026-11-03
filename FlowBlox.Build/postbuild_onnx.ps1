<#
.SYNOPSIS
  FlowBlox PostBuild ONNX copier (PowerShell, DRY, PS 5.1 compatible)

.PARAMETER TargetDir
  The target output directory (e.g. $(TargetDir)).

.PARAMETER Rid
  Runtime identifier override (win-x64, win-x86, win-arm64, linux-x64). Default: win-x64.

.PARAMETER DataSubfolder
  Data folder under FlowBloxResources\data (onnxruntimes or onnxruntimesgenai). Default: onnxruntimes.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $TargetDir,

  [Parameter(Mandatory = $false)]
  [string] $Rid = "win-x64",

  [Parameter(Mandatory = $false)]
  [ValidateSet("onnxruntimes", "onnxruntimesgenai")]
  [string] $DataSubfolder = "onnxruntimes"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# -----------------------------
# Helper functions (DRY)
# -----------------------------

function Write-Info([string] $Message) { Write-Host $Message }
function Write-Warn([string] $Message) { Write-Warning $Message }

function Ensure-Directory([string] $Path) {
  if (-not (Test-Path -LiteralPath $Path)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
  }
}

function Normalize-Dir([string] $Path) {
  # Normalize to a full path and ensure it ends with a directory separator.
  $full = [System.IO.Path]::GetFullPath($Path)
  $sep  = [System.IO.Path]::DirectorySeparatorChar
  if (-not $full.EndsWith($sep)) {
    $full += $sep
  }
  return $full
}

function Test-DirectoryHasContent([string] $Path) {
  if (-not (Test-Path -LiteralPath $Path)) { return $false }
  $item = Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1
  return ($null -ne $item)
}

function Test-FileUpToDate {
  param(
    [Parameter(Mandatory=$true)][string] $SourceFile,
    [Parameter(Mandatory=$true)][string] $DestFile
  )

  if (-not (Test-Path -LiteralPath $DestFile)) {
    return $false
  }

  $srcInfo = Get-Item -LiteralPath $SourceFile -ErrorAction Stop
  $dstInfo = Get-Item -LiteralPath $DestFile -ErrorAction Stop

  if ($srcInfo.Length -ne $dstInfo.Length) {
    return $false
  }

  return ($srcInfo.LastWriteTimeUtc -eq $dstInfo.LastWriteTimeUtc)
}

function Copy-DirectoryContentIncremental {
  param(
    [Parameter(Mandatory=$true)][string] $SourceDir,
    [Parameter(Mandatory=$true)][string] $DestDir,
    [Parameter(Mandatory=$true)][string] $ProviderName,
    [Parameter(Mandatory=$true)][string] $Rid
  )

  if (-not (Test-Path -LiteralPath $SourceDir)) {
    Write-Info ("[FlowBlox] INFO: Provider '{0}' source folder missing (skipping): {1}" -f $ProviderName, $SourceDir)
    return
  }

  if (-not (Test-DirectoryHasContent $SourceDir)) {
    Write-Warn ("[FlowBlox] WARN: Provider '{0}' source folder exists but is empty: {1}" -f $ProviderName, $SourceDir)
    return
  }

  $filesToCopy = @()
  $allSourceFiles = Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force
  foreach ($file in $allSourceFiles) {
    $srcFile = $file.FullName
    $relPath = $srcFile.Substring($SourceDir.Length).TrimStart('\','/')
    $dstFile = Join-Path $DestDir $relPath

    if (-not (Test-FileUpToDate -SourceFile $srcFile -DestFile $dstFile)) {
      $filesToCopy += [PSCustomObject]@{
        Source = $srcFile
        Destination = $dstFile
      }
    }
  }

  if ($filesToCopy.Count -eq 0) {
    Write-Info ("[FlowBlox] Provider '{0}' RID={1} is up-to-date (no copy)." -f $ProviderName, $Rid)
    return
  }

  Ensure-Directory $DestDir

  Write-Info ""
  Write-Info ("[FlowBlox] Copy provider '{0}' RID={1}" -f $ProviderName, $Rid)
  Write-Info ("          from {0}" -f $SourceDir)
  Write-Info ("            to {0}" -f $DestDir)
  Write-Info ("          files {0}" -f $filesToCopy.Count)

  foreach ($entry in $filesToCopy) {
    $dstParent = Split-Path $entry.Destination -Parent
    Ensure-Directory $dstParent
    Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force

    $srcInfo = Get-Item -LiteralPath $entry.Source -ErrorAction Stop
    (Get-Item -LiteralPath $entry.Destination -ErrorAction Stop).LastWriteTimeUtc = $srcInfo.LastWriteTimeUtc
  }
}

# -----------------------------
# Compute paths
# -----------------------------

$targetDirNorm = Normalize-Dir $TargetDir

# Script directory and repo root: one level above FlowBlox.Build\
$scriptDir = Normalize-Dir $PSScriptRoot
$rootDir   = Normalize-Dir (Join-Path $scriptDir "..")

$srcBase = Join-Path (Join-Path $rootDir "FlowBloxResources\data") $DataSubfolder
$dstBase = Join-Path (Join-Path $targetDirNorm "data") $DataSubfolder

Write-Info ""
Write-Info "[FlowBlox] PostBuild copy"
Write-Info ("  DataFolder: {0}" -f $DataSubfolder)
Write-Info ("  TargetDir : {0}" -f $targetDirNorm)
Write-Info ("  RootDir   : {0}" -f $rootDir)
Write-Info ("  Source    : {0}\" -f $srcBase)
Write-Info ("  Dest      : {0}\" -f $dstBase)
Write-Info ("  RID       : {0}" -f $Rid)
Write-Info ""

if (-not (Test-Path -LiteralPath $srcBase)) {
  Write-Warn ("[FlowBlox] WARN: Source folder not found: {0}\" -f $srcBase)
  exit 0
}

if (-not (Test-Path -LiteralPath $targetDirNorm)) {
  Write-Warn ("[FlowBlox] WARN: TargetDir not found: {0}" -f $targetDirNorm)
  exit 0
}

Ensure-Directory $dstBase

# -----------------------------
# Copy providers
# -----------------------------

# CPU
Copy-DirectoryContentIncremental `
  -SourceDir (Join-Path $srcBase ("cpu\{0}" -f $Rid)) `
  -DestDir   (Join-Path $dstBase ("cpu\{0}" -f $Rid)) `
  -ProviderName "cpu" `
  -Rid $Rid

# CUDA
Copy-DirectoryContentIncremental `
  -SourceDir (Join-Path $srcBase ("cuda\{0}" -f $Rid)) `
  -DestDir   (Join-Path $dstBase ("cuda\{0}" -f $Rid)) `
  -ProviderName "cuda" `
  -Rid $Rid

# DirectML
Copy-DirectoryContentIncremental `
  -SourceDir (Join-Path $srcBase ("directml\{0}" -f $Rid)) `
  -DestDir   (Join-Path $dstBase ("directml\{0}" -f $Rid)) `
  -ProviderName "directml" `
  -Rid $Rid

# OpenVINO
Copy-DirectoryContentIncremental `
  -SourceDir (Join-Path $srcBase ("openvino\{0}" -f $Rid)) `
  -DestDir   (Join-Path $dstBase ("openvino\{0}" -f $Rid)) `
  -ProviderName "openvino" `
  -Rid $Rid

Write-Info ""
Write-Info ("[FlowBlox] PostBuild copy done ({0})." -f $DataSubfolder)
exit 0

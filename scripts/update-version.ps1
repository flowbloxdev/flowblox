<# 
.SYNOPSIS
  Sets version + metadata centrally for FlowBlox projects.
.PARAMETER Version
  SemVer with 3 parts, e.g. "1.0.0".
.PARAMETER Root
  Optional: Root path; default is the repository root (parent of this scripts directory).
#>

param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
  [string]$Version,
  [string]$Root = ""
)

$ErrorActionPreference = 'Stop'

# Fallback for Root (robust for different invocation styles)
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-Location }
$rootPath = [string]$Root
if ((Split-Path -Leaf $rootPath) -ieq "scripts") {
  $Root = Split-Path -Parent $rootPath
}

# ---- Configuration ----
$Projects = @(
  "FlowBlox.Avalonia\FlowBlox.Avalonia.csproj",
  "FlowBlox.AIAssistant\FlowBlox.AIAssistant.csproj",
  "FlowBlox.CLI\FlowBlox.CLI.csproj",
  "FlowBlox.Core\FlowBlox.Core.csproj",
  "FlowBlox.RunnerHost\FlowBlox.RunnerHost.csproj",
  "FlowBlox.WorkerService\FlowBlox.WorkerService.csproj",
  "FlowBlox.UICore\FlowBlox.UICore.csproj",
  "FlowBlox.SequenceDetection\FlowBlox.SequenceDetection.csproj"
)

$AssemblyVersion = "$Version.0"

# Version metadata for all csproj. Shared assembly/package metadata lives in Directory.Build.props.
$Meta = @{
  "Version"         = $Version
  "AssemblyVersion" = $AssemblyVersion
  "FileVersion"     = $AssemblyVersion
}

# ---- Helpers ----
function Load-Xml([string]$path) {
  if (-not (Test-Path $path)) { throw ("File not found: {0}" -f $path) }
  [xml]$xml = Get-Content -LiteralPath $path -Raw
  return $xml
}
function Save-Xml([xml]$xml, [string]$path) {
  $settings = New-Object System.Xml.XmlWriterSettings
  $settings.OmitXmlDeclaration = $true
  $settings.Encoding = New-Object System.Text.UTF8Encoding($true)
  $settings.Indent = $true
  $writer = [System.Xml.XmlWriter]::Create($path, $settings)
  $xml.Save($writer)
  $writer.Flush(); $writer.Close()
}
function Get-Or-Create-PropertyGroup([xml]$xml) {
  if (-not $xml) { throw "XML document is null." }

  $project = $xml.DocumentElement
  if (-not $project -or $project.Name -ne 'Project') {
    throw "No <Project> root found."
  }
  
  $pgNodes = $project.SelectNodes("./PropertyGroup")
  $count = if ($pgNodes) { $pgNodes.Count } else { 0 }
  Write-Host ("  Found existing PropertyGroup count: {0}" -f $count) -ForegroundColor Cyan

  if ($count -gt 0) {
    return [System.Xml.XmlElement]$pgNodes.Item(0)
  } else {
    Write-Host "  No PropertyGroup found, creating new one..." -ForegroundColor Magenta
    $pg = $xml.CreateElement("PropertyGroup")
    [void]$project.AppendChild($pg)
    return $pg
  }
}
function Set-Property([xml]$xml, [System.Xml.XmlElement]$pg, [string]$name, [string]$value) {
  $node = $pg.SelectSingleNode($name)
  if (-not $node) {
    Write-Host ("  Creating property <{0}> with value '{1}'" -f $name, $value) -ForegroundColor DarkGreen
    $node = $xml.CreateElement($name)
    [void]$pg.AppendChild($node)
  } else {
    Write-Host ("  Updating property <{0}> from '{1}' to '{2}'" -f $name, $node.InnerText, $value) -ForegroundColor Yellow
  }
  $node.InnerText = $value
}

# ---- Process csproj files ----
$updated = @()
foreach ($rel in $Projects) {
  $path = Join-Path $Root $rel
  Write-Host ("Updating {0} ..." -f $path) -ForegroundColor Cyan

  $xml = Load-Xml $path
  $pg  = Get-Or-Create-PropertyGroup $xml
  foreach ($k in $Meta.Keys) {
    Set-Property $xml $pg $k $Meta[$k]
  }
  Save-Xml $xml $path
  $updated += $path
}

# ---- Result ----
Write-Host "Updated:" -ForegroundColor Green
$updated | ForEach-Object { Write-Host ("  {0}" -f $_) }

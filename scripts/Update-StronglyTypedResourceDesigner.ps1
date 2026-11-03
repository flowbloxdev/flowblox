<#
.SYNOPSIS
  Regenerates strongly typed resource Designer.cs files for .resx files.

.DESCRIPTION
  This mirrors the important part of the Visual Studio custom tool for resource
  files. If a localized file such as *.de.resx is passed, the script regenerates
  the neutral/main *.resx designer instead.

  Only existing strongly typed resource designers are processed. WinForms/WPF
  control designers are intentionally skipped. Repository-wide generation excludes
  the obsolete FlowBlox project; an explicit ResxPath can still target it.

.PARAMETER ResxPath
  Optional path to a specific .resx file. Localized paths are mapped to the
  corresponding neutral .resx file.

.PARAMETER Root
  Repository root. Defaults to the parent directory of this scripts folder.
#>

param(
    [string]$ResxPath = "",
    [string]$Root = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ((Split-Path -Leaf $Root) -ieq "scripts") {
    $Root = Split-Path -Parent $Root
}

$Root = (Resolve-Path -LiteralPath $Root).Path

Add-Type -AssemblyName System.Design
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName Microsoft.CSharp

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$generatorOptions = New-Object System.CodeDom.Compiler.CodeGeneratorOptions
$generatorOptions.BracingStyle = "C"
$generatorOptions.BlankLinesBetweenMembers = $true
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)

function Resolve-NeutralResxPath {
    param([string]$Path)

    $resolvedPath = if ([System.IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $Root $Path
    }

    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "Resource file not found: $Path"
    }

    $file = Get-Item -LiteralPath $resolvedPath
    $neutralName = $file.Name -replace '\.[a-z]{2}(-[A-Z]{2})?\.resx$', '.resx'
    $neutralPath = Join-Path $file.DirectoryName $neutralName

    if (-not (Test-Path -LiteralPath $neutralPath)) {
        throw "Neutral resource file not found for '$($file.FullName)': $neutralPath"
    }

    return (Resolve-Path -LiteralPath $neutralPath).Path
}

function Get-ResourceDesignerInfo {
    param([string]$Resx)

    $resxFile = Get-Item -LiteralPath $Resx
    $designerPath = Join-Path $resxFile.DirectoryName ($resxFile.BaseName + ".Designer.cs")

    if (-not (Test-Path -LiteralPath $designerPath)) {
        return $null
    }

    $content = Get-Content -LiteralPath $designerPath -Raw
    if ($content -notmatch 'System\.Resources\.ResourceManager') {
        return $null
    }

    if ($content -notmatch 'namespace\s+([^\s\{]+)') {
        throw "Namespace not found in resource designer: $designerPath"
    }

    $namespace = $Matches[1]

    if ($content -notmatch '(public|internal)\s+class\s+(\w+)') {
        throw "Class declaration not found in resource designer: $designerPath"
    }

    return [pscustomobject]@{
        DesignerPath = $designerPath
        Namespace = $namespace
        ClassName = $Matches[2]
        IsPublic = $Matches[1] -eq "public"
    }
}

function New-ResourceDictionary {
    param([string]$Resx)

    $resxFile = Get-Item -LiteralPath $Resx
    $reader = New-Object System.Resources.ResXResourceReader($resxFile.FullName)
    $reader.BasePath = $resxFile.DirectoryName

    $resources = New-Object System.Collections.Hashtable
    try {
        foreach ($entry in $reader) {
            $resources[$entry.Key] = $entry.Value
        }
    }
    finally {
        $reader.Close()
    }

    return $resources
}

function Update-Designer {
    param([string]$Resx)

    $info = Get-ResourceDesignerInfo -Resx $Resx
    if ($null -eq $info) {
        Write-Host "Skipped $Resx (no strongly typed resource designer found)."
        return
    }

    $resources = New-ResourceDictionary -Resx $Resx
    $errors = $null
    $compileUnit = [System.Resources.Tools.StronglyTypedResourceBuilder]::Create(
        $resources,
        $info.ClassName,
        $info.Namespace,
        $provider,
        -not $info.IsPublic,
        [ref]$errors)

    if ($errors -and $errors.Length -gt 0) {
        throw "Resource generation failed for $Resx`: $($errors -join ', ')"
    }

    $writer = New-Object System.IO.StreamWriter($info.DesignerPath, $false, $utf8WithBom)
    try {
        $provider.GenerateCodeFromCompileUnit($compileUnit, $writer, $generatorOptions)
    }
    finally {
        $writer.Dispose()
    }

    Write-Host "Generated $($info.DesignerPath)"
}

if ([string]::IsNullOrWhiteSpace($ResxPath)) {
    $legacyDirectory = (Join-Path $Root 'FlowBlox') + [System.IO.Path]::DirectorySeparatorChar
    $resxFiles = Get-ChildItem -Path $Root -Recurse -Filter *.resx |
        Where-Object {
            $_.FullName -notmatch '\\bin\\|\\obj\\' -and
            -not $_.FullName.StartsWith($legacyDirectory, [System.StringComparison]::OrdinalIgnoreCase) -and
            $_.BaseName -notmatch '\.[a-z]{2}(-[A-Z]{2})?$'
        } |
        ForEach-Object { $_.FullName }
}
else {
    $resxFiles = @(Resolve-NeutralResxPath -Path $ResxPath)
}

$processed = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($resx in $resxFiles) {
    $neutralResx = Resolve-NeutralResxPath -Path $resx
    if ($processed.Add($neutralResx)) {
        Update-Designer -Resx $neutralResx
    }
}

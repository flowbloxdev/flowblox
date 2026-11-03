param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,
    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-StableId {
    param(
        [string]$Prefix,
        [string]$Text
    )

    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text.ToLowerInvariant())
        $hash = $md5.ComputeHash($bytes)
        $hex = ([System.BitConverter]::ToString($hash)).Replace('-', '')
        return "$Prefix$($hex.Substring(0, 24))"
    }
    finally {
        $md5.Dispose()
    }
}

$resolvedSourceDir = (Resolve-Path $SourceDir).Path
if (-not (Test-Path $resolvedSourceDir)) {
    throw "Source directory not found: $SourceDir"
}

if (-not (Test-Path -LiteralPath (Join-Path $resolvedSourceDir 'FlowBlox.exe') -PathType Leaf)) {
    throw "Avalonia application executable not found in '$resolvedSourceDir'. Publish FlowBlox.Avalonia before building the installer."
}

$files = Get-ChildItem -Path $resolvedSourceDir -Recurse -File | Sort-Object FullName
if ($files.Count -eq 0) {
    throw "No files found in source directory '$resolvedSourceDir'."
}

$sourceVar = '$(var.SourceDir)'

$directorySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$null = $directorySet.Add('')

foreach ($file in $files) {
    $relative = $file.FullName.Substring($resolvedSourceDir.Length).TrimStart('\', '/')
    $dir = [System.IO.Path]::GetDirectoryName($relative)

    while (-not [string]::IsNullOrEmpty($dir)) {
        $null = $directorySet.Add($dir)
        $parent = [System.IO.Path]::GetDirectoryName($dir)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $dir) {
            break
        }

        $dir = $parent
    }
}

$dirIds = @{}
$dirIds[''] = 'INSTALLFOLDER'

$dirs = $directorySet | Where-Object { $_ -ne '' } | Sort-Object
foreach ($dir in $dirs) {
    $dirIds[$dir] = New-StableId -Prefix 'DIR_' -Text $dir
}

$childrenByParent = @{}
foreach ($dir in $dirs) {
    $parent = [System.IO.Path]::GetDirectoryName($dir)
    if ([string]::IsNullOrEmpty($parent)) {
        $parent = ''
    }

    if (-not $childrenByParent.ContainsKey($parent)) {
        $childrenByParent[$parent] = New-Object System.Collections.Generic.List[string]
    }

    $childrenByParent[$parent].Add($dir)
}

function Write-DirectoryTree {
    param(
        [string]$Parent,
        [int]$Indent,
        [System.Text.StringBuilder]$Builder,
        [hashtable]$ChildrenByParent,
        [hashtable]$DirIds
    )

    if (-not $ChildrenByParent.ContainsKey($Parent)) {
        return
    }

    $children = $ChildrenByParent[$Parent] | Sort-Object
    foreach ($child in $children) {
        $name = [System.IO.Path]::GetFileName($child)
        $id = $DirIds[$child]
        $pad = (' ' * $Indent)
        [void]$Builder.AppendLine("$pad<Directory Id=`"$id`" Name=`"$([System.Security.SecurityElement]::Escape($name))`">")
        Write-DirectoryTree -Parent $child -Indent ($Indent + 2) -Builder $Builder -ChildrenByParent $ChildrenByParent -DirIds $DirIds
        [void]$Builder.AppendLine("$pad</Directory>")
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
Write-DirectoryTree -Parent '' -Indent 6 -Builder $sb -ChildrenByParent $childrenByParent -DirIds $dirIds
[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="FlowBloxFiles">')

foreach ($file in $files) {
    $relative = $file.FullName.Substring($resolvedSourceDir.Length).TrimStart('\', '/')
    $relativeWin = $relative.Replace('/', '\')
    $relativeEscaped = [System.Security.SecurityElement]::Escape($relativeWin)
    $directory = [System.IO.Path]::GetDirectoryName($relative)
    if ([string]::IsNullOrEmpty($directory)) {
        $directory = ''
    }

    $dirId = $dirIds[$directory]
    $componentId = New-StableId -Prefix 'CMP_' -Text $relative
    $fileId = if ($relative -ieq 'FlowBlox.exe') {
        'FlowBloxExecutable'
    }
    else {
        New-StableId -Prefix 'FIL_' -Text $relative
    }
    $sourcePath = "$sourceVar\\$relativeEscaped"

    [void]$sb.AppendLine("      <Component Id=`"$componentId`" Directory=`"$dirId`" Guid=`"*`">")
    [void]$sb.AppendLine("        <File Id=`"$fileId`" Source=`"$sourcePath`" />")
    [void]$sb.AppendLine('      </Component>')
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$outputDir = Split-Path -Parent $OutputFile
if (-not (Test-Path $outputDir)) {
    New-Item -Path $outputDir -ItemType Directory | Out-Null
}

Set-Content -Path $OutputFile -Value $sb.ToString() -Encoding UTF8
Write-Host "Generated WiX file fragment: $OutputFile"
Write-Host "Source directory: $resolvedSourceDir"
Write-Host "File count: $($files.Count)"


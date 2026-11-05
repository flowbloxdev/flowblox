#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$RuntimeManifestFileName = "runtime-manifest.json"
$RuntimeLayoutVersion = 1

function Write-Info($msg)  { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Write-Warn($msg)  { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Err ($msg)  { Write-Host "[ERR ] $msg" -ForegroundColor Red }

function Ensure-Directory([string]$path) {
    if (-not (Test-Path $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
}

function Reset-Directory([string]$path) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    Ensure-Directory $path
}

function Test-RuntimeManifestMatches(
    [string]$manifestPath,
    [System.Collections.IDictionary]$expected) {
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $false }

    try {
        $actual = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($key in $expected.Keys) {
            $property = $actual.PSObject.Properties[$key]
            if ($null -eq $property -or [string]$property.Value -ne [string]$expected[$key]) {
                return $false
            }
        }
        return $true
    } catch {
        return $false
    }
}

function Prepare-RuntimeOutput(
    [string]$path,
    [System.Collections.IDictionary]$expectedManifest) {
    Ensure-Directory $path
    $manifestPath = Join-Path $path $RuntimeManifestFileName

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        Write-Info "No runtime manifest found; preserving '$path' and using incremental copy."
        return
    }

    if (Test-RuntimeManifestMatches $manifestPath $expectedManifest) {
        Write-Info "Runtime manifest is current; using incremental copy: $path"
        return
    }

    Write-Warn "Runtime version or layout changed; rebuilding: $path"
    Reset-Directory $path
}

function Write-RuntimeManifest(
    [string]$path,
    [System.Collections.IDictionary]$manifest) {
    $manifestPath = Join-Path $path $RuntimeManifestFileName
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Info "Wrote runtime manifest: $manifestPath"
}

function Test-FileUpToDate([string]$sourceFile, [string]$destFile) {
    if (-not (Test-Path -LiteralPath $destFile)) { return $false }

    $src = Get-Item -LiteralPath $sourceFile -ErrorAction Stop
    $dst = Get-Item -LiteralPath $destFile -ErrorAction Stop

    if ($src.Length -ne $dst.Length) { return $false }
    return ($src.LastWriteTimeUtc -eq $dst.LastWriteTimeUtc)
}

function Get-RepositoryRootPaths {
    # Script runs in FlowBloxResources
    $root = Resolve-Path -LiteralPath $PSScriptRoot
    $csproj = Join-Path $root "..\FlowBlox.Core\FlowBlox.Core.csproj"
    $csproj = Resolve-Path -LiteralPath $csproj -ErrorAction SilentlyContinue
    if (-not $csproj) {
        throw "Could not find FlowBlox.Core.csproj at '..\FlowBlox.Core\FlowBlox.Core.csproj' relative to '$root'."
    }
    return @{
        Root      = $root.Path
        Csproj    = $csproj.Path
        DataDir   = (Join-Path $root.Path "data")
        OrtOut    = (Join-Path $root.Path "data\onnxruntimes")
        GenAiOut  = (Join-Path $root.Path "data\onnxruntimesgenai")
    }
}

function Get-PackageVersionFromCsproj([string]$csprojPath, [string]$packageId) {
    [xml]$xml = Get-Content -LiteralPath $csprojPath -Raw

    # Handle common forms:
    # 1) <PackageReference Include="X" Version="1.2.3" />
    # 2) <PackageReference Include="X"><Version>1.2.3</Version></PackageReference>
    # 3) Central package mgmt (Directory.Packages.props) -> not handled (warn)

    $nsMgr = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $nsMgr.AddNamespace("msb", $xml.DocumentElement.NamespaceURI)

    $nodes = $xml.SelectNodes("//msb:PackageReference[@Include='$packageId' or @Update='$packageId']", $nsMgr)
    if (-not $nodes -or $nodes.Count -eq 0) {
        # Try without namespace (some csproj have none)
        $nodes = $xml.SelectNodes("//PackageReference[@Include='$packageId' or @Update='$packageId']")
    }
    if (-not $nodes -or $nodes.Count -eq 0) { return $null }

    foreach ($n in $nodes) {
        if ($n.Version -and $n.Version.Trim().Length -gt 0) { return $n.Version.Trim() }
        $v = $n.SelectSingleNode("msb:Version", $nsMgr)
        if (-not $v) { $v = $n.SelectSingleNode("Version") }
        if ($v -and $v.InnerText.Trim().Length -gt 0) { return $v.InnerText.Trim() }
    }
    return $null
}

function Get-NuGetGlobalPackagesFolder {
    # A fresh checkout/runner may not have a cache directory yet. Resolve its
    # configured location independently of existence; restore will populate it.
    if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        return [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
    }

    $output = & dotnet nuget locals global-packages --list --force-english-output
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine NuGet global packages folder: dotnet exited with code $LASTEXITCODE."
    }

    # First-run SDK messages can precede the actual path. Parse a complete output line.
    $match = [regex]::Match(($output -join "`n"), '(?m)^global-packages:\s*(.+)$')
    if (-not $match.Success) {
        throw "Could not determine NuGet global packages folder from dotnet output."
    }
    return [System.IO.Path]::GetFullPath($match.Groups[1].Value.Trim())
}

function Test-PackageInCache([string]$nugetRoot, [string]$packageId, [string]$version) {
    $pkgPath = Join-Path $nugetRoot (Join-Path $packageId.ToLowerInvariant() $version)
    return (Test-Path $pkgPath)
}

function Get-PackageDependencyVersion(
    [string]$nugetRoot,
    [string]$packageId,
    [string]$version,
    [string]$dependencyId) {
    $pkgPath = Join-Path $nugetRoot (Join-Path $packageId.ToLowerInvariant() $version)
    $nuspec = Get-ChildItem -LiteralPath $pkgPath -Filter "*.nuspec" -File | Select-Object -First 1
    if (-not $nuspec) {
        throw "Could not find nuspec for $packageId $version."
    }

    [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $dependency = $xml.SelectSingleNode(
        "//*[local-name()='dependency' and translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='$($dependencyId.ToLowerInvariant())']")
    if (-not $dependency -or [string]::IsNullOrWhiteSpace($dependency.version)) {
        throw "Could not find dependency '$dependencyId' in $packageId $version."
    }

    return $dependency.version.Trim().Trim('[', ']', '(', ')').Split(',')[0].Trim()
}

function Ensure-PackagesInCache([string]$csprojDir, [hashtable[]]$packages, [string]$nugetRoot) {
    # Create a temp csproj that references all required packages and dotnet restore it.
    # This will populate the NuGet global cache without touching your real project refs.
    $tmpDir = Join-Path $csprojDir ".tmp_restore_runtimes"
    Ensure-Directory $tmpDir

    $tmpProj = Join-Path $tmpDir "RestoreRuntimes.csproj"

    $refs = $packages | ForEach-Object {
        "<PackageReference Include=""$($_.Id)"" Version=""$($_.Version)"" />"
    } | Out-String

    $projXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RestoreNoCache>true</RestoreNoCache>
    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
    <DisableImplicitNuGetFallbackFolder>true</DisableImplicitNuGetFallbackFolder>
  </PropertyGroup>
  <ItemGroup>
$refs
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath $tmpProj -Value $projXml -Encoding UTF8

    Write-Info "Restoring helper project to populate NuGet cache..."
    Write-Info "  $tmpProj"

    # Restore
    & dotnet restore $tmpProj --packages $nugetRoot -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore returned exit code $LASTEXITCODE."
    }

    # Cleanup project file (keep folder for troubleshooting if needed)
    try { Remove-Item -LiteralPath $tmpProj -Force -ErrorAction SilentlyContinue } catch { }
}

function Copy-RuntimeNative([string]$nugetRoot, [string]$packageId, [string]$version, [string]$rid, [string]$destDir) {
    $pkgBase = Join-Path $nugetRoot (Join-Path $packageId.ToLowerInvariant() $version)
    if (-not (Test-Path $pkgBase)) {
        throw "Package not found in cache: $packageId $version"
    }

    $src = Join-Path $pkgBase (Join-Path "runtimes\$rid" "native")
    if (-not (Test-Path $src)) {
        throw "RID/native not found: $packageId $version -> runtimes\$rid\native"
    }

    Ensure-Directory $destDir

    # Incremental file copy: only copy changed/missing files.
    $srcRoot = [System.IO.Path]::GetFullPath($src)
    $destRoot = [System.IO.Path]::GetFullPath($destDir)
    $srcFiles = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -File -Force -ErrorAction Stop)

    if ($srcFiles.Length -eq 0) {
        throw "Source folder is empty: $src"
    }

    $filesToCopy = @()
    foreach ($file in $srcFiles) {
        $srcFile = $file.FullName
        $relPath = $srcFile.Substring($srcRoot.Length).TrimStart('\','/')
        $dstFile = Join-Path $destRoot $relPath

        if (-not (Test-FileUpToDate $srcFile $dstFile)) {
            $filesToCopy += [PSCustomObject]@{
                Source = $srcFile
                Destination = $dstFile
            }
        }
    }

    if ($filesToCopy.Count -eq 0) {
        Write-Info "Up-to-date: $packageId $version [$rid] -> $destDir"
        return $true
    }

    Write-Info "Copy: $packageId $version [$rid] -> $destDir (files: $($filesToCopy.Count))"
    foreach ($entry in $filesToCopy) {
        $parent = Split-Path -Parent $entry.Destination
        Ensure-Directory $parent
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force -ErrorAction Stop

        $srcInfo = Get-Item -LiteralPath $entry.Source -ErrorAction Stop
        (Get-Item -LiteralPath $entry.Destination -ErrorAction Stop).LastWriteTimeUtc = $srcInfo.LastWriteTimeUtc
    }

    return $true
}

function Write-Readme([string]$rootDir) {
    $readmePath = Join-Path $rootDir "README.txt"
    $txt = @"
FlowBlox Native ONNX Runtime Bundling
====================================

This README describes the native runtime binaries stored below this folder in:
  data\onnxruntimes\...
  data\onnxruntimesgenai\...

Sync-OnnxRuntimes.ps1 copies these binaries from the local NuGet Global Packages cache.

Why?
-----
FlowBlox deploys selected ONNX Runtime / ONNX Runtime GenAI native binaries (CPU/CUDA/DirectML/OpenVINO)
independently from NuGet to support dynamic runtime/provider loading.

When to run this script?
------------------------
Run Sync-OnnxRuntimes.ps1 after checkout and whenever you change the managed NuGet versions in:
  ../FlowBlox.Core/FlowBlox.Core.csproj

The script reads the managed versions:
  - Microsoft.ML.OnnxRuntime.Managed
  - Microsoft.ML.OnnxRuntimeGenAI.Managed

Then it:
  1) Resolves NUGET_PACKAGES or the global packages path reported by dotnet.
     The cache does not need to exist yet. Missing runtime/provider packages are
     downloaded via a temporary 'dotnet restore' into that exact cache.
  2) Copies files from:
       <resolved NuGet cache>\<package>\<version>\runtimes\<rid>\native\*
     into:
       data\onnxruntimes\...
       data\onnxruntimesgenai\...

Notes
-----
- The sync fails if a required provider package, RID, or native file set is unavailable.
- The cache is only a build-time package source. Deployed runtimes are loaded from data, not NuGet.
- All GenAI provider packages must depend on the same ONNX Runtime version selected by FlowBlox.Core.
- Each runtime output folder contains a runtime-manifest.json with the synchronized package and layout versions.
- Matching manifests keep the existing folders and enable incremental copying. A version or layout change rebuilds only the affected runtime output folder.

"@
    Set-Content -LiteralPath $readmePath -Value $txt -Encoding UTF8
    Write-Info "Wrote README: $readmePath"
}

function Sync-OrtAndGenAiRuntimes {
    $paths = Get-RepositoryRootPaths
    $csprojPath = $paths.Csproj
    $csprojDir  = Split-Path -Parent $csprojPath

    Write-Info "Using csproj: $csprojPath"

    $ortVer   = Get-PackageVersionFromCsproj $csprojPath "Microsoft.ML.OnnxRuntime.Managed"
	$genaiVer = Get-PackageVersionFromCsproj $csprojPath "Microsoft.ML.OnnxRuntimeGenAI.Managed"

    if (-not $ortVer)   { throw "Could not find PackageReference version for Microsoft.ML.OnnxRuntime.Managed in csproj." }
    if (-not $genaiVer) { throw "Could not find PackageReference version for Microsoft.ML.OnnxRuntimeGenAI.Managed in csproj." }

    Write-Info "Detected versions:"
    Write-Info "  ONNX Runtime     : $ortVer"
    Write-Info "  ONNX Runtime GenAI: $genaiVer"

    $nugetRoot = Get-NuGetGlobalPackagesFolder
    Ensure-Directory $nugetRoot
    Write-Info "NuGet global cache: $nugetRoot"

    # --- Define required packages based on managed versions
    # ORT provider packages (same version as managed ORT)
    $ortPackages = @(
        @{ Id = "Microsoft.ML.OnnxRuntime";             	Version = $ortVer },
        @{ Id = "Microsoft.ML.OnnxRuntime.Gpu.Windows";     Version = $ortVer },
		@{ Id = "Microsoft.ML.OnnxRuntime.Gpu.Linux";     	Version = $ortVer },
        @{ Id = "Microsoft.ML.OnnxRuntime.DirectML";    	Version = $ortVer },
        @{ Id = "Intel.ML.OnnxRuntime.OpenVino";        	Version = $ortVer }
    )

    # GenAI packages (same version as managed GenAI)
    $genaiPackages = @(
        @{ Id = "Microsoft.ML.OnnxRuntimeGenAI";            Version = $genaiVer },
        @{ Id = "Microsoft.ML.OnnxRuntimeGenAI.DirectML";   Version = $genaiVer },
        @{ Id = "Microsoft.ML.OnnxRuntimeGenAI.Cuda";       Version = $genaiVer }
    )

    # --- Ensure all packages exist in cache; if not, restore once.
    $missing = @()
    foreach ($p in ($ortPackages + $genaiPackages)) {
        if (-not (Test-PackageInCache $nugetRoot $p.Id $p.Version)) {
            $missing += $p
        }
    }

    if ($missing.Count -gt 0) {
        Write-Warn "Some packages are missing in NuGet global cache. Running 'dotnet restore' helper..."
        $missing | ForEach-Object { Write-Warn "  missing: $($_.Id) $($_.Version)" }
        Ensure-PackagesInCache $csprojDir ($ortPackages + $genaiPackages) $nugetRoot
    } else {
        Write-Info "All required packages already present in NuGet cache."
    }

    foreach ($p in ($ortPackages + $genaiPackages)) {
        if (-not (Test-PackageInCache $nugetRoot $p.Id $p.Version)) {
            throw "Required package is unavailable after restore: $($p.Id) $($p.Version)"
        }
    }

    $genAiOrtDependencies = @(
        @{ Package = "Microsoft.ML.OnnxRuntimeGenAI";          Dependency = "Microsoft.ML.OnnxRuntime" },
        @{ Package = "Microsoft.ML.OnnxRuntimeGenAI.DirectML"; Dependency = "Microsoft.ML.OnnxRuntime.DirectML" },
        @{ Package = "Microsoft.ML.OnnxRuntimeGenAI.Cuda";     Dependency = "Microsoft.ML.OnnxRuntime.Gpu" }
    )
    foreach ($entry in $genAiOrtDependencies) {
        $requiredOrtVersion = Get-PackageDependencyVersion `
            $nugetRoot $entry.Package $genaiVer $entry.Dependency
        if ($requiredOrtVersion -ne $ortVer) {
            throw "$($entry.Package) $genaiVer requires $($entry.Dependency) $requiredOrtVersion, but FlowBlox.Core selects ONNX Runtime $ortVer. Choose a fully synchronized version set."
        }
    }
    Write-Info "Verified: all GenAI provider packages use ONNX Runtime $ortVer."

    $ortManifest = [ordered]@{
        manifestVersion = 1
        runtime = "onnxruntime"
        layoutVersion = $RuntimeLayoutVersion
        onnxRuntimeVersion = $ortVer
    }
    $genAiManifest = [ordered]@{
        manifestVersion = 1
        runtime = "onnxruntime-genai"
        layoutVersion = $RuntimeLayoutVersion
        onnxRuntimeVersion = $ortVer
        onnxRuntimeGenAiVersion = $genaiVer
    }

    # Keep matching outputs for incremental copies; rebuild only after a version/layout change.
    Ensure-Directory $paths.DataDir
    Prepare-RuntimeOutput $paths.OrtOut $ortManifest
    Prepare-RuntimeOutput $paths.GenAiOut $genAiManifest

    # --- Copy maps (RID -> destination)
    $ridMapCpu = @(
        @{ Rid = "win-x64";    Dest = "cpu\win-x64" },
        @{ Rid = "win-arm64";  Dest = "cpu\win-arm64" },
        @{ Rid = "linux-x64";  Dest = "cpu\linux-x64" },
        @{ Rid = "linux-arm64";Dest = "cpu\linux-arm64" },
        @{ Rid = "osx-x64";    Dest = "cpu\osx-x64" },
        @{ Rid = "osx-arm64";  Dest = "cpu\osx-arm64" }
    )

    # ONNX Runtime GenAI 0.14.1 does not publish an osx-x64 native binary.
    $ridMapGenAiCpu = @(
        @{ Rid = "win-x64";    Dest = "cpu\win-x64" },
        @{ Rid = "win-arm64";  Dest = "cpu\win-arm64" },
        @{ Rid = "linux-x64";  Dest = "cpu\linux-x64" },
        @{ Rid = "linux-arm64";Dest = "cpu\linux-arm64" },
        @{ Rid = "osx-arm64";  Dest = "cpu\osx-arm64" }
    )

    $ridMapWin = @(
        @{ Rid = "win-x64";    Dest = "win-x64" },
        @{ Rid = "win-arm64";  Dest = "win-arm64" }
    )

    $ridMapCuda = @(
        @{ Rid = "win-x64";    Dest = "win-x64" },
        @{ Rid = "linux-x64";  Dest = "linux-x64" }
    )

    # --- Copy ONNX Runtime
    Write-Info "=== Copying ONNX Runtime native runtimes ==="
    $anyCopied = $false

    foreach ($m in $ridMapCpu) {
        $dest = Join-Path $paths.OrtOut $m.Dest
        $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime" $ortVer $m.Rid $dest) -or $anyCopied
    }

    foreach ($m in $ridMapWin) {
        $dest = Join-Path (Join-Path $paths.OrtOut "directml") $m.Dest
        $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime.DirectML" $ortVer $m.Rid $dest) -or $anyCopied
    }

    foreach ($m in $ridMapCuda) {
        $destWin = Join-Path (Join-Path $paths.OrtOut "cuda") "win-x64"
        $destLin = Join-Path (Join-Path $paths.OrtOut "cuda") "linux-x64"
        if ($m.Rid -eq "win-x64")   { $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime.Gpu.Windows" $ortVer $m.Rid $destWin) -or $anyCopied }
        if ($m.Rid -eq "linux-x64") { $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime.Gpu.Linux" $ortVer $m.Rid $destLin) -or $anyCopied }
    }

    # OpenVINO (commonly win-x64 only)
    $destOv = Join-Path (Join-Path $paths.OrtOut "openvino") "win-x64"
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Intel.ML.OnnxRuntime.OpenVino" $ortVer "win-x64" $destOv) -or $anyCopied

    # --- Copy GenAI
    Write-Info "=== Copying ONNX Runtime GenAI native runtimes ==="
    foreach ($m in $ridMapGenAiCpu) {
        $dest = Join-Path $paths.GenAiOut $m.Dest
        $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime" $ortVer $m.Rid $dest) -or $anyCopied
        $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntimeGenAI" $genaiVer $m.Rid $dest) -or $anyCopied
    }

    foreach ($m in $ridMapWin) {
        $dest = Join-Path (Join-Path $paths.GenAiOut "directml") $m.Dest
        $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime.DirectML" $ortVer $m.Rid $dest) -or $anyCopied
        $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntimeGenAI.DirectML" $genaiVer $m.Rid $dest) -or $anyCopied
    }

    $destCudaWin = Join-Path (Join-Path $paths.GenAiOut "cuda") "win-x64"
    $destCudaLin = Join-Path (Join-Path $paths.GenAiOut "cuda") "linux-x64"
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime.Gpu.Windows" $ortVer "win-x64" $destCudaWin) -or $anyCopied
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntimeGenAI.Cuda" $genaiVer "win-x64" $destCudaWin) -or $anyCopied
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntime.Gpu.Linux" $ortVer "linux-x64" $destCudaLin) -or $anyCopied
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntimeGenAI.Cuda" $genaiVer "linux-x64" $destCudaLin) -or $anyCopied

    # GenAI has no separate OpenVINO NuGet package. Combine its provider-neutral
    # native library with the matching OpenVINO build of ONNX Runtime.
    $destGenAiOpenVino = Join-Path (Join-Path $paths.GenAiOut "openvino") "win-x64"
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Intel.ML.OnnxRuntime.OpenVino" $ortVer "win-x64" $destGenAiOpenVino) -or $anyCopied
    $anyCopied = (Copy-RuntimeNative $nugetRoot "Microsoft.ML.OnnxRuntimeGenAI" $genaiVer "win-x64" $destGenAiOpenVino) -or $anyCopied

    if (-not $anyCopied) {
        Write-Warn "No files were copied. Check package versions and availability in NuGet cache."
    }

    Write-RuntimeManifest $paths.OrtOut $ortManifest
    Write-RuntimeManifest $paths.GenAiOut $genAiManifest
    Write-Readme $paths.Root
    Write-Info "Done."
}

# Entry
Sync-OrtAndGenAiRuntimes

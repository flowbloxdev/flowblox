# FlowBlox Windows installer

The WiX installer packages the main Avalonia desktop application together with
CLI, RunnerHost and WorkerService. It no longer builds the obsolete WinForms
application. The Start menu shortcut and `.fbprj` file association launch
`FlowBlox.exe`, built from `FlowBlox.Avalonia`; the product name and upgrade identity remain FlowBlox.

From the repository root:

```powershell
# Set release versions in the active application and library projects:
.\scripts\update-version.ps1 -Version 1.0.0

# Publish all four applications and build the Windows x64 MSI:
.\scripts\Build-FlowBloxMsi.ps1
```

Requires .NET 10 SDK and access to the configured NuGet feeds for WiX and application
packages. Prepare the ONNX runtimes with `FlowBloxResources/Sync-OnnxRuntimes.ps1`
before building. These packages are framework-dependent: the destination machine
needs the .NET 10 Desktop Runtime.

The MSI registers JetBrains Mono Regular system-wide in the Windows Fonts folder
through the native Windows Installer Font table. This font component is permanent:
uninstalling FlowBlox does not remove a font that other applications may use.
The copy under `data/fonts` remains available for builds distributed without MSI.
Application startup skips per-user installation if the font family is already
installed system-wide or for the current user. The Avalonia application does not
restart after its startup font check.

The build publishes each application into a separate staging directory, merges
identical shared files and rejects conflicting files. Data and ONNX runtime files
come from the shared `FlowBlox.Build` targets. Output locations:

- Combined payload: `artifacts/installer/publish/win-x64`
- MSI: `artifacts/installer/msi/FlowBlox_<version>_x64.msi`

`-NoRestore` uses already restored packages. `-NoPublish` reuses the combined payload
and verifies that all four application executables exist. An old WinForms payload
must be republished before packaging.

`Publish-FlowBloxMsiRelease.ps1` is a separate release step that copies the MSI into
the configured web directory and writes the update manifest. Building an MSI does
not publish a release or install the application.

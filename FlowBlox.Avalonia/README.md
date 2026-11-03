# FlowBlox.Avalonia — Main Desktop Application

**FlowBlox.Avalonia is the main FlowBlox desktop application. It replaces the former
`FlowBlox` WinForms application.** Develop new desktop application features and fixes
here. The [obsolete WinForms project](../FlowBlox/README.md) is retained only as a
knowledge base and a reference for comparison with the legacy implementation, and
is scheduled for removal.

The application uses .NET 10, Avalonia 12.1.4 and Dock 12.1.0.12 (open source/MIT).
For general project information, see the [main repository README](../README.md).

## Running the application

The project builds `FlowBlox.exe` (without an Avalonia suffix). The project name and source directory remain `FlowBlox.Avalonia`.

Run these commands from the repository root:

```powershell
dotnet run --project FlowBlox.Avalonia
# Optionally open a project at startup:
dotnet run --project FlowBlox.Avalonia -- --project C:\Projects\MyProject.fbprj
```

## UI architecture

The application shell, menus, docking, splash animation and loading indicator use
Avalonia. The eight embedded views and existing dialogs currently continue to use
`FlowBlox.UICore`/WPF. `Interop/WpfPanel.cs` hosts them through `HwndSource` and
`NativeControlHost`. The application therefore currently requires Windows.

The menu structure, labels, colored icons and background image preserve the legacy
application's appearance. Project operations, Project-Space, examples, recent
projects, notifications and updates, directory shortcuts, automation and runtime
events have been carried over. Command availability is bound through
`MenuEntry`/`ICommand` and updated centrally by `UpdateUI` using project, changelist
and runtime state.

`Docking/WorkspaceFactory.cs` replaces the WeifenLuo docking factories. Dock
serializes panel sizes, positions, selected tabs, hidden and pinned panels, and
floating layouts. The Avalonia layout is stored separately under
`%LOCALAPPDATA%\FlowBlox\Avalonia`; legacy WinForms layouts are not imported.
Use the View menu to reopen hidden panels or reset the layout. Content and
view models are detached from layout data during layout changes. Drag previews
display an outline because the same native WPF surface cannot belong to two visual
trees simultaneously.

Workspace persistence is event-driven: changes to the serialized Dock models and
collections, or to the main window's size and maximized state, schedule one save
after a 400 ms quiet period. There is no periodic polling. Pending changes are
saved immediately when the application closes or replaces its workspace. The files
`layout.json` and `window.json` are written atomically, and unchanged data is skipped.

The project panel supports docking on every side, docking in the center, and
floating in its own window. The central document host remains available even when
empty, so the project panel can always be dragged back into it. Older saved layouts
are updated to retain this center target and remove the former project docking
restrictions. Drop indicators use floating adorners to remain visible above native
WPF surfaces.

## Shared code and build inputs

Application services, supporting code, localized resources, icons and logging
configuration are owned by this project. No source files or resources are linked
from the legacy `FlowBlox` directory. New supporting code uses the
`FlowBlox.AvaloniaDesktop` namespace. This project contains no WinForms code or
WinForms initialization and does not reference `System.Windows.Forms` or
`WindowsFormsIntegration` in its application assembly.

Open/save dialogs use Avalonia's `StorageProvider` through `IFilePickerService`.
Menu shortcuts use `KeyGesture`; native messages from the remaining hosted WPF
surfaces are translated into Avalonia key types at the Windows interop boundary.

The FlowBlox message box is implemented in `Dialogs/MessageBoxWindow.axaml`, with
the existing dark appearance and localized OK/Yes/No buttons. `IOwnerService`
provides AppWindow as the dialog owner. Message boxes are centered on that owner
and remain above hosted WPF dialogs; WPF input and native modality are preserved
while a message box is open. Asynchronous callers use `IMessageBoxService`.
Existing synchronous UICore contracts are adapted through a modal dispatcher
frame, which keeps rendering and input running instead of blocking on a Task.
The remaining WPF/native helpers are isolated under `Interop`.

Application data and build scripts are maintained independently in
[`FlowBlox.Build`](../FlowBlox.Build/README.md). Avalonia, CLI and the legacy
WinForms project import `FlowBlox.Build/FlowBlox.Build.targets`, which incrementally
copies `data` into each build and publish directory and runs the shared ONNX copy
scripts. RunnerHost application files are also copied into the Avalonia output.

The Windows MSI packages this application together with CLI, RunnerHost and
WorkerService. Build it with `scripts/Build-FlowBloxMsi.ps1`; see the
[installer README](../FlowBlox.Installer/README.md) for prerequisites and output paths.

## Verification

The integration checks live in the separate
[`FlowBlox.Avalonia.IntegrationTests`](../FlowBlox.Avalonia.IntegrationTests/README.md)
executable project. The application does not contain the check classes or depend
on the runner. Run it from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-FlowBloxAvalonia.ps1
```

The test loads an isolated copy of the Recursion example, opens all eight embedded
views, checks native handles and WPF layout, renders reference images, and verifies
command availability, focus transfers, icon resource loading, and serialization and
restoration of hidden panels. Layout files and the report are written to
`FlowBlox.Avalonia.IntegrationTests/TestResults`. A second run also checks startup with the previously
saved layout.

Manual verification should cover keyboard input and focus, drag and drop between
the component library and project, floating windows, different monitor DPI
settings, Project-Space and authentication, dialogs, AI state restoration, and
complete runtime executions. Native WPF surfaces remain subject to Windows airspace
limitations; loading overlays hide those surfaces while displayed.

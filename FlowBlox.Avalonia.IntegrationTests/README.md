# FlowBlox.Avalonia.IntegrationTests

Separate executable runner for the Avalonia/WPF desktop integration checks.
All assertions, UI input simulation, reference-image rendering and report generation
live in this project. The application does not reference or ship this runner.

This is deliberately not an MSTest/xUnit test project. It runs the actual Windows
desktop application on an STA thread and executes the checks inside its UI
lifecycle. Checks are grouped in `InteropSmokeTest.cs`, `HostedFocusSmokeTest.cs`
and `WorkspaceBehaviorSmokeTest.cs`; they do not appear in the Test Explorer or
run through `dotnet test`.

## Running

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-FlowBloxAvalonia.ps1
```

The script builds this project, starts its executable, enforces a 60-second timeout,
and verifies the process exit code and JSON report. An interactive Windows desktop
is required. Use `-Configuration Release` or `-ProjectFile <path>` to override the
defaults.

For direct execution after building:

```powershell
dotnet run --project FlowBlox.Avalonia.IntegrationTests -- "FlowBlox.Avalonia.IntegrationTests\TestResults" "FlowBlox.Build\data\example_project\Recursion.fbprj"
```

Results, layout files and images are written to
`FlowBlox.Avalonia.IntegrationTests/TestResults` by default. The runner uses an
isolated workspace and a copy of the supplied project, skips interactive startup
and online services, and returns a nonzero exit code if a check or startup fails.
A second run reuses the isolated saved layout to exercise restoration at startup.

For focused Quick Start checks, append `--quick-starts-only` to the direct runner command.
This opens every registered Quick Start from Help without a configured target, navigates both AI Prompt pages,
and writes `ai-prompt-quick-start-1.png` through `ai-prompt-quick-start-2.png` into the result directory.

For focused property editor checks, append `--field-selection-only` to the direct
runner command. This runs the real desktop lifecycle and checks explicitly enabled field
buttons, scalar selection/unlink, password and combobox editors, grid cells and Project Space project cards
without running unrelated authentication and workspace checks.

`ProjectSpaceCardsChecks.cs` uses prepared projects without remote API calls to verify card selection, version details,
mouse-wheel scrolling and opening with Enter. It writes `project-space-cards.png` alongside the desktop report.

`ConnectionPreviewChecks.cs` checks toolbar and temporary Ctrl+Shift connection gestures during a real rejection message box.
The gesture must release mouse capture and hide its preview before the message appears, including subsequent mouse movement.

The reference to `FlowBlox.Avalonia` is one-way. `InternalsVisibleTo` permits the
runner to inspect internal application state without introducing public test APIs.
The shared desktop startup path accepts hosted-session settings; the normal
application keeps its interactive defaults and standard per-user workspace.

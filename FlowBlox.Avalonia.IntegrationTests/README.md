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

To open a local project, run the Project Panel automatic layout command, save it through the application and close:

```powershell
.\FlowBlox.Avalonia.IntegrationTests.exe <results-directory> <project.fbprj> --layout-save
```

The runner uses an editable working copy, checks the original for concurrent changes and copies the saved project back.
It does not upload the project. The result is written to `layout-save-result.json`.

For notice-only maintenance, edit `Notice` first and use `--save-only`. It skips automatic layout and saves an application
working copy, verifying the saved notice and that saving preserves the initialized graph. The source definition stays intact
to avoid carrying unrelated normalization changes from opening older projects into a notice-only upload.
The report includes `AutoLayout: false`; this mode does not execute or upload the project.

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
The Quick Start image checks also cover wheel zoom, the 400% limit, slim scrollbars,
left/middle-button capture and release, fitting with Home and resetting zoom on page changes.
They write `quick-start-image-fitted.png` and `quick-start-image-zoomed.png`.
The same mode opens Local AI setup from a real property transaction in English and German,
checks ownership and overwrite defaults, and writes `local-ai-setup-in-property-window-en-US.png`
and `local-ai-setup-in-property-window-de-DE.png`.

For focused property editor checks, append `--field-selection-only` to the direct
runner command. This runs the real desktop lifecycle and checks explicitly enabled field
buttons, scalar selection/unlink, password and combobox editors, grid cells and Project Space project cards
without running unrelated authentication and workspace checks.
Use `--list-view-edit-only` for the focused Split separator row regression. It edits and saves
the generated separator dialog, verifies visible cells and selection, checks cancellation and
clearing Special, and confirms changes remain in the parent property transaction.
It writes `split-separator-list-after-edit.png`.

`ProjectSpaceCardsChecks.cs` uses prepared projects without remote API calls to verify card selection, version details,
mouse-wheel scrolling and opening with Enter. It writes `project-space-cards.png` alongside the desktop report.

`ConnectionPreviewChecks.cs` checks toolbar and temporary Ctrl+Shift connection gestures during a real rejection message box.
The gesture must release mouse capture and hide its preview before the message appears, including subsequent mouse movement.

The reference to `FlowBlox.Avalonia` is one-way. `InternalsVisibleTo` permits the
runner to inspect internal application state without introducing public test APIs.
The shared desktop startup path accepts hosted-session settings; the normal
application keeps its interactive defaults and standard per-user workspace.

## Component assistant

Use `--component-assistant-only` for a synthetic desktop check covering expansion/collapse,
working-copy updates, history navigation, fresh chats and cancelling the editor.
It also writes `assistant-opening-timings.json` with property-window construction,
first-open synchronous work, time until dispatcher idle and reopening time. Fixed test
delays are excluded. Initialization phases are recorded in the application log.
For history-size profiling, place synthetic history files in the result directory's
`assistant-history` subdirectory before starting the test; real chat files are not needed.

`--component-assistant-live` additionally runs the configured assistant, checks its regex against
an independent extraction from a real page, commits the editor, and saves the sample flow via
Project Panel layout/save. Supply `FLOWBLOX_COMPONENT_ASSISTANT_URL`,
`FLOWBLOX_COMPONENT_ASSISTANT_PROMPT` and `FLOWBLOX_COMPONENT_ASSISTANT_EXPECTED_PATTERN`.
The expected pattern's first capture group supplies the expected values. This opt-in check
uses the current provider and temporarily isolates chat histories in the result directory.
It accepts one-time research-skill approvals only when their input contains the supplied URL.
It writes `regex-result.json`, screenshots, a transcript and `smoke-result.json`.

## Full assistant project live check

`--assistant-project-live` creates an empty project in the desktop app, submits a task through the project assistant,
opens Session Usage, and saves the resulting project through Project Panel automatic layout/save.
Set `FLOWBLOX_ASSISTANT_URL`, `FLOWBLOX_ASSISTANT_PROMPT`, `FLOWBLOX_ASSISTANT_EXPECTED_PATTERN`
(first capture group contains expected values), and `FLOWBLOX_ASSISTANT_OUTPUT` (a new XLSX file).
The runner compares workbook cells with an independent extraction from the page. It isolates histories,
uses the configured provider, and accepts research confirmations only when their inputs contain the supplied URL.
It writes usage details, screenshots, the transcript, extraction results and `smoke-result.json`.

# Project Space command line

The Avalonia `FlowBlox.exe` supports Project Space commands without opening the desktop UI.
Normal `--project` and `--projectSpaceGuid` arguments continue to open the application.
Only the latest Project Space content is used; version selection is not part of these commands.

## Commands

```powershell
# Help; no UI and no login.
.\FlowBlox.exe --ps-help | Out-String

# List public projects anonymously, optionally searching their names.
.\FlowBlox.exe --list-ps-projects --query "IMAP" | Out-String

# List your private projects using remembered credentials.
.\FlowBlox.exe --list-ps-projects --private | ConvertFrom-Json

# Include your own public projects as well.
.\FlowBlox.exe --list-ps-projects --mine | ConvertFrom-Json

# Filter by project purpose; omit this option to include all classifications.
.\FlowBlox.exe --list-ps-projects --classification Function | ConvertFrom-Json

# Download to the configured project directory.
.\FlowBlox.exe --download-ps-project <PS-GUID> --private | Out-String

# Choose a different working directory; explicitly replace existing local files.
.\FlowBlox.exe --download-ps-project <PS-GUID> --private --directory .\work --overwrite | Out-String

# Refresh an existing hybrid project in place from the current online content.
.\FlowBlox.exe --reload-ps-project .\work\Example.fbprj --overwrite | Out-String

# Reload a private project using remembered credentials.
.\FlowBlox.exe --reload-ps-project .\work\Example.fbprj --overwrite --private | Out-String

# Update the PS project bound to a local file. An unbound file creates a new private project.
.\FlowBlox.exe --upload-ps-project .\work\Example.fbprj | Out-String

# Create a separate private PS project from an existing project.
.\FlowBlox.exe --upload-ps-project .\work\Example.fbprj --new | Out-String

# Explicitly choose visibility. An existing project's visibility is otherwise preserved.
.\FlowBlox.exe --upload-ps-project .\work\Example.fbprj --visibility public | Out-String

# Classify a reusable function or a complete solution. Existing classification is otherwise preserved.
.\FlowBlox.exe --upload-ps-project .\work\Example.fbprj --classification Function | Out-String
```

The PowerShell pipeline waits for the Windows GUI executable and captures its console output.
For scripts that must inspect the exit code, use `Start-Process -Wait -PassThru` with
`-RedirectStandardOutput` and `-RedirectStandardError`. No separate console window is allocated.

Successful commands write UTF-8 JSON to stdout. Listing returns `ProjectSpaceGuid`, `Name`,
`Description` and `Visibility`. Upload, download and reload additionally return `File`.
**Every successful upload returns the resulting Project Space GUID**, including newly created projects.
Errors go to stderr. Exit codes: `0` success/help, `1` operation failure, `2` invalid arguments,
`3` missing remembered authentication. No tokens or passwords are emitted in command output.

## Authentication

Public listing and downloads do not require an account. Add `--private` when listing or downloading
your private projects, or when reloading a private project. `--mine` lists all projects owned by your account, including public ones.
Uploads always require authentication.
Sign in through FlowBlox with **Remember credentials** enabled for the configured Project Space endpoint.
The command restores that session using the existing auto-login and account managers.
There is no interactive login prompt or password command-line option.

## Editing downloaded projects

Downloads create `.fbprj`, `.fbdeps` and `.fblocaldata` files together. Keep these files together:
the local sidecar holds the PS GUID and endpoint used for subsequent updates.
Existing files are protected unless `--overwrite` is passed. Downloads and uploads do not execute
the project, materialize its input files or run startup commands.
To inspect content, open the downloaded `.fbprj` as JSON or open it normally in FlowBlox.
`--query` searches project names; search downloaded content separately when looking for a block or API URL.

### Project notice maintenance

Process one project completely before starting the next:

1. Download the latest Project Space version with the Avalonia CLI into the configured FlowBlox project directory.
   Use a separate working subdirectory when local files already contain edits; do not overwrite them implicitly.
2. Keep `.fbprj`, `.fbdeps` and `.fblocaldata` together. Edit only `Notice` for a documentation-only change.
   Use `##` section headings, descriptive `[link labels](https://example.com/)`, lists and inline code for input names,
   paths and literal values. Preserve setup instructions, defaults and legal notices; keep the original language.
3. Open and save the project in the active Avalonia application, then close that editing session.
   For notice-only changes, preserve the existing flow configuration and layout. The desktop integration runner's
   `--save-only` mode saves an application working copy and verifies its notice and graph. It retains the original source
   definition to avoid uploading unrelated defaults introduced when opening older projects.
   Use the normal `--layout-save` workflow when a task includes flow/layout changes.
4. Upload the saved local project with `--upload-ps-project <file>`. Its sidecar retains the existing Project Space identity;
   omit `--new` and visibility/classification overrides when updating documentation.
5. Exit code `0` and the successful upload JSON are sufficient confirmation. Record the returned Project Space GUID,
   then continue with the next project. Do not routinely download the uploaded project again.

If an upload fails, retain the saved working copy and record the update as pending. Retry transient server failures using
that same file; do not claim completion or replace it with another download.

Project Notice, Run Project and Quick Start use the existing Markdown renderer. Plain text remains readable without
Markdown syntax; headings and links require the corresponding Markdown markup.

## Reloading a hybrid project

`--reload-ps-project` requires an existing local `.fbprj` bound to the configured Project Space endpoint.
It fetches current online content, preserves the local filename and replaces the project and its sidecars.
Stable version selection is cleared. Local changes are discarded; `--overwrite` is required explicitly.
Reloading does not execute the project or its startup commands. A failed download leaves the local files intact.

The desktop **Project > Reload from Project Space** command performs the same refresh, then closes
and reopens the project. It is enabled only for a saved hybrid project while execution is stopped.
A confirmation through the message-box service explains that local changes will be discarded.

## Required descriptions

Projects store `UseProjectDescriptionForProjectSpace`, which defaults to `true`, including when loading
older projects without this property. The PS upload dialog displays **Use the project description**;
when enabled, the description is read-only and reflects `ProjectDescription`.
An empty name or description prevents upload in both the dialog and command line.

Disable this setting to maintain a separate PS description. Existing projects retain their remote
description unless overridden with `--description`. A new project with automatic transfer disabled
requires `--description`. This option is rejected while automatic transfer is enabled.
New projects are private by default; `--new` on a bound project also assigns a new internal project identity.

When wiring shared PS flows, set Execute Project to `AbortOnError=true`, `AbortOnWarning=false`.

## AI Assistant

`GetPSProjects` searches names and descriptions without downloading project content. It accepts
`searchForNames` (contains-OR terms) and `visibility` (`public`, `private`, `all`) and returns descriptions,
GUIDs and available stable version numbers. Private access uses the current account or remembered credentials.
`GetSkillProject` inspects inputs and outputs with `projectSpaceGuid`, optional `projectSpaceVersion`
and `private=true` for private projects. `ExecuteExternalProject` accepts the same reference instead of `projectFile`;
the existing execution confirmation remains in place. Omitted versions prefer the latest immutable stable snapshot,
falling back to current content when no stable version exists. Pin the returned version in Execute Project dependencies.

For example, search for mail/IMAP settings, inspect the shared flow's email/protocol inputs and server outputs,
then offer the user a choice between a shared subflow and a local implementation. Details are loaded through
`explaining_project_space`; the session bootstrap contains only a short discovery hint.

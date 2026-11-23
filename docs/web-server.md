# Local web dashboard

FlowBlox.WebServer is an ASP.NET Core Blazor application for running existing flows from a phone or another browser on the local network. It reuses the desktop project loader, isolated RunnerHost processes and execution history. It does not require a cloud jobs service.

## Start and configuration

Use **Tools → Web server → Configuration**, then **Start**. **Open dashboard** opens the local address. The default is `http://127.0.0.1:9001/`; `127.0.0.1` permits access only from the same computer. For a phone, select a LAN listen address or `0.0.0.0`, then open `http://<computer-LAN-address>:9001/`. Allow the selected port through the firewall on the trusted private network if needed.

The generated configuration editor uses a detached registry. Configuration is stored as the encrypted `WebServer.Configuration` option. An **app password is optional**: empty means direct dashboard access; a configured password enables a single password prompt without usernames. Restart after configuration changes. Restarting invalidates existing web sessions. Password-protected access uses rate limiting, antiforgery protection and HTTP-only cookies.

An optional **PFX/P12 certificate with a private key**, together with its password, enables HTTPS on the configured port. Use a certificate trusted by the connecting devices for LAN password access and browser clipboard integration. No certificate is generated or installed automatically. Browser copy/paste remains available without clipboard API access; the extra Paste button appears only when supported in a secure browser context.

The server runs as a **background process in the signed-in Windows account**, allowing browser flows to use that user's interactive desktop and protected storage. Optional autostart runs after Windows sign-in. Locking Windows does not stop the process; signing out does. A Windows service is intentionally not used because services cannot provide an interactive browser in the user's desktop session.

Stopping the web server leaves independently running flows alive. Stop an execution from its execution page to terminate its recorded RunnerHost process tree. Process identity includes both PID and creation time to avoid terminating a reused PID.

## Projects, history and artifacts

The catalog includes the configured local project directory, recently loaded projects, Project Space favorites and the signed-in user's own Project Space projects. Project Space uses the desktop account's stored sign-in. Refresh reloads the catalog.

Input fields retain their declared order and support suggestions, allowed values and password masking.
The Insert menu beside each free-text input offers field references, another project's output directory and clipboard paste when available.
Selections open in the same menu; insertion uses the focused list entry when in List mode. The gear beside the field title controls presentation.
Text inputs default to a single line and automatically expand when the value contains line breaks, including pasted text.
Each free-text field has a small display-settings menu: Automatic, Single line, Multiline or List.
List mode offers newline, semicolon or comma separators, editable entries and Add/Remove actions; it preserves whitespace and empty entries.
Changing display mode or separator does not rewrite the value; editing a list joins its entries using the selected separator.
Preferences take effect immediately and are saved in browser local storage per internal project GUID and field name, independently of execution.
No input values are saved there. Settings are local to that browser/origin; if storage is blocked or the project has no GUID, the editor remains usable without persistence. Fresh forms leave fields marked **Store value locally** empty, matching the desktop Run project dialog. History is offered when input fields exist and applies prior submitted input values and supported option overrides as a template. Times are displayed in the browser's local time zone.

Artifact folders, counts and downloads are available from project and execution pages. Execution artifacts use that execution's recorded output directory, including path overrides. Reset requires confirmation, retains the root output directory and refuses to clear a directory used by an active execution. Browsing and downloads reject absolute paths, traversal outside the selected output directory and symbolic links/junctions. Output directories are server-selected, never arbitrary browser-supplied paths. Runtime logs are downloadable from completed execution details when available.

All dashboard clients control the same Windows user's available projects and files. The app password is an access gate, not a multi-user authorization model.

## Input configurations

Desktop Run/Execute and the web dashboard use the same named input configurations. When a local
project and its `.fblocaldata` sidecar exist, a **Local developer configuration** is offered first.
It is read directly from the sidecar whenever configurations are loaded, not stored as a duplicate
in the preset list. Online-only projects and local projects without a sidecar do not get this entry.

The local source profile cannot be renamed or overwritten. Selecting it copies its inputs into the
current execution form; edits affect that run only. **Save configuration** creates a separate named
preset, retaining the existing password protection. Neither action writes to the project or its sidecar.

## Build and verification

Build `FlowBlox.WebServer/FlowBlox.WebServer.csproj` or the desktop application. Desktop build, publish and MSI packaging place the companion beside the desktop executable, sharing existing Core/RunnerHost dependencies. The companion includes RunnerHost and its runtime dependencies. Machines without the .NET SDK need the **ASP.NET Core 10 Runtime** in addition to FlowBlox's Windows Desktop Runtime.

Server logs are under `%LOCALAPPDATA%\FlowBlox\webserver`: `server.log` (rotated at 2 MB) and `startup.log` for initialization failures. For an isolated profile, start with `--data-directory <absolute-directory>` before any options or launcher stores initialize.

`scripts/tests/Test-WebServer.ps1` performs a loopback HTTP contract check with an isolated profile, including password access, antiforgery, static Blazor assets and project metadata rendering. `-WithoutPassword` checks direct access. Add `-WithoutPassword -BrowserInputs` for interactive editor checks and desktop/mobile screenshots with Node 22+ and installed Chrome/Edge (no npm dependencies). These checks use synthetic projects and do not execute a flow. Domain tests cover artifact boundaries, store concurrency, password storage and existing runner/history behavior.

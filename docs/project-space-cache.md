# Project Space content cache

`FlowBloxProject.FromProjectSpaceGuidAsync` shares downloaded definitions between desktop, CLI and runner processes.
The default `cacheOnly=false` checks project metadata and downloads content only when its timestamp fingerprint changes
or no valid cache exists. Missing timestamps trigger a download rather than silently trusting stale content.

Entries live under `Paths.LocalAppDataDir/project-space-cache/<endpoint hash>/<Project Space GUID>/<version or latest>/project.json`.
The GUID is the Project Space GUID, not the internal `FlowBloxProject.ProjectGuid`. Stable versions have separate entries;
editing a version comment does not invalidate immutable content. Each load creates a fresh project instance.
Writes are atomic, downloads are locked across processes and cached payloads are verified using SHA-256.

Execute Project preloads its referenced project in `RuntimeStarted`, reports success or failure in the runtime log and
aborts startup if loading fails. Its metadata cache is cleared at runtime start and finish. Runner requests from this block
set `ProjectSpaceCacheOnly=true`: the runner reads existing cached content without API calls or automatic login.
A missing or corrupt cache causes an error; cache-only execution never falls back to downloading.
Local child projects are also preloaded, but runners continue opening their local project file.

Normal loads still check server access, including authentication for private projects. Cache-only loading is intended for
the local runner after successful preload. Cached definitions reside in the current operating-system user's AppData directory;
credentials and live runtime state are not part of the cache entry.

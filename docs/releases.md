# How to create a FlowBlox release

FlowBlox maintains one current release line. **Pushing** a stable version tag on
`main` to GitHub starts an automatic Windows x64 release: checks, unit tests, WiX MSI build,
SHA-256 checksum, and publication on GitHub Releases.

We aim for a regular release approximately every **six weeks**. This is a planning
guideline rather than a fixed deadline: scope and readiness may shift a release,
and important fixes may be delivered sooner.

## Supported release platforms

| Platform | Status | Release workflow | Package |
| --- | --- | --- | --- |
| Windows x64 | Available | `release-windows.yml` | WiX MSI |
| Linux | Planned; no release build yet | To be added with the Linux port | To be defined |

Release workflows and downloadable artifacts are separated by operating system.
The application version remains shared: a future Linux package can belong to the
same version tag as the Windows package.

## Create a Windows x64 release

1. Merge the intended changes into `main` through pull requests. Wait for the
   **Windows build and unit tests** check to pass.
2. Update your local checkout and check that it contains the intended release:

   ```powershell
   git switch main
   git pull --ff-only origin main
   git fetch origin --tags
   git status --short
   ```

   Your working tree should be clean.
3. Prepare the next version as a **local tag only**:

   ```powershell
   ./scripts/New-FlowBloxReleaseTag.ps1
   ```

   By default this increments minor and resets patch, for example `v1.2.3` ->
   `v1.3.0`. It uses the highest existing local stable version tag; without tags,
   it uses the version in `FlowBlox.Avalonia.csproj`. The script does not fetch,
   push, modify project versions, or publish anything.
4. When you are ready to publish, **push the exact tag returned by the script**:

   ```powershell
   # Example only: replace v1.2.3 with the tag created above.
   git push origin v1.2.3
   ```

5. Open the repository's **Actions** tab and follow **Release Windows x64**.
   After successful completion, the **Releases** page contains the MSI,
   its `.sha256` checksum, and automatically generated release notes. The release
   title explicitly identifies Windows x64; no Linux package is published yet.

**Pushing the tag publishes the release automatically when all checks pass.**
Do not create a GitHub Release manually before running this workflow.

## Asking Codex to prepare a release

For example: **"Create the release tag for the next version. Do not push."**
The root `AGENTS.md` points agents directly to this guide. Codex should use the
tag helper and report the tag and commit it selected. A request to prepare a tag
does not authorize a push or publication.

- **Default:** increase minor by one, reset patch to zero.
- **Only when explicitly requested:** `-Major` (`1.2.3` -> `2.0.0`) or `-Patch`
  for a hotfix (`1.2.3` -> `1.2.4`).
- Major versions never increase automatically, including when the MSI minor
  version limit is reached. The helper rejects an out-of-range version.
- Before creating a tag, use an up-to-date, checked `main` and locally available
  release tags. Updating the checkout and tags is a separate preparation step.
- Publishing remains a separate explicit action. The tag is created locally;
  GitHub Actions builds and publishes only after that tag is pushed.

## Versioning

- Use stable tags in the exact form `vMAJOR.MINOR.PATCH`, such as `v1.2.3`.
  Prerelease suffixes such as `-rc.1` are currently rejected.
- The tagged commit must be part of `main`.
- CI calls the existing `scripts/update-version.ps1` in its disposable checkout.
  No manual version edit or automated commit back to `main` is necessary.
  The script remains available for local builds.
- Tag `v1.2.3` produces application version `1.2.3`, assembly/file version
  `1.2.3.0`, and `FlowBlox_1.2.3.0_x64.msi`.
- Windows Installer compares the first three version components; the workflow
  enforces its limits of `255.255.65535`. Increase those components for updates.
- Never move or reuse a published tag. Deliver fixes as a new release.

## One-time GitHub setup

1. Commit and merge the workflows and scripts into `main`.
2. Under **Settings > Actions > General**, allow the official `actions/*`
   actions used by the workflows. The workflows declare their token permissions;
   only the final publication job requests repository content write access.
   No personal access token or additional secret is required.
3. Once CI has run, add **Windows build and unit tests** as a required check
   to the existing `main` ruleset. Keep the PR requirement enabled.
4. Recommended: add an active tag ruleset targeting `v*` that prevents tag
   updates and deletion. If restricting tag creation too, give only release
   maintainers permission to bypass that restriction.

## Checks and troubleshooting

- PRs and pushes to `main` build the active desktop application, CLI, runner
  (through the desktop project reference), WorkerService, and test project.
  Tests run automatically with `TestCategory=UnitTest`. A run with no executed
  tests also fails. TRX results are attached to the Actions run for 14 days.
- Release tags rerun the same checks before preparing native ONNX runtimes and
  invoking the existing `scripts/Build-FlowBloxMsi.ps1` on a Windows runner.
- Interactive desktop checks and tests requiring external services, credentials,
  or model downloads are separate from this unit-test job.
- If checks or packaging fail, no public release is created. Inspect the failed
  Actions step and its logs. For a transient failure, use **Re-run failed jobs**.
  A code correction needs a new commit and a new version tag.
- Publication first creates a draft with the assets, then publishes it. If
  publication fails after draft creation, inspect and remove that incomplete
  draft before retrying. Existing releases are never overwritten automatically.
- Windows packaging installs JetBrains Mono Regular system-wide through WiX.
  The font remains installed after uninstall; application startup skips font
  installation when the family is already present.
- The application and MSI currently have no configured Windows Authenticode
  signing step. The legacy WinForms project contains old manifest certificate
  settings with manifest signing disabled; these do not sign Avalonia or its MSI.
  Code signing requires a separate certificate/signing setup.
- The installer is framework-dependent; users need the .NET 10 Desktop Runtime.
- This workflow publishes to GitHub Releases. It does not update the website's
  download directory or update manifest. The existing
  `scripts/Publish-FlowBloxMsiRelease.ps1` remains a separate website deployment.

For local packaging details, see the [installer guide](../FlowBlox.Installer/README.md).

# Contributing to FlowBlox

Contributions are welcome: bug fixes, FlowBlocks, examples, documentation, tests, and portability improvements all help the project move forward.

## Start with an issue

Before implementing a bug fix or feature, [open an issue](https://github.com/mscheitza/flowblox/issues) or join the existing discussion. Describe the problem, expected behavior, and proposed scope. For bugs, include reproduction steps and the affected version; for features, explain the use case.

If you would like to implement the change, say so in the issue. Wait for a maintainer to confirm the scope and coordinate who is working on it before starting substantial work. This avoids duplicate implementations and changes that do not fit the project direction. Small documentation-only corrections can be proposed directly as pull requests.

Anyone can submit a pull request from a fork of this public repository. You do not need collaborator access or an invitation to propose a change. Agreement on an issue coordinates the work; maintainers decide whether the finished contribution is ready to merge.

## Development workflow

1. Agree on an issue and its scope.
2. Fork the repository if you do not have write access, then create a branch from the latest `main`.
3. Implement one focused change and validate the affected behavior.
4. Open a pull request targeting `main`. Link the issue, explain the resulting behavior, and include validation results. Use a draft PR for early feedback.
5. Address review feedback. A maintainer reviews and merges the contribution when it is ready.
6. Delete the working branch after merge.

For an issue fully resolved by the PR, include `Closes #123` in its description. Use `Refs #123` when the work covers only part of the issue.

### Branch names

Use lowercase names with an issue number and a short description separated by hyphens:

| Change | Branch pattern | Example |
| --- | --- | --- |
| Feature or enhancement | `feature/<issue-number>-<description>` | `feature/123-linux-build` |
| Bug fix, including an urgent fix | `bugfix/<issue-number>-<description>` | `bugfix/124-project-close-redraw` |
| Documentation-only change | `docs/<issue-number>-<description>` | `docs/125-generator-guide` |

For a small documentation correction without an issue, use `docs/<description>`.

`main` is the integration branch. Changes reach it through pull requests; contributors should not push changes directly to it. Maintainers normally squash-merge a focused PR and keep its title descriptive.

## Build and validate

Desktop development currently requires Windows and the .NET 10 SDK. The active UI project is **`FlowBlox.Avalonia`**, which builds `FlowBlox.exe`. The old `FlowBlox` WinForms project is retained only as a reference; do not implement desktop UI changes there.

From the repository root, restore dependencies for an initial checkout or after changing package references, then build:

```powershell
dotnet restore FlowBlox.Avalonia/FlowBlox.Avalonia.csproj
dotnet build FlowBlox.Avalonia/FlowBlox.Avalonia.csproj --no-restore
```

For relevant unit tests:

```powershell
dotnet restore FlowBloxTest/FlowBloxTest.csproj
dotnet test FlowBloxTest/FlowBloxTest.csproj --no-restore --filter "TestCategory=UnitTest"
```

For desktop hosting, docking, or WPF integration changes, also run the [Avalonia desktop integration checks](FlowBlox.Avalonia.IntegrationTests/README.md). These require an interactive Windows desktop and run separately from `dotnet test`.

Use meaningful tests for behavior changes and describe manual UI checks when applicable. Documentation-only changes need link and formatting checks rather than an application build. Follow the existing code style, keep changes focused, and localize new user-facing text in English and German. See [agent and coding conventions](AGENT_CONVENTIONS.md) and the [Avalonia application guide](FlowBlox.Avalonia/README.md) for project-specific details.

## Reviews and repository access

Opening a PR does not grant write access. Maintainers review the implementation, relevant checks, and consistency with the agreed issue before merging.

Contributors who repeatedly demonstrate careful implementation, constructive reviews, and reliable follow-through may be invited to take on more responsibility. Access is granted deliberately; it is not automatic after a particular number of contributions. Most contributors can continue working through forks.

Repository administration and code review are separate responsibilities. A reviewer or regular contributor does not need full administrator access. If the project moves to a GitHub organization, use granular roles such as Triage for issue management, Write or Maintain for trusted maintainers, and Admin only for repository administration. Personal-account repositories have a simpler owner/collaborator permission model.

## Releases

FlowBlox maintains **one current release line**. There is no separate `develop` branch and no permanent `release` or `hotfix` branch.

- Features and fixes are integrated into `main` through PRs. Only merge completed work that can ship in the next release.
- Maintainers choose a tested commit on `main`, update version metadata through a PR when needed, and publish a version tag such as `v1.2.3` with a corresponding GitHub Release and build artifacts.
- Tags identify the exact source revision of each published version. Do not move a published version tag to a different commit; publish a new version instead.
- Urgent corrections use the normal `bugfix/<issue-number>-<description>` workflow and are included in the next release from `main`.
- Older releases remain identifiable through their tags; they are not maintained as separate patch branches. Fixes are delivered in the latest release.

For packaging instructions, see the [installer guide](FlowBlox.Installer/README.md).

## Planning work

Use issues to discuss and track work. A lightweight GitHub Projects board can organize them as **Inbox → Ready → In progress → In review → Done**. A maintainer confirms the scope before moving a bug or feature to Ready. Use milestones for release goals when useful; fixed sprints or Scrum ceremonies are not required.

The [development milestones in the README](README.md#next-development-milestones) identify larger areas where contributions are welcome.

## Maintainer setup on GitHub

The policies above must be backed by repository settings. Adding this file or the templates does not enable branch protection or create a project board.

Configure a ruleset or branch protection for `main` that requires pull requests, blocks force pushes and deletion, and requires the applicable build/test checks once those checks are available in CI. Keep administrative bypass permissions narrow.

While there is one maintainer, review incoming contributions before merging and use PRs with recorded validation for maintainer-authored changes too. Do not require an independent approving review for every PR until another trusted reviewer is available: GitHub does not allow authors to approve their own PRs. Once there are multiple maintainers, require at least one independent approving review and invalidate approvals when the reviewed code changes. Add `CODEOWNERS` when ownership of particular areas has been agreed.

## GitHub references

- [Forks and pull requests](https://docs.github.com/en/pull-requests/reference/pull-requests)
- [GitHub flow](https://docs.github.com/en/get-started/using-github/github-flow)
- [Protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)
- [Repository permissions](https://docs.github.com/en/get-started/learning-about-github/access-permissions-on-github)
- [Releases and tags](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
- [GitHub Projects](https://docs.github.com/en/issues/planning-and-tracking-with-projects)

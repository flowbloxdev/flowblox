# FlowBlox Agent Conventions

## Purpose
This file summarizes core project conventions for AI/coding agents working in the FlowBlox repository.

## High-Level Architecture
- `FlowBlox.Avalonia` is the active UI host and the main desktop application. Implement and verify host UI changes here, including the real desktop checks in `FlowBlox.Avalonia.IntegrationTests`.
- `FlowBlox` is the retired WinForms host. It is retained only as a knowledge base/reference; it is no longer used. Do not implement UI fixes there unless explicitly requested.
- `FlowBlox.UICore` contains WPF-based UI infrastructure (property rendering, editors, factories, resolvers).
- `FlowBlox.Core` contains execution/runtime logic and the core domain model.
- FlowBlock execution logic belongs to `FlowBlox.Core` (runtime, testing, generation, execution pipeline).

## Code Formatting
- Always put XML documentation `<summary>` and `</summary>` on separate lines, with the summary content on one or more lines between them; never use inline summary tags.
- Prefer open, readable formatting over compressed code: one statement or initializer entry per line.
- Put conditional bodies on separate lines; use braces for multi-line bodies and object assignments.
- Wrap longer signatures and calls with one parameter or argument per line; put LINQ steps on separate lines.
- Separate logical steps with blank lines and use descriptive local names. Keep the implementation simple without squeezing its layout.
- Do not combine type checks and multiple property checks in compound property-pattern guard clauses. Assign the typed value to a descriptive local first, then express the guard with explicit boolean conditions.

## FlowBlock UI Generation Model
- FlowBlock property UIs are generated via attributes (annotation-driven UI).
- Main pipeline:
  - Schema/property resolution in PropertyView
  - Control resolution via resolver classes
  - Concrete controls built via factories
- Key classes in `FlowBlox.UICore`:
  - `Resolver/PropertyControlResolver`
  - `Resolver/TextBoxWithOptionalButtonsCreator`
  - `Factory/PropertyView/DataGridFactory`
  - `Factory/Base/PropertyFactoryBase` (`PropertyViewControlFactoryBase`)

## Required Annotation Namespaces
- FlowBlox UI annotations:
  - `FlowBlox.Core.Attributes`
  - Commonly used:
    - `FlowBloxUIAttribute`
    - `FlowBloxTextBoxAttribute`
    - `FlowBloxDataGridAttribute`
    - `FlowBloxListViewAttribute`
    - `FlowBloxUIGroupAttribute`
    - `FlowBloxSupportedTypesAttribute`
    - `FlowBloxUIFileSelectionAttribute`
- Display/localization annotation:
  - `System.ComponentModel.DataAnnotations`
  - Use `DisplayAttribute` with `ResourceType = typeof(FlowBloxTexts)`

## Localization and Logging Conventions (Mandatory)
- Always create localization keys for new user-visible types/properties/tooltips.
- German localization files must contain real German umlauts (`ä`, `ö`, `ü`, `Ä`, `Ö`, `Ü`, `ß`). Do not use XML character codes for normal text and avoid mojibake such as `Ã¤`.
- Key naming format:
  - Type display name: `ClassName_DisplayName`
  - Property label: `ClassName_PropertyName`
  - Property tooltip: `ClassName_PropertyName_Tooltip`
- Add keys in both:
  - `FlowBlox.Core/FlowBloxTexts.resx`
  - `FlowBlox.Core/FlowBloxTexts.de.resx`
- After changing `.resx` keys, regenerate the strongly typed designer for the neutral/main `.resx` file so `DisplayAttribute(ResourceType = ...)` can find the generated static string properties. Passing a localized file is okay; the script maps it to the neutral file:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Update-StronglyTypedResourceDesigner.ps1 -ResxPath FlowBlox.Core/FlowBloxTexts.resx`
  - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Update-StronglyTypedResourceDesigner.ps1 -ResxPath FlowBlox.Core/FlowBloxTexts.de.resx`
- The designer regeneration script uses `StronglyTypedResourceBuilder` with an explicit `.resx` base path so image/SVG `ResXFileRef` entries resolve correctly. It processes only existing strongly typed resource designers and skips WinForms/WPF control designers.
- Encoding requirement:
  - `FlowBlox.Core/FlowBloxTexts.de.resx` must be saved as `UTF-8` **without BOM**.
- Rule: provide tooltip text for explanatory/complex properties (recommended), especially for selectors/patterns (e.g., XPath, CSS selector, regex). Include short examples to improve UX where helpful.

### WPF/UICore localization
- Localize both the title and message of every message box. Never place user-visible fallback text directly in the call.
- Provide a neutral English `.resx` and a `.de.resx`. In `FlowBlox.UICore`, keep resources scoped to their owner:
  - A view and its ViewModel share the view resource, for example `Resources/PropertyWindow.resx` and `Resources/PropertyWindow.de.resx`.
  - Non-view classes use individual resources in the central `Resources/ClassResources` directory, for example `Resources/ClassResources/UIActionsProviderBase.resx` or `Resources/ClassResources/TestDefinitionViewFactory.resx`.
- Always resolve strings through `FlowBloxResourceUtil.GetLocalizedString(key, typeof(ResourceClass))`. Do not read generated resource properties directly, including for message-box titles and messages.

### Logging
- Log a concise technical English message that identifies the operation and failure location; do not reuse the localized UI text as the log message.
- In `FlowBlox.UICore` and other view/UI code, write to the application logs through `FlowBloxLogManager.Instance.GetLogger()`.
- Select `Info`, `Warning`, or `Error` according to severity. In a `catch` block, pass both the technical message and the caught exception to `Warning(message, exception)` or `Error(message, exception)` as appropriate.
- Inside FlowBlock execution/runtime code, report through the active runtime (`runtime.Report(...)`) so the problem remains associated with that execution. Do not substitute the UICore application logger for runtime reporting.

## Property Declaration Conventions for FlowBlocks/Strategies
- Use `[Display(...)]` on user-facing properties.
- For editable string content with optional code editor/suggestions use:
  - `[FlowBlockTextBox(...)]`
- For association/linkable objects use:
  - `[FlowBlockUI(Factory = UIFactory.Association, ...)]`
- For list/grid rendering use:
  - `[FlowBlockUI(Factory = UIFactory.ListView|GridView, ...)]`
  - `[FlowBlockDataGrid(...)]` or `[FlowBlockListView(...)]` where needed.

## FlowBlock Card Summaries (`GetDisplayableProperties`)
- Every concrete FlowBlock with meaningful user configuration should override `GetDisplayableProperties()`.
- Start with `base.GetDisplayableProperties()` and append only the properties that best identify the configured operation at a glance.
- Prefer primary configuration such as service URLs and paths, source/target providers, associated managed-state FlowBlocks, selectors, patterns, and expressions.
- Omit secondary formatting and tuning details such as separators, indentation, retry values, and similar switches. Also omit credentials, secrets, large payload/content properties, and verbose mapping/result collections unless they are the defining configuration of the FlowBlock.
- Keep the properties in the same logical order in which the user configures the FlowBlock.
- FlowBlocks without meaningful user-facing configuration may rely on the base implementation.

## Unit-Test Scope
- Do not add trivial baseline tests for changes below roughly two story points of complexity.
- A test must protect meaningful FlowBlox-owned behavior such as branching, validation and error handling, state transitions, migrations, serialization compatibility, runtime interaction, or non-trivial orchestration.
- Do not create tests that merely prove framework/BCL or third-party behavior, straightforward property-to-option mapping, or known cryptographic library output. For example, hashing `"abc"` and comparing it with the standard SHA-256 digest does not meaningfully test FlowBlox.
- Do not add tests that only mirror declarations or configuration, such as checking that a constructor assigns a default, an `OptionsInit` method adds an option, or an annotation/resource key exists. These tests add refactoring cost without protecting runtime behavior.
- Do not add unit tests for display names, descriptions, tooltips, or other display-only metadata, including tests that only verify their presence or localization. Verify these changes through resource generation and a successful build instead. Exact text assertions remain appropriate for functional outputs, validation errors, protocols, or other text whose content is the behavior under test.
- Thin wrappers and direct mappings normally require a successful build, not a dedicated unit test.
- Add tests when project-specific logic or a concrete regression risk makes them valuable, even if the implementation itself is small.

## Runtime/Execution Conventions
- Keep execution concerns in `FlowBlox.Core`.
- Generation strategies and FlowBlock test/runtime integration must use existing runtime pipeline, not parallel ad-hoc execution paths.
- Prefer passing/using existing runtime context from executors instead of creating duplicate runtime instances.

## FlowBlock Notifications
- For concise, flow-block-local error/warning messages shown directly in the FlowBlock UI, use the Notifications model.
- Define a block-specific enum with `FlowBloxNotificationAttribute` on each entry.
- Register it via `NotificationTypes` override in the FlowBlock.
- Trigger it with `CreateNotification(runtime, ...)`.
- Keep notification display texts short and action-oriented.

## Practical Agent Rules
- Reuse existing infrastructure before introducing new patterns.
- Keep changes consistent with annotation-driven UI architecture.
- Do not introduce user-visible strings without localization keys.
- For new UI-facing properties: label keys are required. Tooltip keys are strongly recommended for explanatory/complex properties (e.g., XPath/CSS selectors) and should include concise examples when useful.
- Build and test with `--no-restore` by default, especially after NuGet restore hangs or fails because NuGet cannot be reached.
- If a local build fails because build outputs are locked, the application is already running, or a similar local environment issue is present, do not work around it with temporary output directories. Report the situation to the developer and let them close the app or run the verification locally.
- Run restore only when NuGet package references were newly added or changed. If restore is required and cannot reach NuGet, do not keep retrying; report the restore problem and let the developer handle that verification step.
- Do not use ad-hoc reflection/introspection projects to discover NuGet API shapes; this has proven unreliable in this repository. Prefer the local package XML documentation, installed package docs, existing code usage, and compiler feedback.

## Protecting concurrent developer changes

- Existing developer changes take precedence and must not be overwritten with an earlier state previously read or generated by Codex.
- When editing a file for the first time, record the current file hash in the working context and read the required target section.
- After making a file change, update the stored hash. Before a later edit to the same file, especially after a new user message or a longer work step, compute the hash again.
- If the hash has changed since the last Codex edit, assume an interim developer change, reread the current target section, and treat that state as authoritative. Do not write back an older state previously produced by Codex.
- Preserve interim developer changes. Modify them only when required for the current task or when there is an obvious error; explicitly mention such corrections in the result.
- Full files usually do not need to be reread; a fingerprint and a small context section are normally enough.

## Icon Workflow
- For every new FlowBlock or other component that needs a specific icon, first decide which Pictogrammers Material Design Icons (MDI) symbol best communicates its purpose. Do not browse or reuse `FlowBloxIcons.resx` as the initial source of ideas.
- After selecting the semantic match in the Pictogrammers MDI library, check whether that exact icon is already imported. If so, reuse it.
- Otherwise download the original MDI SVG to `FlowBloxResources/temp`, copy the required SVG to `FlowBlox.Core/Resources`, and import it into `FlowBlox.Core/FlowBloxIcons.resx`.
- Regenerate `FlowBloxIcons.Designer.cs` with `scripts/Update-StronglyTypedResourceDesigner.ps1`, then use the generated resource key in code.
- Keep the FlowBlox icon resources limited to directly downloaded Pictogrammers SVGs that are actually used; do not import speculative alternatives or custom approximations.
- If the icon is not imported yet, use `FlowBloxComponent` icon as temporary compile-safe fallback and switch to the final icon after import.

## FlowBlock Execute Contract
- Keep notifications scoped to the concrete operation/context of the flow block.
- Do not rely on `BaseFlowBlock` generic unexpected-error notification for expected domain failures.
- In `Execute(...)`, always leave via exactly one flow contract action:
  - non-result blocks: `ExecuteNextFlowBlocks(runtime)`
  - result blocks: `GenerateResult(...)`
- On handled failures, keep the same contract action (continue with next block or emit empty/default result), paired with a scoped notification.
- For iterator-like blocks, consider a dedicated warning notification when no items are found (empty but valid result set).

## ManagedObject UI Operations
- For ManagedObject associations, default to allowing all UI operations unless the domain context requires restrictions.
- In practice this means: if no explicit `UIOperations` are set, treat it as fully enabled (`Link`, `Unlink`, `Create`, `Edit`, `Delete`).
- Only constrain operations when there is a clear functional or security reason in the specific business context.

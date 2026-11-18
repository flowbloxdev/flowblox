# Execution environment prerequisites and Quick Starts

This mechanism covers dependencies of the execution environment: installed programs,
WebDrivers, native runtime libraries and model files. It does not cover missing API
keys, provider references, blank properties or invalid FlowBlox configuration.
Use standard annotation validation for configuration consistency. Defensive execution
checks retain their ordinary exception or notification; do not wrap them in
`ExecutionEnvironmentPrerequisiteException` or manually invoke annotation validators.
Resolve required configuration before entering an environment-initialization catch.

Missing installations are execution errors. Keep the existing lifecycle of each component:
ONNX initializes in `RuntimeStarted`; browser sessions and media processes are created when their blocks/providers execute.
Checks must use the resolved values at that point. Flow-generated paths are not evaluated by a separate startup scan.
Existing media output can still be reused without starting or requiring FFmpeg.

## Extension API

Extensions can connect a prerequisite failure to their own
popup-provider target type:

```csharp
var executable = ExecutionEnvironmentPrerequisites.RequireExecutable<MyTool>(
    "my-tool",
    this.Resolve(owner => owner.ExecutablePath));
```

The helper returns the located executable or throws `ExecutionEnvironmentPrerequisiteException`. It checks an explicit path, the current
directory, the application directory and `PATH`; an optional additional search directory can be supplied. It never downloads
or executes programs while checking their presence. Errors starting an installed program still need to be handled at the call site.

For another installation or initialization failure, wrap the cause with the same open metadata:

```csharp
throw new ExecutionEnvironmentPrerequisiteException(
    "my-tool",
    typeof(MyTool),
    configuredPath,
    exception);
```

Declare `DisplayAttribute` on `MyTool` as usual. That type's display name identifies the matching Help menu entry and appears
in the detailed error message. There is no prerequisite registration in Core and no dependency on UI assemblies.

## Automatic FlowBlock validation

Override the documented hook on `FlowBloxComponent`; the shared execution boundary calls it automatically
for the FlowBlock and its associated ManagedObjects, including shared references and cycles, once per component.
Do not call it manually inside an `Invoke` delegate. Activation conditions and required fields are checked first;
the parent context is assigned before the hook runs. Read selected field values through `Resolve` in the hook;
the configuration object itself retains its literals. Deferred iteration-context blocks
are checked only when their accumulated invocation actually executes, not for each incoming row.

```csharp
public override void ValidateExecutionEnvironmentPrerequisites()
{
    base.ValidateExecutionEnvironmentPrerequisites();
    ExecutionEnvironmentPrerequisites.RequireExecutable<MyTool>(
        "my-tool",
        this.Resolve(owner => owner.ExecutablePath));
}
```

The default hook has no prerequisites. A thrown `ExecutionEnvironmentPrerequisiteException` skips the executor, emits the compact
English `SetupRequired` error and completes the normal result/continuation contract. Other exceptions retain the existing
unexpected-error handling. API keys and required provider associations remain ordinary configuration validation;
a simple Quick Start does not require any prerequisite exception or hook.

Override the failure hook only when the block needs its own scoped notification or failure-result payload:

```csharp
protected override void OnExecutionEnvironmentPrerequisiteFailure(
    BaseRuntime runtime,
    ExecutionEnvironmentPrerequisiteException exception)
{
    CreateNotification(runtime, MyNotifications.SetupRequired, exception);
    GenerateResult(runtime);
}

public enum MyNotifications
{
    [FlowBloxNotification(NotificationType = NotificationType.Error)]
    [Display(Name = "Setup required")]
    SetupRequired
}
```

Notification enums and their display names remain compact English text without localization. `CreateNotification` always
uses the enum's `GetDisplayName()` for the node message and `NotifyWarning`/`NotifyError`; there is no message-override overload.
The exception's `Message` contains the full localized installation/configuration guidance;
pass the exception through so logs, error details and aborted Runner results retain it. The Runner's existing abort settings
and per-notification severity overrides still apply.

Managed-object media providers inherit this same hook. FlowBlock execution evaluates the associated components before
its executor; direct `DownloadAsync` callers use the same evaluator at the provider boundary before `DownloadCoreAsync`.
There are no manual prerequisite calls inside the block's executor or the provider's download implementation.

The boundary checks for reusable FFmpeg output first, preserving reuse without installed binaries. Conditional components
can override `GetExecutionEnvironmentPrerequisiteComponents()` to omit dependencies that the current operation does not use.
The default uses `GetAssociatedManagedObjects()`; evaluation uses a fresh graph for each invocation, with reference
identity de-duplication. ONNX initialization remains in `RuntimeStarted`; browser creation remains lazy at execution time.

## Automatic setup guidance after execution failures

When a runtime reports an `ExecutionEnvironmentPrerequisiteException`, including wrapped and aggregate exceptions,
`BaseRuntime.ExecutionEnvironmentPrerequisiteFailed` carries the target type and assembly names as structured metadata.
The desktop host resolves these names against registered Quick Start providers and asks on the UI thread whether to open
the existing setup guide. The localized Yes/No question uses the shared message-box service and names the affected component.
Only Yes opens the guide; declining leaves execution status and diagnostics unchanged.
No component instance is created. Completed onboarding does not suppress this setup guidance.
Each provider is offered only once per execution, including when declined; questions and guides are queued rather than stacked.

Separate runners include `ExecutionEnvironmentPrerequisiteFailures` in `RunnerResponse`, independently of warning/error abort settings.
The desktop launcher offers the matching guide when it receives a completed response, including runs started from the project
execution dialog. `ExecuteProjectFlowBlock` forwards child-runner failures to the parent runtime, so nested executions also
reach the same UI handler. A standalone CLI/headless runner returns the metadata and full diagnostics without opening a window.
Old responses without this field remain readable. Unknown or unavailable providers do not trigger arbitrary assembly loading.

## Automatic Help menu

Register the existing popup provider as `IComponentPopupProvider`. Providers derived from `ComponentPopupProviderBase<TTarget>`
with `PopupEvent.Open` appear automatically under Help → Quick Starts. No AppWindow changes or target instance creation are needed.
The title defaults to the target's display name, with the provider window title as a fallback for targets without `DisplayAttribute`.

Manual opening always works, even after onboarding has been completed, and does not change the automatic onboarding-completed option.
Providers with instance-specific instructions can override `CreateQuickStartItems()` to supply target-independent guidance,
or opt out of the menu with `IsQuickStart = false`. The default calls `CreateItems(default)`; generic instructions must not depend
on a configured target instance. A provider implementing the interface directly can expose `IsQuickStart`, `QuickStartTitle`
and `CreateQuickStartWindow()` itself.

## Built-in checks

- yt-dlp validates its executable, FFmpeg/FFprobe and an explicitly configured Deno path before starting discovery.
- With no explicit Deno path, yt-dlp remains responsible for runtime/source detection. Its JavaScript-runtime failure diagnostics
  are converted into a setup failure, including for redirects and extractors not recognizable from the original URL.
- FFmpeg validates its binaries before processing, after checking whether an existing output can be reused.
- Browser construction failures include guidance for browser, WebDriver and browser-settings installation/configuration.
- ONNX keeps its existing eager native-runtime/model initialization and cleanup; failures now include the matching Quick Start.

Requirements are checked when used, and do not become global requirements for unrelated blocks or providers.

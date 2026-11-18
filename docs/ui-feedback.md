# UI feedback

`FlowBloxCustomStyles.xaml` owns the shared feedback styles. Their colors are semantic brush resources in
`Views/Styles/Themes/FlowBloxTheme.xaml`; changing a palette updates all consumers.

## Notification boxes

Use `FlowBlox.NotificationBox.Information`, `.Success` or `.Error` on a `Border`.
All variants share padding, border thickness and rounded corners through `FlowBlox.NotificationBox`.
Each variant only supplies its `Background`, `Border` and `Text` brush triplet under
`FlowBlox.Brush.Notification.<variant>.*`.

Use `FlowBlox.NotificationIcon`, `FlowBlox.NotificationTitle` and `FlowBlox.NotificationText` for the content.
Their foreground follows the surrounding notification box, including when its variant changes.

| UI | When visible |
| --- | --- |
| ProjectExecutionWindow: blue Started box | Viewing an execution that has already been submitted, both running and completed; shows local start time and explains that the execution configuration cannot be changed. |
| ProjectExecutionWindow: completion box | Execution finished or launch failed; green for success, red for failure, with diagnostics/log links when available. |
| RegistrationWindow | `ErrorText` contains an input-validation or registration/service error. |
| PasswordResetWindow | `ErrorText` contains a reset-validation or service error. |
| EditUserPropertiesWindow | `ErrorText` contains a validation or update error. |
| EditProjectMetadataWindow | `ErrorText` contains a validation or save error. |
| EditProjectVersionMetadata | `ErrorText` contains a validation or save error. |
| CreateProjectVersion | `ErrorText` contains a validation or publishing error. |
| TestDefinitionView | `HasValidationMessages` is true; displays the test definition's validation messages. |

Execution history stores the project description at launch. Older entries read only the description from the
available project JSON, without loading its registry, extensions or local field values. If neither is available,
the description area is hidden. The Started notice remains separate from the description.

The execution dialog bounds long descriptions to about five lines (100 DIP including padding)
and scrolls the remaining text. Completed runs expose the runtime logfile returned after
execution. Its link uses `FlowBloxEditingHelper.OpenFileWithSelection`, shared with task
management, to choose the configured FlowBlox editor or the system default application.

Dashboard executions persist the runner PID and process start time. The Started box
shows the PID; the footer offers process termination only while that exact runner
instance is active. Termination stops its process tree and records user cancellation.

## Indeterminate progress

Use `FlowBlox.ProgressBar.Indeterminate` for a slim continuous marquee, as in the
Avalonia splash and dashboard. The WPF template scales its animated segment with
the available width and stops animation while hidden. Colors come from
`FlowBlox.Brush.ProgressForeground` and `FlowBlox.Brush.ProgressBackground`.
The Project Space upload overlay and Run Project indicator share this style.
The upload overlay includes localized information about the free cloud service
and virus scanning; existing circular loaders elsewhere remain unchanged.

## Empty lists and selections

Use a `ContentControl` with `Style="{StaticResource FlowBlox.EmptyState}"` and a localized `Content` string.
The template supplies the grey exclamation icon, font, wrapping and icon/text spacing. The caller retains the
visibility condition and surrounding layout. `FlowBlox.Brush.EmptyStateText` controls the shared color.

Texts remain context-specific: no search results, no entries and no selection are different states, even though
they share the same presentation. Existing localization keys are preserved. `FlowBlox.EmptyStateIconStyle` and
`FlowBlox.EmptyStateTextStyle` remain available for contextual inline hints, such as "Edit in editor", that are
not empty-list states and retain their information icon.

| UI | When visible |
| --- | --- |
| ListFactoryBase: ListViewFactory and DataGridFactory | The edited collection has no items; existing collection-change subscriptions keep visibility current. |
| ListViewSplitModeFactory | No item is selected in the detail pane. |
| PSProjectsWindow | Project search returns no cards. |
| ExtensionsWindow | Extension search returns no results. |
| FieldViewControl | No fields are available. |
| ManagedObjectsViewControl | The displayed managed-object list is empty. |
| TestViewControl | The test-case list is empty. |
| ProblemsViewControl | No problem traces are available. |
| ProblemTraceWindow | The selected trace has no field values. |
| FlowBloxTaskManagementWindow | Task input parameters finished loading and the selected task has none. |
| OptionsWindow | No option is selected in the detail pane. |
| AiAssistantHistoryView | There are no saved histories; retains both its localized heading and explanation. |
| AiAssistantDebugView | Its selected debug view reports `IsEmpty`. |
| DeletionConfirmationWindow | The selected deletion preview has no entries/messages. |
| ProjectExecutionWindow | There are no input parameters or no option overrides, in both the editable launcher and read-only execution view. The option grid reappears when an override is added. |

The project-note bulb uses `FlowBlox.Brush.ProjectNoteIcon` (`#F4B400`), matching the existing Open input directory icon.

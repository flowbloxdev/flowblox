# Field references in property editors

Possible unresolved field references are checked by the FlowBlock after its regular
requirements pass and before execution. Only the current values of its associated
fields are inspected. The FlowBlock collects `GetAssociatedFields()` once per runtime
and reuses those field objects; it never caches their values. Each invocation reports
current matches through `runtime.Report(..., Warning)` without pausing or blocking.
There is no separate runtime-level reference scan or warning deduplication cache.

Field selection defaults to `UISelectionButtons.FLD` on supported scalar business configuration;
no field-only annotation is needed. Read enabled properties with `this.Resolve(x => x.Property)`
before use, preserving configured text and existing specialized SQL/math resolvers. Disable selection
explicitly for identifiers, declarations, flow-logic and UI metadata with
`[FlowBloxUI(UISelectionButtons.None)]`. See [ui-annotations.md](ui-annotations.md)
for the specialized editor attributes and central default resolution.

`FlowBloxFieldSelection.Supports(PropertyInfo)` is the shared eligibility check for UI,
collectors, runtime, legacy import and AI metadata. It requires the enable flag and a supported
type: `string`, `bool`, `int`, `long`, `float`, `double`, `DateTime`, enums or nullable scalars.
The same editors serve grid cells, string comboboxes and passwords; read-only rules still apply.

Strings keep inline placeholders and the existing text replacement behavior. Scalar
properties store one fully qualified field name in `FlowBloxReactiveObject.PropertySelectedFieldMap`.
Selection replaces an existing entry; unlink removes it and preserves the configured
literal. Existing field selection attributes continue to restrict offered choices.
Do not infer restrictions from the field's CLR type: runtime conversions stay flexible.

Consumers resolve scalar selections at the point of use with `Resolve` or `TryResolve`, including FlowBlocks,
owned reactive configuration, providers, modifiers, validation callbacks and lifecycle initialization.
Configured properties retain their literal values throughout execution, including failures. Repeated calls read current field values.
Field collectors, rename tracking, deletion review and AI metadata use the same support rules.

Use `FlowBlox.Core.Extensions` for a single property-resolution API:

```csharp
var timeunit = this.Resolve(x => x.Timeunit);
var baseUrl = this.Resolve(x => x.BaseUrl, required: true);
if (!mapping.TryResolve(x => x.SourceValue, out var sourceValue))
    continue;

if (!this.ResolveOrCreateNotification(
    x => x.FormatExpression,
    out var formatExpression,
    runtime,
    FormatFlowBlockNotifications.FormatExpressionMissingAfterResolving))
    return;
```

- `Resolve` returns a typed scalar or a string with inline placeholders resolved. Strings are never null.
  It preserves configured literals. Name the local after its property in camelCase.
- Empty values are allowed by default. `required: true` throws a localized `ValidationException` on null/blank;
  `TryResolve` returns false instead. Zero and false are valid values. Conversion failures retain existing behavior.
- `ResolveOrCreateNotification` is available only on FlowBlocks. Failure creates the supplied notification, then
  result blocks generate an empty result and other blocks execute their next blocks. The caller must return on false.
  Handle intentionally optional values at the caller before choosing this failure path.
- Low-level `FlowBloxFieldHelper` methods remain appropriate for arbitrary text or validation candidates rather than
  property access. Keep specialized SQL/math evaluation and intentional fallback behavior unchanged.

Project import recognizes old `<Property>_SelectedField` JSON members centrally and
writes their field names to the map, including JSON reference resolution. New exports
contain only the map. No per-FlowBlock compatibility properties or file migration are needed.

The AI update handlers link scalar fields through `/Property/Link` with
`{"resolveFieldElementByFQName":"$Source::Value"}`. `/Property/Unlink` clears the selection
and preserves the configured literal. `/Property` assigns a literal and removes any scalar selection;
it does not accept scalar field resolvers or field-name references. Strings keep inline placeholders.
Use `GetPlaceholders` to discover references; `[FLD]` in `uiFlags` identifies enabled properties.
Selection restrictions remain in the existing compact attributes; toolbox categories remain in `ui.toolboxCategory`.
Shared placeholder, resolver and toolbox usage rules live in central Assistant explanations, not repeated in type/property metadata.

For the AI assistant, `PropertySelectedFieldMap` is a read-only source of truth for
current scalar assignments: each entry maps a property name to a fully qualified
field name. Missing entries use the configured literal; string placeholders remain
inline. Snapshots retain this information. Properties annotated with
`[FlowBloxImmutable]` use the shared immutable-property policy. Both AI metadata
and update handlers read this attribute, without property-name or owner-type rules.
The map is marked `readOnly` in metadata; direct replacement and all child-path
updates through both update handlers, including Link, Unlink and Delete. UI operations,
project import and scalar Link/Unlink operations still maintain the map internally.

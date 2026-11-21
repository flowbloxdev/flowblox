# FieldElement subtypes

Status: open refactoring TODO; the subtype split and new prefixes are not implemented.

## Target model

Keep `FieldElement` as the shared contract for identity, name, value access, field references and common behavior.
Introduce three concrete types, each in its own file:

| Type | Responsibility | Canonical reference |
| --- | --- | --- |
| `RuntimeFieldElement` | FlowBlock result fields, source ownership and runtime values | `$FlowBlockName::FieldName` |
| `MemoryFieldElement` | Project memory, persisted configured values and startup clearing | `$Memory::FieldName` |
| `InputFieldElement` | Project parameters, defaults, passwords, required inputs and value suggestions | `$Input::FieldName` |

Move role-specific properties and annotations into the respective classes; leaving them on the base would retain the current metadata overhead.
Preserve shared runtime value isolation, conditions and modifiers where applicable. Derive the role from the type instead of mutable flags.
This reduces irrelevant UI, JSON and AI metadata and makes responsibilities, readability and extensions clearer.
Actual size savings should be measured after implementation.

## Legacy JSON migration

- Extend the existing pre-deserialization migration pipeline. Inspect legacy field objects using `UserField`, `UserFieldType`
  and their owning context: ordinary/result field → runtime, input → input, memory → memory.
  Handle string/numeric enum representations and missing defaults; diagnose ambiguous or contradictory objects instead of guessing.
- Rewrite only recognized legacy field types. Preserve `$id`/`$ref`, names, field identities, collection element contracts,
  `Source` links and all values. Cover nested fields and both plain arrays and `$values` wrappers.
  Do not replace objects independently after deserialization: that can break shared references.
- A serialization binder alone cannot select a subtype from object properties. Normalize the JSON before materializing it,
  then use the existing binder/contract resolvers. Preserve extension-defined field types.
- Carry over `MigrateInputValue`, password/default rules and local-data handling. Current runtime validation also supports
  required memory fields; preserve that behavior during migration unless explicitly changed separately.
- Migration must be idempotent and run for file, JSON, Project Space/cache and runner import paths.
  `FromJsonContents` now migrates independently of a file path; directory substitution remains file-specific.
  Test copies, snapshots and component import/clone paths separately before making the base abstract.

## Placeholder compatibility

New references use `$Input::Name` and `$Memory::Name`; runtime references retain the FlowBlock prefix.
Keep `$User::Name` as a supported legacy alias: search **both** input and memory sources and resolve the unique matching field.
Never silently prefer one source when both match. Preserve cross-role name uniqueness during the compatibility phase;
report ambiguous legacy aliases and leave unknown references unresolved under the existing warning policy.

Centralize this lookup for string replacement, `GetFieldElementOrNull`, `HasUserField`, scalar `PropertySelectedFieldMap`,
field selection, rename/delete tracking, runner inputs and AI tools. Avoid blanket replacement in arbitrary text.
Reserve `Input`/`Memory` in `FlowBloxSourceIds` and check existing FlowBlock names for collisions before activating the new prefixes.

## Creation, UI and type compatibility

- Make `FlowBloxRegistry.CreateField` and `BaseResultFlowBlock.CreateDestinationResultField` create runtime fields directly.
  `CreateUserField` and `UserFieldObjectManager` factories create the requested input/memory subtype.
- Result declarations such as `TableSelectorMappingEntry.Field` must use a runtime-specific property type or fixed item factory.
  Creating a TableReader output must **not** open a subtype picker. Consuming field-reference properties remain polymorphic.
- Audit assignability and exact-type checks, including `ListViewFactory`, managed-object discovery, supported-type filtering,
  JSON contract resolvers and AI type aliases/metadata. Preserve password redaction when properties move to subclasses.
  Do not confuse the field role with its value's `FieldTypes`; existing scalar conversions remain flexible.
- Initially retain obsolete constructors/factory adapters for extension compatibility. Make the base abstract and remove legacy flags
  only in a deliberate compatibility-breaking phase, after all callers and extension contracts have been checked.

## Implementation order and acceptance

1. Introduce subtypes, fixed factories and JSON migration while retaining compatible base APIs.
2. Add canonical prefixes and centralized `$User` fallback; update editors and metadata.
3. Move role-specific members and retire obsolete APIs after compatibility review.

Verify legacy/new JSON round trips with shared references, all import paths, alias resolution for both roles and collisions,
defaults/password redaction, memory lifecycle, runtime isolation, AI/runner integration and direct TableReader field creation.
Open migrated distributed/example projects in the active desktop host and verify save/reload before any bulk conversion.

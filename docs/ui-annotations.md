# Property UI annotations

Properties participate in generated property views through `DisplayAttribute`. UI settings alone do not make a property visible.

## Responsibilities

| Attribute | Responsibility |
| --- | --- |
| `FlowBloxUI` | Visibility, labels, read-only state, maximization, toolbox category and optional selection buttons |
| `FlowBloxAssociation` | Object operations, creatable concrete types and the item factory for individual values or collection elements |
| `FlowBloxItemsSource` | Selectable entries (`Method`), their display member and the combo box refresh dependency |
| `FlowBloxComboBox` | Selects the combo box editor; configures suggestions and value mapping |
| `FlowBloxListView` | Selects the list editor; `SplitMode` embeds the selected item's editor beside the list |
| `FlowBloxDataGrid` | Selects the grid editor |
| `FlowBloxTextBox` | Configures text, code and password editors |
| `FlowBloxFieldSelection` | Configures field-reference choices and required-value behavior |

ListView and DataGrid inherit `ColumnMemberNames` and `IsMovable` from `FlowBloxCollectionViewAttribute`.
Empty-column behavior is editor-specific: grids show eligible properties; list views show only the item icon.
Grid column order follows `ColumnMemberNames`; list views use reflected property order.
`IsMovable` allows manual collection reordering and disables grid column sorting.

## Defaults and resolution

Use the extensions in `FlowBlox.Core.Extensions`:

```csharp
var ui = property.GetFlowBloxUIAttribute();
var factory = property.GetFlowBloxUIFactory();
var association = property.GetFlowBloxAssociationAttribute();
var itemsSource = property.GetFlowBloxItemsSourceAttribute();
```

The attribute getters return the declared attribute or a fresh default instance. Attribute initializers are the source of truth
for defaults. Do not independently reconstruct defaults in UI, runtime or AI consumers, or cache a shared mutable fallback attribute.

The factory is inferred from annotations: DataGrid, ListView (including split mode), ComboBox, Association, then Default.
Collection presentation takes precedence over association settings because collections also need object operations and item factories.
Do not combine conflicting editor annotations. An `ItemsSource` alone does not select an editor.
Ordinary enums need no ComboBox annotation; explicitly annotate them only when custom combo box behavior is needed.

`FlowBloxUI.SelectionButtons` defaults to `UISelectionButtons.FLD`. Field references remain limited to supported writable scalar
properties through `FlowBloxFieldSelection.Supports(PropertyInfo)`. Object associations and collections are excluded by that type check.
Explicit button flags replace the default:

```csharp
[FlowBloxUI(UISelectionButtons.FLD | UISelectionButtons.FL)]
public string FilePath { get; set; }

[FlowBloxUI(UISelectionButtons.None)]
public string DeclarationName { get; set; }
```

Omit redundant `FlowBloxUI` attributes. Preserve literal-only declarations, identifiers, metadata and flow-control settings with `None`.
New runtime-valued business configuration must still resolve values at consumption time as described in [field-references.md](field-references.md).

## Associations and collections

```csharp
[FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
[FlowBloxItemsSource(Method = nameof(GetConnections), DisplayMember = nameof(Connection.Name))]
public Connection Connection { get; set; }

[FlowBloxDataGrid(ColumnMemberNames = [nameof(Row.Name), nameof(Row.Value)], IsMovable = true)]
[FlowBloxAssociation(ItemFactory = typeof(RowFactory))]
public ObservableCollection<Row> Rows { get; set; }
```

Object operations default to `UIOperations.All`. A collection does not need an Association annotation unless its operations,
creatable types or item factory differ from the default. Item factories retain the `IItemFactory<T>` contract.

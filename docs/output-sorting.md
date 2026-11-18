# Output sorting

`BaseResultFlowBlock.OutputSorting` maintains one row per own output field. Rows synchronize when fields are added,
replaced or removed; existing priorities/directions survive. A new row defaults to `None`, preserving the supplied order.
Move rows to choose primary and secondary keys; enable `Ascending` or `Descending` as needed.

Sorting runs after output selection, modifiers and duplicate filtering, before dataset flushing. It is stable for equal
keys and uses each field's `TypeElement` configuration, including date formats and decimal/group separators.
Nulls sort before values ascending and after values descending; strings use ordinal comparison and byte arrays compare
lexicographically. Current field values are not overwritten while sorting. Field-bound `FieldElement.Value` is unchanged.

Sorting-field associations follow the same informational deletion-review treatment as input-behavior associations.
The AI Assistant edits existing rows through ordinary indexed paths such as `/OutputSorting/0/Direction` after locating
the field in the component snapshot. No special update path or manual association creation is needed.

IMAP uses the general output sorting instead of `NewestFirst`, without a legacy import path. New IMAP blocks have no
enabled sorting by default. `MaxResults` limits the retrieved messages; general sorting orders that returned set.
The optional `ExcludeQuotedHistory` uses heuristics for common English/German reply formats. Original content is available
through the `OriginalMessage` result destination; unusual formats can remain and quotations can contain relevant content.

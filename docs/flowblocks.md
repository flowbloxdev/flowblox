# FlowBlocks and execution

[Back to the README](../README.md#how-flowblocks-work)

## FlowBlocks

A **FlowBlock** is the basic executable unit in FlowBlox. Each block encapsulates an operation, such as reading table data, formatting text, calling web endpoints, or writing output.

FlowBlocks can define:

- Activation conditions.
- Required fields.
- Structured inputs and outputs, depending on block type.

## Dataset-driven execution

Flow execution is dataset-driven:

- If a source FlowBlock produces result datasets, each direct successor is invoked once per dataset.
- If the source produces no result datasets, successors are invoked once.
- Successors run in creation order unless an explicit execution index is set (starting at `0`).

Set an index through **Right-click FlowBlock → Set index**.

## Datasets and field propagation

Result FlowBlocks produce one or more datasets. Each dataset contains field values defined by the producing block.

For example, a `TableReader` with `FirstName`, `LastName`, and `Salutation` mappings produces a dataset for each input row. Successors receive these datasets one at a time.

Field values can be consumed through direct field references or fully qualified field names in text properties. Field propagation stays unambiguous along the invocation path, including deeper successors.

## Result block variants

| Pattern | Behavior |
| --- | --- |
| Result FlowBlock | Produces N results |
| SingleResult FlowBlock | Produces one result |
| Pipe FlowBlock | Transforms one input into one result |

## Multi-input combination

When multiple input FlowBlocks connect to a successor, FlowBlox combines datasets according to the configured input behavior. The default is **FIRST** (the first dataset from each incoming source). Use **CROSS** when a combinational Cartesian-product behavior is required. The successor is invoked once per resulting combined dataset.

Configure behavior for each incoming source under **Edit FlowBlock → Input**. Supported approaches enable scenarios such as taking the first dataset from source A, combining rows from sources B and C, or selecting the last dataset from source D.

## Iteration context

In multi-input scenarios, the iteration context determines when dataset combination and target invocation occur.

By default, FlowBlox resolves the context to the **first common upstream FlowBlock** across the incoming paths. Override it under **Input → IterationContext**.

The target FlowBlock executes at the end of **each individual iteration** of its iteration-context FlowBlock, after the connected input paths have finished processing the current dataset. It combines the input values according to the configured input behaviors and processes the resulting datasets before the next iteration begins. For example: process page 1 → extract its title, description, and author → run TableWriter; then repeat for page 2.

## Flow design patterns

**Sequential chain:** Connect dependent operations in a downstream sequence when each step needs the previous step's output.

**Stack pattern:** Connect several independent next FlowBlocks to a shared source to organize prerequisites or preparation tasks. This can improve readability and avoid excessively long horizontal chains.


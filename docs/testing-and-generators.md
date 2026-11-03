# Testing and generators

[Back to the README](../README.md#testing-and-generators)

For illustrated introductions, see [AI Property Value Generation](../README.md#ai-property-value-generation) and [Sequence Detection Generation](../README.md#sequence-detection-generation).

## Tests

Open **Edit FlowBlock → Tests** to:

- Create or link test cases.
- Define expectation conditions on target FlowBlocks.
- Provide predecessor field values manually.
- Combine manual test setup with automatic value selection.

Tests support example-driven, repeatable validation of FlowBlock behavior.

## Generator strategies

| Strategy | Purpose | Status |
| --- | --- | --- |
| `AIPropertyValueGenerationStrategy` | Generate property values from examples, property context, and test expectations; e.g. a regex for extracting company names from DOM content | Available |
| `SequenceDetectionGenerationStrategy` | Learn extraction patterns from at least two similarly structured inputs, expected values, and match counts; no hand-written selectors required | Available, non-AI/offline |
| `OnnxPropertyValueGenerationStrategy` | ONNX-based property value generation | In progress |

**Required-for-runtime-execution tests** can act as a guard:

1. Before execution, check the status of a generator-relevant required test.
2. If the test fails, run the generator.
3. Validate again after generation.
4. Continue only if the test passes; otherwise abort runtime execution.

This guards the configured test expectations. It does not guarantee correct results for inputs outside the tested cases.


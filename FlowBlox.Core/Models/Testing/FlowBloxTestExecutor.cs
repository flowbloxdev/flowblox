using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Models.Testing
{
    public class FlowBloxTestExecutor
    {
        private TransientRuntime _localRuntime;
        private readonly FlowBloxRegistry _registry;

        private FlowBloxTestDefinition _testDefinition;
        private List<BaseFlowBlock> _capturedFlowBlocks;

        public event EventHandler<TestExpectationConditionFailedEventArgs>? ExpectationConditionFailed;

        public FlowBloxTestExecutor()
        {
            this._registry = FlowBloxRegistryProvider.GetRegistry();
        }

        private readonly FlowBloxTestDefinitionLatestFlowBlockResolver _latestResolver = new FlowBloxTestDefinitionLatestFlowBlockResolver();

        public void Initialize(
            FlowBloxTestDefinition testDefinition,
            BaseFlowBlock currentFlowBlock,
            IEnumerable<BaseFlowBlock>? includedFlowBlocks = null)
        {
            _testDefinition = testDefinition;

            FlowBloxTestCapture flowBloxCapture = new FlowBloxTestCapture();
            var effectiveTargetFlowBlock = currentFlowBlock ?? _latestResolver.ResolveLatestFlowBlock(_testDefinition);
            flowBloxCapture.CreateCapture(_registry.GetStartFlowBlock(), effectiveTargetFlowBlock);
            _capturedFlowBlocks = flowBloxCapture.GetCapturedFlowBlocks();

            InitializeTransientRuntime(includedFlowBlocks);
        }

        public void Shutdown()
        {
            _localRuntime.ShutdownRuntime(_capturedFlowBlocks);
        }

        private void InitializeTransientRuntime(IEnumerable<BaseFlowBlock>? includedFlowBlocks)
        {
            _localRuntime = new TransientRuntime(FlowBloxProjectManager.Instance.ActiveProject);
            _localRuntime.IncludedFlowBlocks = includedFlowBlocks?
                .Where(x => x != null)
                .Distinct()
                .ToList() ?? new List<BaseFlowBlock>();
            _localRuntime.InitializeRuntime(_capturedFlowBlocks);
        }

        public Task<FlowBloxTestResult> ExecuteTestAsync()
        {
            return Task.Run(ExecuteTest);
        }

        public FlowBloxTestResult ExecuteTest()
        {
            if (_localRuntime == null ||
                _testDefinition == null ||
                _capturedFlowBlocks == null)
            {
                throw new InvalidOperationException($"Please call \"{nameof(Initialize)}\" before calling \"{nameof(ExecuteTest)}.");
            }

            var userFields = _registry.GetUserFields();
            foreach (var userField in userFields)
                userField.ReleaseAllRuntimeValues();

            try
            {
                return ExecuteTestCore();
            }
            finally
            {
                foreach (var userField in userFields)
                    userField.ReleaseRuntimeValue(_localRuntime);
            }
        }

        private FlowBloxTestResult ExecuteTestCore()
        {
            var fieldValueAssignments = new Dictionary<string, string>();

            if (!ValidateAssociatedFlowBlockExecution())
                return new FlowBloxTestResult(false, fieldValueAssignments);

            var entryOfUserFields = _testDefinition.Entries.SingleOrDefault(x => x.FlowBlock == null);
            if (entryOfUserFields != null)
            {
                foreach (var userFieldTestConfiguration in entryOfUserFields.FlowBloxTestConfigurations)
                {
                    var field = userFieldTestConfiguration.FieldElement;
                    if (userFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput)
                    {
                        var fieldValue = userFieldTestConfiguration.UserInput;
                        field.SetValue(_localRuntime, fieldValue, true);
                        fieldValueAssignments[field.FullyQualifiedName] = fieldValue;
                    }
                    else if (userFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.Keep)
                    {
                        fieldValueAssignments[field.FullyQualifiedName] = userFieldTestConfiguration.FieldElement.StringValue;
                    }
                    else
                    {
                        fieldValueAssignments[field.FullyQualifiedName] = string.Empty;
                    }
                }
            }

            // TransientRuntime defers this check until the test's input assignments
            // are applied, so validation sees the effective runtime-bound values.
            _localRuntime.ValidateUserFieldsAtRuntimeStartup();

            foreach (var capturedFlowBlock in _capturedFlowBlocks)
            {
                var testDataset = _testDefinition.Entries.SingleOrDefault(x => x.FlowBlock == capturedFlowBlock);
                if (testDataset == null)
                {
                    _localRuntime.Report($"Test case: No test configuration was found for the FlowBlock \"{capturedFlowBlock.Name}\".");
                    continue;
                }

                var flowBloxTestConfigurations = testDataset.FlowBloxTestConfigurations;

                if (testDataset.Execute)
                {
                    if (!capturedFlowBlock.ValidateRequirements(out List<string> messages))
                    {
                        messages.ForEach(x => _localRuntime.Report($"Test case: {x}", FlowBloxLogLevel.Warning));
                    }

                    if (!capturedFlowBlock.Execute(_localRuntime, null))
                    {
                        _localRuntime.Report($"Test case: Execution of \"{capturedFlowBlock.Name}\" has failed.", FlowBloxLogLevel.Warning);
                    }
                }

                if (capturedFlowBlock is BaseResultFlowBlock)
                {
                    var resultFlowBlock = ((BaseResultFlowBlock)capturedFlowBlock);
                    foreach (var field in resultFlowBlock.Fields)
                    {
                        var FlowBloxFieldTestConfiguration = flowBloxTestConfigurations.SingleOrDefault(x => x.FieldElement == field);
                        if (FlowBloxFieldTestConfiguration == null)
                        {
                            _localRuntime.Report($"Test case: No field configuration was found for the field \"{field.FullyQualifiedName}\".", FlowBloxLogLevel.Warning);
                            continue;
                        }

                        ReportExpectedValueConfiguredButNotExecuted(testDataset, FlowBloxFieldTestConfiguration);
                        ReportMissingExpectedValueForExecutedDataset(testDataset, FlowBloxFieldTestConfiguration);

                        if (testDataset.Execute)
                        {
                            var fieldValues = resultFlowBlock.GridElementResult.Results
                                .SelectMany(x => x.FieldValueMappings)
                                .Where(x => x.Field == field)
                                .Select(x => x.Value)
                                .ToList();

                            if (!EvaluateExpectationConditions(field, FlowBloxFieldTestConfiguration, fieldValues, out var failedCondition))
                            {
                                var eventArgs = new TestExpectationConditionFailedEventArgs(
                                    _testDefinition,
                                    capturedFlowBlock,
                                    field,
                                    FlowBloxFieldTestConfiguration,
                                    failedCondition!,
                                    _localRuntime,
                                    new FlowBloxTestResult(false, new Dictionary<string, string>(fieldValueAssignments)));

                                ExpectationConditionFailed?.Invoke(this, eventArgs);
                                if (!eventArgs.RepeatLatestExecution)
                                    return new FlowBloxTestResult(false, fieldValueAssignments);

                                _localRuntime.Report(
                                    $"Test case: Repeating latest execution for \"{capturedFlowBlock.Name}\" after regeneration hook.",
                                    FlowBloxLogLevel.Info);

                                if (testDataset.Execute)
                                {
                                    if (!capturedFlowBlock.ValidateRequirements(out List<string> repeatMessages))
                                        repeatMessages.ForEach(x => _localRuntime.Report($"Test case: {x}", FlowBloxLogLevel.Warning));

                                    if (!capturedFlowBlock.Execute(_localRuntime, null))
                                        _localRuntime.Report($"Test case: Re-execution of \"{capturedFlowBlock.Name}\" has failed.", FlowBloxLogLevel.Warning);
                                }

                                fieldValues = resultFlowBlock.GridElementResult.Results
                                    .SelectMany(x => x.FieldValueMappings)
                                    .Where(x => x.Field == field)
                                    .Select(x => x.Value)
                                    .ToList();

                                if (!EvaluateExpectationConditions(field, FlowBloxFieldTestConfiguration, fieldValues, out _))
                                    return new FlowBloxTestResult(false, fieldValueAssignments);
                            }

                            if (FlowBloxFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.First)
                            {
                                var fieldValue = fieldValues.FirstOrDefault();
                                field.SetValue(_localRuntime, fieldValue);
                                fieldValueAssignments[field.FullyQualifiedName] = fieldValue;
                            }

                            if (FlowBloxFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.Index)
                            {
                                if (!FlowBloxFieldTestConfiguration.Index.HasValue)
                                {
                                    _localRuntime.Report($"Test case: No index was defined in the test data for field \"{field.FullyQualifiedName}\".", FlowBloxLogLevel.Error);
                                    new FlowBloxTestResult(false, fieldValueAssignments);
                                }

                                var fieldValue = fieldValues.ElementAtOrDefault(new Index(FlowBloxFieldTestConfiguration.Index.Value));
                                field.SetValue(_localRuntime, fieldValue);
                                fieldValueAssignments[field.FullyQualifiedName] = fieldValue;
                            }


                            if (FlowBloxFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.Last)
                            {
                                var fieldValue = fieldValues.LastOrDefault();
                                field.SetValue(_localRuntime, fieldValue);
                                fieldValueAssignments[field.FullyQualifiedName] = fieldValue;
                            }   

                            if (FlowBloxFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue)
                            {
                                if (!string.IsNullOrWhiteSpace(FlowBloxFieldTestConfiguration.UserInput))
                                {
                                    var fieldValue = FlowBloxFieldTestConfiguration.UserInput;
                                    field.SetValue(_localRuntime, fieldValue);
                                    fieldValueAssignments[field.FullyQualifiedName] = fieldValue;
                                }
                            }
                        }

                        var userInput = FlowBloxFieldTestConfiguration.UserInput;
                        if (!string.IsNullOrEmpty(userInput) && 
                            FlowBloxFieldTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput)
                        {
                            var fieldValue = userInput;
                            field.SetValue(_localRuntime, fieldValue);
                            fieldValueAssignments[field.FullyQualifiedName] = fieldValue;
                        }
                    }
                }
            }

            _localRuntime.Report($"The test \"{_testDefinition.Name}\" was completed successfully.");

            return new FlowBloxTestResult(true, fieldValueAssignments);
        }

        private bool ValidateAssociatedFlowBlockExecution()
        {
            var isValid = true;
            var entries = _testDefinition.Entries?.ToList() ?? new List<FlowBlockTestDataset>();
            var entriesByFlowBlock = entries
                .Where(x => x.FlowBlock != null)
                .GroupBy(x => x.FlowBlock)
                .ToDictionary(x => x.Key, x => x.First());

            foreach (var executedDataset in entries.Where(x => x.Execute && x.FlowBlock != null))
            {
                foreach (var associatedFlowBlock in FlowBloxTestDefinition
                    .ResolveAssociatedFlowBlocks(executedDataset.FlowBlock)
                    .Distinct())
                {
                    if (entriesByFlowBlock.TryGetValue(associatedFlowBlock, out var associatedDataset) &&
                        associatedDataset.Execute)
                        continue;

                    _localRuntime.Report(
                        $"Test case: FlowBlock \"{executedDataset.FlowBlock.Name}\" is executed, but its associated FlowBlock \"{associatedFlowBlock.Name}\" is not executed.",
                        FlowBloxLogLevel.Error);
                    isValid = false;
                }
            }

            return isValid;
        }

        private void ReportExpectedValueConfiguredButNotExecuted(
            FlowBlockTestDataset testDataset,
            FlowBloxFieldTestConfiguration fieldTestConfiguration)
        {
            if (testDataset.Execute || testDataset.FlowBlock == null)
                return;

            var selectionMode = fieldTestConfiguration.SelectionMode;
            var hasExpectedValueSelectionMode = selectionMode == FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue;
            var hasExpectedValue = !string.IsNullOrWhiteSpace(fieldTestConfiguration.UserInput);

            if (!hasExpectedValueSelectionMode || !hasExpectedValue)
                return;

            var fieldName = fieldTestConfiguration.FieldElement?.FullyQualifiedName;
            var flowBlockName = testDataset.FlowBlock.Name;

            _localRuntime.Report(
                $"Test case: Warning - an expected value is configured for field \"{fieldName}\", but flow block \"{flowBlockName}\" is not executed.",
                FlowBloxLogLevel.Warning);
        }

        private void ReportMissingExpectedValueForExecutedDataset(
            FlowBlockTestDataset testDataset,
            FlowBloxFieldTestConfiguration fieldTestConfiguration)
        {
            if (!testDataset.Execute || testDataset.FlowBlock == null)
                return;

            if (fieldTestConfiguration.SelectionMode != FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue)
                return;

            if (!string.IsNullOrWhiteSpace(fieldTestConfiguration.UserInput))
                return;

            var hasTestExpectations = fieldTestConfiguration.ExpectationConditions?.Any() == true;
            if (hasTestExpectations)
                return;

            var fieldName = fieldTestConfiguration.FieldElement?.FullyQualifiedName ?? string.Empty;
            var flowBlockName = testDataset.FlowBlock.Name;

            _localRuntime.Report(
                $"Test case: Warning - flow block \"{flowBlockName}\" is executed with selection mode \"{FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue}\", but no expected value and no test expectations are configured for field \"{fieldName}\". No result is selected or fixed by expected value, and no expectation validation is defined.",
                FlowBloxLogLevel.Warning);
        }

        private bool EvaluateExpectationConditions(
            FieldElement field,
            FlowBloxFieldTestConfiguration flowBloxTestConfiguration,
            IEnumerable<string> fieldValues,
            out ExpectationCondition? failedCondition)
        {
            failedCondition = null;
            var fieldValueList = fieldValues?.ToList() ?? new List<string>();
            var hasExpectedValue = flowBloxTestConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue &&
                !string.IsNullOrWhiteSpace(flowBloxTestConfiguration.UserInput);
            var hasExpectationConditions = flowBloxTestConfiguration.ExpectationConditions?.Any() == true;

            if (!hasExpectedValue && !hasExpectationConditions)
            {
                _localRuntime.Report($"Test case: No test expectations defined for the field \"{field.FullyQualifiedName}\".", FlowBloxLogLevel.Info);
                return true;
            }

            var expectationConditions = (flowBloxTestConfiguration.ExpectationConditions ?? Enumerable.Empty<ExpectationCondition>())
                .ToList();

            if (hasExpectedValue)
                expectationConditions.Add(CreateExpectedValueCondition(flowBloxTestConfiguration.UserInput));

            var conditionsNotMetCount = 0;

            foreach (var expectationCondition in expectationConditions)
            {
                if (IsExpectationConditionMet(expectationCondition, fieldValueList))
                    continue;

                _localRuntime.Report($"Test case: Expectation condition \"{expectationCondition.DisplayName}\" failed for the field \"{field.FullyQualifiedName}\".", FlowBloxLogLevel.Error);
                failedCondition ??= expectationCondition;
                conditionsNotMetCount++;
            }

            if (conditionsNotMetCount > 0)
            {
                _localRuntime.Report(
                    $"Test case: {conditionsNotMetCount} expectation condition(s) were not met for the field \"{field.FullyQualifiedName}\".",
                    FlowBloxLogLevel.Error);
                return false;
            }

            _localRuntime.Report($"Test case: All test expectations met for the field \"{field.FullyQualifiedName}\".", FlowBloxLogLevel.Success);
            return true;
        }

        private static bool IsExpectationConditionMet(
            ExpectationCondition expectationCondition,
            IReadOnlyList<string> fieldValues)
        {
            switch (expectationCondition.ExpectationConditionTarget)
            {
                case ExpectationConditionTarget.FirstValue:
                    {
                        string value = fieldValues.FirstOrDefault();
                        return expectationCondition.Compare(value);
                    }
                case ExpectationConditionTarget.AnyValue:
                    {
                        return fieldValues.Any(value => expectationCondition.Compare(value));
                    }
                case ExpectationConditionTarget.LastValue:
                    {
                        string value = fieldValues.LastOrDefault();
                        return expectationCondition.Compare(value);
                    }
                case ExpectationConditionTarget.ValueAtIndex:
                    {
                        string value = fieldValues.ElementAtOrDefault(expectationCondition.Index);
                        return expectationCondition.Compare(value);
                    }
                case ExpectationConditionTarget.NumberOfDatasets:
                    {
                        int count = fieldValues.Count;
                        return expectationCondition.Compare(count);
                    }
                default:
                    return false;
            }
        }

        private static ExpectationCondition CreateExpectedValueCondition(string expectedValue)
        {
            return new ExpectationCondition
            {
                ExpectationConditionTarget = ExpectationConditionTarget.AnyValue,
                Operator = ComparisonOperator.Equals,
                Value = expectedValue
            };
        }

        public BaseRuntime GetRuntime() => _localRuntime;
    }
}

using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Models.FlowBlocks.Base
{
    [Serializable()]
    [FlowBloxUIGroup("BaseResultFlowBlock_Groups_Output", 25)]
    public abstract class BaseResultFlowBlock : BaseFlowBlock
    {
        public delegate void OutputDatasetProcessingChangedEventHandler(BaseRuntime runtime);
        public event OutputDatasetProcessingChangedEventHandler OutputDatasetProcessingChanged;

        [Display(Name = "BaseResultFlowBlock_IgnoreDuplicates", Description = "BaseResultFlowBlock_IgnoreDuplicates_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseResultFlowBlock_Groups_Output", Order = -1)]
        public bool OutputIgnoreDuplicates { get; set; }

        [Display(Name = "BaseResultFlowBlock_OutputBehavior", Description = "BaseResultFlowBlock_OutputBehavior_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseResultFlowBlock_Groups_Output", Order = 0)]
        [CustomValidation(typeof(BaseResultFlowBlock), nameof(ValidateOutputBehavior))]
        public OutputBehavior OutputBehavior { get; set; }

        private string _startValue;

        [Display(Name = "BaseResultFlowBlock_StartValue", Description = "BaseResultFlowBlock_StartValue_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseResultFlowBlock_Groups_Output", Order = 0)]
        [ActivationCondition(ActivationMethod = nameof(IsStartValueActive))]
        [DependsOnProperty(MemberNames = [nameof(OutputBehavior), nameof(StartValue), nameof(StartIndex), nameof(EndIndex)])]
        [FlowBloxUI(Factory = UIFactory.Default)]
        [FlowBloxFieldSelection(DefaultRequiredValue = false)]
        [FlowBloxTextBox(MultiLine = true)]
        public string StartValue
        {
            get => _startValue;
            set
            {
                if (_startValue == value)
                    return;

                _startValue = value;

                if (!string.IsNullOrEmpty(_startValue) && OutputBehavior != OutputBehavior.Range)
                {
                    OutputBehavior = OutputBehavior.Range;
                    OnPropertyChanged(nameof(OutputBehavior));
                }

                OnPropertyChanged();
            }
        }

        private int? _startIndex;

        [Display(Name = "BaseResultFlowBlock_StartIndex", Description = "BaseResultFlowBlock_StartIndex_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseResultFlowBlock_Groups_Output", Order = 1)]
        [ActivationCondition(ActivationMethod = nameof(AreRangeIndicesActive))]
        [DependsOnProperty(MemberNames = [nameof(OutputBehavior), nameof(StartValue), nameof(StartIndex), nameof(EndIndex)])]
        [Range(0, int.MaxValue)]
        public int? StartIndex
        {
            get => _startIndex;
            set
            {
                if (_startIndex == value)
                    return;

                _startIndex = value;
                
                if (_startIndex != null && OutputBehavior != OutputBehavior.Range)
                {
                    OutputBehavior = OutputBehavior.Range;
                    OnPropertyChanged(nameof(OutputBehavior));
                }

                OnPropertyChanged();
            }
        }

        private int? _endIndex;

        [Display(Name = "BaseResultFlowBlock_EndIndex", Description = "BaseResultFlowBlock_EndIndex_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseResultFlowBlock_Groups_Output", Order = 2)]
        [ActivationCondition(ActivationMethod = nameof(AreRangeIndicesActive))]
        [DependsOnProperty(MemberNames = [nameof(OutputBehavior), nameof(StartValue), nameof(StartIndex), nameof(EndIndex)])]
        [Range(0, int.MaxValue)]
        public int? EndIndex
        {
            get => _endIndex;
            set
            {
                if (_endIndex == value)
                    return;

                _endIndex = value;

                if (_endIndex != null && OutputBehavior != OutputBehavior.Range)
                {
                    OutputBehavior = OutputBehavior.Range;
                    OnPropertyChanged(nameof(OutputBehavior));
                }

                OnPropertyChanged();
            }
        }

        public static ValidationResult ValidateOutputBehavior(OutputBehavior outputBehavior, ValidationContext context)
        {
            var instance = context.ObjectInstance as BaseResultFlowBlock;
            if (outputBehavior == OutputBehavior.Range &&
                instance != null &&
                string.IsNullOrWhiteSpace(instance.StartValue) &&
                !instance.StartIndex.HasValue &&
                !instance.EndIndex.HasValue)
            {
                return new ValidationResult(
                    FlowBloxResourceUtil.GetLocalizedString(
                        "BaseResultFlowBlock_Validation_RangeCriterionRequired",
                        typeof(FlowBloxTexts)),
                    [context.MemberName]);
            }

            if (outputBehavior == OutputBehavior.Range &&
                instance != null &&
                !string.IsNullOrWhiteSpace(instance.StartValue) &&
                (instance.StartIndex.HasValue || instance.EndIndex.HasValue))
            {
                return new ValidationResult(
                    FlowBloxResourceUtil.GetLocalizedString(
                        "BaseResultFlowBlock_Validation_RangeModesMutuallyExclusive",
                        typeof(FlowBloxTexts)),
                    [context.MemberName]);
            }

            if (outputBehavior == OutputBehavior.Range &&
                instance?.StartIndex.HasValue == true &&
                instance.EndIndex.HasValue &&
                instance.StartIndex.Value >= instance.EndIndex.Value)
            {
                return new ValidationResult(
                    FlowBloxResourceUtil.GetLocalizedString(
                        "BaseResultFlowBlock_Validation_StartIndexMustPrecedeEndIndex",
                        typeof(FlowBloxTexts)),
                    [context.MemberName]);
            }

            return ValidationResult.Success;
        }

        protected bool IsStartValueActive()
        {
            var hasStartValue = !string.IsNullOrWhiteSpace(StartValue);
            var hasIndex = StartIndex.HasValue || EndIndex.HasValue;
            return OutputBehavior == OutputBehavior.Range && (!hasIndex || hasStartValue);
        }

        protected bool AreRangeIndicesActive()
        {
            var hasStartValue = !string.IsNullOrWhiteSpace(StartValue);
            var hasIndex = StartIndex.HasValue || EndIndex.HasValue;
            return OutputBehavior == OutputBehavior.Range && (!hasStartValue || hasIndex);
        }

        public abstract List<FieldElement> Fields { get; }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(BaseResultFlowBlockNotifications));
                return notificationTypes;
            }
        }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public FlowBlockOut GridElementResult { get; protected set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public FlowBlockOutDataset OutputDataset_CurrentlyProcessing { get; internal set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public int OutputDataset_CurrentlyProcessingIndex
        {
            get
            {
                var datasets = GridElementResult?.Results;
                if (datasets == null || OutputDataset_CurrentlyProcessing == null)
                    return 0;

                var index = datasets.IndexOf(OutputDataset_CurrentlyProcessing);
                return index >= 0 ? index + 1 : 0;
            }
        }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public int OutputDatasets_Count => GridElementResult?.Results?.Count ?? 0;

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public bool OutputDatasetProcessingCompleted { get; private set; }

        public override List<FieldElement> GetPossibleFieldElements()
        {
            return FlowBloxFieldsResolver.GetFieldsOrderedByReferencedFlowBlocksExcluding(this);
        }

        public override List<IManagedObject> DefinedManagedObjects
        {
            get
            {
                var definedManagedObjects = base.DefinedManagedObjects;
                definedManagedObjects.AddRange(this.Fields);
                return definedManagedObjects;
            }
        }

        protected void CreateDestinationResultField<TEnum>(
            ICollection<ResultFieldByEnumValue<TEnum>> resultFields,
            TEnum destination,
            FieldTypes fieldType = FieldTypes.Text)
            where TEnum : struct, Enum
        {
            if (resultFields == null)
                throw new ArgumentNullException(nameof(resultFields));

            var field = FlowBloxRegistryProvider.GetRegistry().CreateField(this);
            field.Name = destination.ToString();

            if (field.FieldType != null)
                field.FieldType.FieldType = fieldType;

            resultFields.Add(new ResultFieldByEnumValue<TEnum>
            {
                EnumValue = destination,
                ResultField = field
            });
        }

        internal override void CompleteFailedResolution(BaseRuntime runtime) => GenerateResult(runtime);

        protected void GenerateResult(BaseRuntime runtime, string content)
        {
            var field = this.Fields.Single();
            GenerateResult(runtime, new List<Dictionary<FieldElement, string>>()
            {
                new Dictionary<FieldElement, string>()
                {
                    { field, content }
                }
            });
        }

        protected void GenerateResult(BaseRuntime runtime, IEnumerable<string> contents)
        {
            var field = this.Fields.Single();
            var resultMap = new List<Dictionary<FieldElement, string>>();
            contents.ToList().ForEach(x => resultMap.Add(new Dictionary<FieldElement, string>() { { field, x } }));
            GenerateResult(runtime, resultMap);
        }

        private void AdjustedResultMap(BaseRuntime runtime, IEnumerable<Dictionary<FieldElement, string>> resultMap)
        {
            if (resultMap == null)
                return;

            foreach (var record in resultMap)
            {
                foreach (var fieldElement in record.Keys.ToList())
                {
                    var fieldValue = record[fieldElement];
                    if (fieldValue == null)
                        continue;

                    // Process modifiers
                    foreach (var modifierElement in fieldElement.Modifiers)
                    {
                        var modifiedValue = modifierElement.Modify(runtime, fieldValue);
                        if (modifiedValue != fieldValue)
                        {
                            var shortenedFieldValue = FieldElement.GetShortStringValue(fieldValue);
                            var shortenedModifiedValue = FieldElement.GetShortStringValue(modifiedValue);
                            runtime.Report($"Modified value from \"{shortenedFieldValue}\" to \"{shortenedModifiedValue}\" (at \"{fieldElement.FullyQualifiedName}\").");
                            record[fieldElement] = modifiedValue;
                            fieldValue = modifiedValue;
                        }
                    }

                    // Process conditions
                    var invalidDueToCondition = fieldElement.Conditions.FirstOrDefault(condition => !condition.Compare(fieldValue));
                    if (invalidDueToCondition != null)
                    {
                        runtime.Report($"Validation failed for \"{fieldElement.ShortStringValue}\": does not meet \"{invalidDueToCondition.DisplayName}\" (at \"{fieldElement.FullyQualifiedName}\").");
                        record[fieldElement] = null;
                        continue;
                    }
                }
            }
        }

        private IEnumerable<Dictionary<FieldElement, string>> GetFilteredResultMap(IEnumerable<Dictionary<FieldElement, string>> resultMap)
        {
            if (!OutputIgnoreDuplicates || resultMap == null)
                return resultMap;

            var uniqueResultMaps = new List<Dictionary<FieldElement, string>>();
            var alreadyProcessedValues = new HashSet<string>();

            foreach (var record in resultMap)
            {
                var recordValues = string.Join("|", record.OrderBy(x => x.Key).Select(x => x.Value));
                if (alreadyProcessedValues.Contains(recordValues))
                    continue;

                alreadyProcessedValues.Add(recordValues);
                uniqueResultMaps.Add(record);
            }

            return uniqueResultMaps;
        }

        private Dictionary<FieldElement, string> GetPrecedingFieldValues(BaseFlowBlock baseFlowBlock)
        {
            var result = new Dictionary<FieldElement, string>();
            foreach (var previousFlowBlock in baseFlowBlock.ReferencedFlowBlocks.OfType<BaseResultFlowBlock>())
            {
                foreach (var kv in GetPrecedingFieldValues(previousFlowBlock))
                    result.TryAdd(kv.Key, kv.Value);

                foreach (var field in previousFlowBlock.Fields)
                    result.TryAdd(field, field.StringValue);
            }
            return result;
        }

        protected void GenerateResult(BaseRuntime runtime, IEnumerable<Dictionary<FieldElement, string>> resultMap = null)
        {
            ApplyOutputBehavior(runtime, ref resultMap);
            AdjustedResultMap(runtime, resultMap);
            resultMap = GetFilteredResultMap(resultMap);
            this.GridElementResult = new FlowBlockOut();
            Dictionary<FieldElement, string> precedingFieldValues = null;
            precedingFieldValues = GetPrecedingFieldValues(this);
            this.GridElementResult.Results = resultMap?.Any() == true ? resultMap.Select(x => new FlowBlockOutDataset()
            {
                FieldValueMappings = x.Select(y => new FlowBlockOutDatasetFieldValueMapping()
                {
                    Field = y.Key,
                    Value = y.Value,
                    PrecedingFieldValues = precedingFieldValues
                }).ToList()
            }).ToList() : CreateEmptyResults();

            ResetOutputDatasetProcessing(runtime);

            runtime.NotifyResultDatasetsGenerated(this, this.GridElementResult.Results ?? new List<FlowBlockOutDataset>());

            if (!runtime.ExecutionFlowEnabled)
                return;

            var nextElementList = this.GetNextFlowBlocks();
            if (!nextElementList.Any())
            {
                foreach (var result in this.GridElementResult.Results)
                {
                    foreach (var fieldValueMapping in result.FieldValueMappings)
                    {
                        fieldValueMapping.Field.SetValue(runtime, fieldValueMapping.Value);
                    }
                }

                CompleteOutputDatasetProcessing(runtime);
            }
            else if (nextElementList.All(x => x.HasIterationContext))
            {
                this.Fields.ForEach(x => x.Pending = true);
                nextElementList.ForEach(x => x.Execute(runtime, this));
                CompleteOutputDatasetProcessing(runtime);
            }
            else
            {
                var items = new List<IRuntimeWorkItem>(this.GridElementResult.Results.Count);
                foreach (var result in this.GridElementResult.Results)
                {
                    items.Add(new Runtime.WorkItems.ApplyOutputDatasetAndScheduleNextWorkItem(this, result));
                }
                items.Add(new Runtime.WorkItems.CompleteOutputDatasetProcessingWorkItem(this));
                runtime.TaskRunner.EnqueueBatchInExecutionOrder(items);
            }
        }

        private void ApplyOutputBehavior(BaseRuntime runtime, ref IEnumerable<Dictionary<FieldElement, string>> resultMap)
        {
            if (resultMap == null)
                return;

            if (OutputBehavior != OutputBehavior.Range && !resultMap.Any())
                return;

            if (this.OutputBehavior == OutputBehavior.First)
            {
                resultMap = new List<Dictionary<FieldElement, string>>()
                {
                    resultMap.First()
                };
            }
            else if (this.OutputBehavior == OutputBehavior.Last)
            {
                resultMap = new List<Dictionary<FieldElement, string>>()
                {
                    resultMap.Last()
                };
            }
            else if (this.OutputBehavior == OutputBehavior.Range)
            {
                string startValue = this.StartValue;
                startValue = FlowBloxFieldHelper.ReplaceFieldsInString(startValue);
                var resultMapList = resultMap.ToList();

                if (!string.IsNullOrWhiteSpace(startValue))
                {
                    runtime.Report("Use StartValue=" + startValue);

                    var matchingStartIndex = resultMapList.FindIndex(x => x.Values.Contains(startValue));
                    if (matchingStartIndex < 0)
                    {
                        CreateNotification(runtime, BaseResultFlowBlockNotifications.StartValueNotFound);
                        resultMap = new List<Dictionary<FieldElement, string>>();
                        return;
                    }

                    resultMap = resultMapList.Skip(matchingStartIndex).ToList();
                    return;
                }

                var hasInvalidStartIndex = StartIndex.HasValue &&
                    (StartIndex.Value < 0 || StartIndex.Value >= resultMapList.Count);
                var hasInvalidEndIndex = EndIndex.HasValue &&
                    (EndIndex.Value < 0 || EndIndex.Value >= resultMapList.Count);
                var hasInvalidIndexOrder = StartIndex.HasValue &&
                    EndIndex.HasValue &&
                    StartIndex.Value >= EndIndex.Value;

                if (hasInvalidStartIndex || hasInvalidEndIndex || hasInvalidIndexOrder)
                {
                    if (hasInvalidStartIndex)
                    {
                        runtime.Report(
                            string.Format(
                                FlowBloxResourceUtil.GetLocalizedString(
                                    "BaseResultFlowBlock_Warning_StartIndexOutsideResultRange",
                                    typeof(FlowBloxTexts)),
                                StartIndex.GetValueOrDefault(),
                                resultMapList.Count),
                            FlowBloxLogLevel.Warning);
                    }

                    if (hasInvalidEndIndex)
                    {
                        runtime.Report(
                            string.Format(
                                FlowBloxResourceUtil.GetLocalizedString(
                                    "BaseResultFlowBlock_Warning_EndIndexOutsideResultRange",
                                    typeof(FlowBloxTexts)),
                                EndIndex.GetValueOrDefault(),
                                resultMapList.Count),
                            FlowBloxLogLevel.Warning);
                    }

                    if (hasInvalidIndexOrder)
                    {
                        runtime.Report(
                            string.Format(
                                FlowBloxResourceUtil.GetLocalizedString(
                                    "BaseResultFlowBlock_Warning_IndexOrderInvalid",
                                    typeof(FlowBloxTexts)),
                                EndIndex.GetValueOrDefault(),
                                StartIndex.GetValueOrDefault()),
                            FlowBloxLogLevel.Warning);
                    }

                    CreateNotification(runtime, BaseResultFlowBlockNotifications.IndexOutputUnavailable);
                    resultMap = new List<Dictionary<FieldElement, string>>();
                    return;
                }

                int startIndex = this.StartIndex ?? 0;
                int endIndex = this.EndIndex ?? resultMapList.Count - 1;

                runtime.Report("Use StartIndex=" + startIndex.ToString() + " EndIndex=" + endIndex.ToString());
                resultMap = resultMapList.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
            }
        }

        private List<FlowBlockOutDataset> CreateEmptyResults()
        {
            var results = new List<FlowBlockOutDataset>();
            var precedingFieldValues = GetPrecedingFieldValues(this);

            var dataset = new FlowBlockOutDataset()
            {
                FieldValueMappings = this.Fields
                    .Select(field => new FlowBlockOutDatasetFieldValueMapping()
                    {
                        Field = field,
                        Value = null,
                        PrecedingFieldValues = precedingFieldValues
                    })
                    .ToList()
            };

            results.Add(dataset);
            return results;
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();

            if (this.OutputBehavior != OutputBehavior.All)
                properties.Add(nameof(OutputBehavior));

            if (this.OutputBehavior == OutputBehavior.Range)
            {
                properties.Add(nameof(StartValue));
                properties.Add(nameof(StartIndex));
                properties.Add(nameof(EndIndex));
            }

            return properties;
        }

        public enum BaseResultFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "BaseResultFlowBlockNotifications_StartValueNotFound", ResourceType = typeof(FlowBloxTexts))]
            StartValueNotFound,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "BaseResultFlowBlockNotifications_IndexOutputUnavailable", ResourceType = typeof(FlowBloxTexts))]
            IndexOutputUnavailable
        }

        protected sealed override bool InvokeExecutor(BaseRuntime runtime, Action executor)
        {
            var success = base.InvokeExecutor(runtime, executor);
            if (!success)
                GenerateResult(runtime);
            return success;
        }

        public void ResetOutputDatasetProcessing() => ResetOutputDatasetProcessing(runtime: null);

        public void ResetOutputDatasetProcessing(BaseRuntime runtime)
        {
            OutputDataset_CurrentlyProcessing = null;
            OutputDatasetProcessingCompleted = false;
            NotifyOutputDatasetProcessingChanged(runtime);
        }

        internal void CompleteOutputDatasetProcessing(BaseRuntime runtime)
        {
            if (OutputDatasets_Count <= 0 || OutputDatasetProcessingCompleted)
                return;

            OutputDatasetProcessingCompleted = true;
            NotifyOutputDatasetProcessingChanged(runtime);
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            ResetOutputDatasetProcessing(runtime);
            base.RuntimeStarted(runtime);
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (runtime?.Aborted != true)
                CompleteOutputDatasetProcessing(runtime);

            base.RuntimeFinished(runtime);
        }

        internal void NotifyOutputDatasetProcessingChanged(BaseRuntime runtime)
        {
            OutputDatasetProcessingChanged?.Invoke(runtime);
        }
    }
}

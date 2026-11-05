using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.CollectionOperations
{
    [Display(Name = "CollectionQueryFieldMapping_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class CollectionQueryFieldMapping : FlowBloxReactiveObject
    {
        [Required]
        [Display(Name = "CollectionQueryFieldMapping_InputField", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, 
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName), 
            SelectionFilterMethod = nameof(CollectionQueryFlowBlock.GetPossibleInputFields), 
            SelectionFilterDependency = nameof(CollectionQueryFlowBlock.ReferencedFlowBlocks))]
        public FieldElement InputField { get; set; }

        [Required]
        [Display(Name = "CollectionQueryFieldMapping_OutputField", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public FieldElement OutputField { get; set; }

        public override string ToString() => $"{InputField} → {OutputField}";
    }

    [FlowBloxUIGroup("CollectionQueryFlowBlock_Groups_Query", 0)]
    [FlowBloxUIGroup("CollectionQueryFlowBlock_Groups_OutputMappings", 1)]
    [Display(Name = "CollectionQueryFlowBlock_DisplayName", Description = "CollectionQueryFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class CollectionQueryFlowBlock : BaseMultiResultCollectorFlowBlock
    {
        private readonly List<bool> _stagedPredicateResults = [];

        public CollectionQueryFlowBlock()
        {
            OutputMappings = new ObservableCollection<CollectionQueryFieldMapping>();
            Predicates = new ObservableCollection<LogicalCondition>();
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.format_list_bulleted, 16, SKColors.Teal);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.format_list_bulleted, 32, SKColors.Teal);

        private CollectionQueryOperation _operation;

        [Display(Name = "CollectionQueryFlowBlock_Operation", ResourceType = typeof(FlowBloxTexts), GroupName = "CollectionQueryFlowBlock_Groups_Query", Order = 0)]
        public CollectionQueryOperation Operation
        {
            get => _operation;
            set
            {
                if (_operation == value) return;
                _operation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Fields));
                OnPropertyChanged(nameof(PredicateResultField));
            }
        }

        [ActivationCondition(MemberName = nameof(Operation), Value = CollectionQueryOperation.ElementAt)]
        [Range(0, int.MaxValue)]
        [Display(Name = "CollectionQueryFlowBlock_ElementIndex", Description = "CollectionQueryFlowBlock_ElementIndex_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "CollectionQueryFlowBlock_Groups_Query", Order = 1)]
        public int ElementIndex { get; set; }

        [Display(Name = "CollectionQueryFlowBlock_Predicates", Description = "CollectionQueryFlowBlock_Predicates_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "CollectionQueryFlowBlock_Groups_Query", Order = 2)]
        [FlowBloxUI(Factory = UIFactory.GridView, UiOptions = UIOptions.EnableFieldSelection, CreatableTypes = [typeof(FieldLogicalComparisonCondition), typeof(LogicalGroupCondition)])]
        [FlowBloxCardList(ItemDisplayName = "CollectionQueryFlowBlock_Predicate")]
        [FlowBloxDataGrid(GridColumnMemberNames = [nameof(LogicalCondition.LogicalOperator), nameof(LogicalCondition.DisplayName)])]
        public ObservableCollection<LogicalCondition> Predicates { get; set; }

        [ActivationCondition(ActivationMethod = nameof(UsesOutputMappings))]
        [DependsOnProperty(MemberName = nameof(Operation))]
        [Display(Name = "CollectionQueryFlowBlock_OutputMappings", Description = "CollectionQueryFlowBlock_OutputMappings_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "CollectionQueryFlowBlock_Groups_OutputMappings", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<CollectionQueryFieldMapping> OutputMappings { get; set; }

        [ActivationCondition(ActivationMethod = nameof(UsesPredicateResultField))]
        [DependsOnProperty(MemberName = nameof(Operation))]
        [Display(Name = "CollectionQueryFlowBlock_PredicateResultField", ResourceType = typeof(FlowBloxTexts), GroupName = "CollectionQueryFlowBlock_Groups_Query", Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public FieldElement PredicateResultField { get; set; }

        public bool UsesPredicateResultField() => Operation is CollectionQueryOperation.Any or CollectionQueryOperation.All;

        public bool UsesOutputMappings() => !UsesPredicateResultField();

        public override List<FieldElement> Fields => UsesPredicateResultField()
            ? (PredicateResultField == null ? [] : [PredicateResultField])
            : OutputMappings.Select(x => x.OutputField).Where(x => x != null).Distinct().ToList();

        public List<FieldElement> GetPossibleInputFields() => ReferencedFlowBlocks
            .OfType<BaseResultFlowBlock>()
            .SelectMany(x => x.Fields)
            .Where(x => x != null)
            .Distinct()
            .ToList();

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Calculation;

        public void SynchronizeOutputFields()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var inputFields = GetPossibleInputFields();
            var obsolete = OutputMappings.Where(x => x.InputField == null || !inputFields.Contains(x.InputField)).ToList();
            foreach (var mapping in obsolete)
            {
                if (mapping.OutputField != null &&
                    FlowBloxDeletionHelper.Analyze(registry, mapping.OutputField).Dependencies.Count > 0)
                    continue;
                OutputMappings.Remove(mapping);
                if (mapping.OutputField != null)
                    FlowBloxDeletionHelper.DeleteInstance(registry, mapping.OutputField);
            }

            foreach (var input in inputFields)
            {
                var mapping = OutputMappings.FirstOrDefault(x => ReferenceEquals(x.InputField, input));
                if (mapping == null)
                {
                    var output = registry.CreateField(this, fieldType: input.FieldType?.FieldType ?? FieldTypes.Text);
                    mapping = new CollectionQueryFieldMapping { InputField = input, OutputField = output };
                    OutputMappings.Add(mapping);
                }
                SynchronizeField(input, mapping.OutputField);
            }

            if (UsesPredicateResultField() && PredicateResultField == null)
            {
                PredicateResultField = registry.CreateField(this, fieldType: FieldTypes.Boolean);
                PredicateResultField.Name = "Result";
            }
            OnPropertyChanged(nameof(OutputMappings));
            OnPropertyChanged(nameof(Fields));
            NotifyComponentChanged();
        }

        private static void SynchronizeField(FieldElement input, FieldElement output)
        {
            if (input == null || output == null) return;
            output.Name = input.Name;
            var sourceType = input.FieldType;
            output.FieldType = new TypeElement
            {
                FieldType = sourceType?.FieldType ?? FieldTypes.Text,
                DateFormat = sourceType?.DateFormat,
                DecimalSeparator = sourceType?.DecimalSeparator,
                GroupSeparator = sourceType?.GroupSeparator,
                IsNullable = sourceType?.IsNullable ?? true
            };
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                EnsureValidOutputMappings();

                var values = OutputMappings
                    .ToDictionary(x => x.OutputField, x => x.InputField.StringValue);

                StageValue(values);
                _stagedPredicateResults.Add(EvaluatePredicates());

                if (CanGenerateResult())
                    GenerateQueryResult(runtime);
            });
        }

        private void EnsureValidOutputMappings()
        {
            if (OutputMappings == null)
                throw new InvalidOperationException("The collection query output mappings must be initialized.");

            for (var index = 0; index < OutputMappings.Count; index++)
            {
                var mapping = OutputMappings[index];
                if (mapping?.InputField == null || mapping.OutputField == null)
                {
                    throw new InvalidOperationException(
                        $"Collection query field mapping at index {index} requires both an input field and an output field.");
                }
            }
        }

        private bool EvaluatePredicates()
        {
            if (Predicates == null || Predicates.Count == 0) return true;
            bool? result = null;
            foreach (var predicate in Predicates.Where(x => x != null))
            {
                var current = predicate.Check();
                result = result == null ? current : predicate.LogicalOperator == LogicalOperator.And ? result.Value && current : result.Value || current;
            }
            return result ?? true;
        }

        private void GenerateQueryResult(BaseRuntime runtime)
        {
            if (UsesPredicateResultField())
            {
                var result = Operation == CollectionQueryOperation.Any
                    ? _stagedPredicateResults.Any(x => x)
                    : _stagedPredicateResults.All(x => x);
                GenerateResult(runtime, new[] { new Dictionary<FieldElement, string> { [PredicateResultField] = result.ToString().ToLowerInvariant() } });
                return;
            }

            var candidates = StagedValues
                .Where((_, index) => _stagedPredicateResults[index])
                .ToList();

            if (candidates.Count == 0) { GenerateResult(runtime); return; }
            if (Operation is CollectionQueryOperation.First or CollectionQueryOperation.Last or CollectionQueryOperation.ElementAt)
            {
                var selected = Operation == CollectionQueryOperation.First ? candidates.FirstOrDefault() : Operation == CollectionQueryOperation.Last ? candidates.LastOrDefault() : candidates.ElementAtOrDefault(ElementIndex);
                if (selected == null) { GenerateResult(runtime); return; }
                GenerateResult(runtime, new[] { selected });
                return;
            }

            var resultMap = new Dictionary<FieldElement, string>();
            foreach (var outputField in OutputMappings
                .Select(x => x.OutputField)
                .Where(x => x != null)
                .Distinct())
            {
                var numbers = candidates.Select(x => Convert.ToDouble(
                    x.GetValueOrDefault(outputField),
                    System.Globalization.CultureInfo.InvariantCulture));
                var result = Operation == CollectionQueryOperation.Minimum ? numbers.Min() : Operation == CollectionQueryOperation.Maximum ? numbers.Max() : numbers.Average();
                resultMap[outputField] = FieldResultFormatter.FormatResult(outputField, result);
            }
            GenerateResult(runtime, new[] { resultMap });
        }

        protected override void ResetStagedValues()
        {
            base.ResetStagedValues();
            _stagedPredicateResults.Clear();
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Operation));
            if (Operation == CollectionQueryOperation.ElementAt)
                properties.Add(nameof(ElementIndex));
            properties.Add(nameof(Predicates));
            return properties;
        }
    }
}

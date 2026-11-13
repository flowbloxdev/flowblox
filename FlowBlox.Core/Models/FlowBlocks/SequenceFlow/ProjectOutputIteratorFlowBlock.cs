using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow.ExecuteProject;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FlowBlox.Core.Models.FlowBlocks
{
    [FlowBloxUIGroup("ProjectOutputIteratorFlowBlock_Groups_Source", 0)]
    [FlowBloxUIGroup("ProjectOutputIteratorFlowBlock_Groups_Mapping", 1)]
    [Display(Name = "ProjectOutputIteratorFlowBlock_DisplayName", Description = "ProjectOutputIteratorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ProjectOutputIteratorFlowBlock : BaseResultFlowBlock
    {
        private FieldElement _inputField;

        [Display(Name = "Global_InputField", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [Required]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleInputFields), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement InputField
        {
            get => _inputField;
            set => SetRequiredInputField(ref _inputField, value);
        }

        public List<FieldElement> GetPossibleInputFields() => FlowBloxFieldsResolver.GetFieldsOfAssociatedFlowBlocks(this);

        [Display(Name = "ProjectOutputIteratorFlowBlock_AssociatedExecuteProjectFlowBlock", Description = "ProjectOutputIteratorFlowBlock_AssociatedExecuteProjectFlowBlock_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "ProjectOutputIteratorFlowBlock_Groups_Source", Order = 1)]
        [AssociatedFlowBlockResolvable]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleExecuteProjectFlowBlocks), DisplayMember = nameof(BaseFlowBlock.Name))]
        public ExecuteProjectFlowBlock AssociatedExecuteProjectFlowBlock { get; set; }

        public IEnumerable<ExecuteProjectFlowBlock> GetPossibleExecuteProjectFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<ExecuteProjectFlowBlock>();
        }

        [Required]
        [Display(Name = "ProjectOutputIteratorFlowBlock_OutputName", ResourceType = typeof(FlowBloxTexts), GroupName = "ProjectOutputIteratorFlowBlock_Groups_Source", Order = 2)]
        [FlowBloxComboBox(DropDownMode = DropDownMode.Suggestion)]
        [FlowBloxItemsSource(Method = nameof(GetOutputNames), Dependency = nameof(AssociatedExecuteProjectFlowBlock))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string OutputName { get; set; }

        [Display(Name = "ProjectOutputIteratorFlowBlock_OutputMappings", ResourceType = typeof(FlowBloxTexts), GroupName = "ProjectOutputIteratorFlowBlock_Groups_Mapping", Order = 0)]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxDataGrid(IsMovable = true)]
        public ObservableCollection<ExecuteProjectOutputMappingEntry> OutputMappings { get; set; }

        public ProjectOutputIteratorFlowBlock()
        {
            OutputMappings = new ObservableCollection<ExecuteProjectOutputMappingEntry>();
        }

        public IEnumerable<string> GetOutputNames()
        {
            var associatedExecuteProjectFb = this.AssociatedExecuteProjectFlowBlock ?? GetPreviousFlowBlockOnPath<ExecuteProjectFlowBlock>(this);
            return associatedExecuteProjectFb?.GetOutputNames() ?? Enumerable.Empty<string>();
        }

        public IEnumerable<string> GetOutputPropertyNames()
        {
            var associatedExecuteProjectFb = this.AssociatedExecuteProjectFlowBlock ?? GetPreviousFlowBlockOnPath<ExecuteProjectFlowBlock>(this);
            return associatedExecuteProjectFb?.GetOutputPropertyNames(OutputName) ?? Enumerable.Empty<string>();
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.playlist_play, 16, SKColors.MediumPurple);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.playlist_play, 32, SKColors.MediumPurple);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.ControlFlow;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(InputField));
            properties.Add(nameof(AssociatedExecuteProjectFlowBlock));
            properties.Add(nameof(OutputName));
            return properties;
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return this.OutputMappings?
                    .Select(m => m.Field)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return this.Invoke(runtime, data, () =>
            {

                string responseJson = this.InputField.StringValue;
                if (string.IsNullOrWhiteSpace(responseJson))
                    throw new ValidationException("ResponseJson must not be empty.");

                if (string.IsNullOrWhiteSpace(OutputName))
                    throw new ValidationException("OutputName must not be empty.");

                var response = JsonSerializer.Deserialize<Runner.Contracts.RunnerResponse>(
                    responseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response == null)
                    throw new InvalidOperationException("RunnerResponse could not be deserialized.");

                if (response.Outputs == null || 
                    !response.Outputs.TryGetValue(OutputName, out var datasets) || 
                    datasets == null)
                {
                    GenerateResult(runtime, new List<Dictionary<FieldElement, string>>());
                    return;
                }

                var resultMap = new List<Dictionary<FieldElement, string>>();

                foreach (var ds in datasets)
                {
                    var entry = new Dictionary<FieldElement, string>();

                    foreach (var mapping in OutputMappings)
                    {
                        if (mapping?.Field == null)
                            continue;

                        var key = mapping.OutputPropertyName;
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        object val = null;
                        if (ds.Values != null)
                            ds.Values.TryGetValue(key, out val);

                        entry[mapping.Field] = val?.ToString() ?? "";
                    }

                    resultMap.Add(entry);
                }

                GenerateResult(runtime, resultMap);
                ExecuteNextFlowBlocks(runtime);
            });
        }
    }
}

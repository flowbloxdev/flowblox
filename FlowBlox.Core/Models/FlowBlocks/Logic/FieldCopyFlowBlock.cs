using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace FlowBlox.Core.Models.FlowBlocks.Logic
{
    [Serializable()]
    [Display(Name = "FieldCopyFlowBlock_DisplayName", Description = "FieldCopyFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class FieldCopyFlowBlock : BaseResultFlowBlock
    {
        [Display(Name = "FieldCopyFlowBlock_InputFieldScope", Description = "FieldCopyFlowBlock_InputFieldScope_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = -1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox)]
        public FieldCopyInputScope InputFieldScope { get; set; }

        private FieldElement _inputField;

        [Display(Name = "Global_InputField", Description = "FieldCopyFlowBlock_InputField_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association, 
            SelectionFilterMethod = nameof(GetPossibleInputFields), 
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName), Operations = UIOperations.Link | UIOperations.Unlink)]
        [Required()]
        [DependsOnProperty(MemberName = nameof(InputFieldScope))]
        public FieldElement InputField 
        {
            get => _inputField;
            set => SetRequiredInputField(ref _inputField, value);
        }

        public List<FieldElement> GetPossibleInputFields() => GetPossibleFieldElements();

        public override List<FieldElement> GetPossibleFieldElements()
        {
            return InputFieldScope switch
            {
                FieldCopyInputScope.DirectPredecessors => FlowBloxFieldsResolver.GetFieldsOfAssociatedFlowBlocks(this),
                FieldCopyInputScope.AllFields => base.GetPossibleFieldElements(),
                FieldCopyInputScope.InputFields => FlowBloxRegistryProvider.GetRegistry().GetUserFields(UserFieldTypes.Input),
                FieldCopyInputScope.MemoryFields => FlowBloxRegistryProvider.GetRegistry().GetUserFields(UserFieldTypes.Memory),
                _ => FlowBloxFieldsResolver.GetFieldsOfAssociatedFlowBlocks(this)
            };
        }

        [Display(Name = "FieldCopyFlowBlock_CopiedFields", Description = "FieldCopyFlowBlock_CopiedFields_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ListView, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        [FlowBloxCardList(DisplayPropertyNames = [nameof(FieldElement.Name), nameof(FieldElement.FieldType)])]
        [FlowBloxListView(LVColumnMemberNames = new[] { nameof(FieldElement.FlowBlockName), nameof(FieldElement.Name) })]
        public ObservableCollection<FieldElement> CopiedFields { get; set; }

        [JsonProperty("DisributedFields")]
        private ObservableCollection<FieldElement> LegacyDistributedFields
        {
            set
            {
                if (CopiedFields.Count == 0 && value != null)
                    CopiedFields = value;
            }
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.source_merge, 16, SKColors.SaddleBrown);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.source_merge, 32, SKColors.SaddleBrown);

        public FieldCopyFlowBlock() : base ()
        {
            CopiedFields = new ObservableCollection<FieldElement>();
        }

        public override List<FieldElement> Fields => CopiedFields.ToList();

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Logic;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(InputFieldScope));
            properties.Add(nameof(InputField));
            return properties;
        }

        public override bool Execute(Runtime.BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                runtime.Report($"Copying field '{InputField.FullyQualifiedName}' to: {string.Join(", ", Fields.Select(x => x.Name))}.");
                var resultEntry = Fields.ToDictionary(x => x, y => InputField.StringValue);
                GenerateResult(runtime, new List<Dictionary<FieldElement, string>>() { resultEntry });
            });
        }
    }
}

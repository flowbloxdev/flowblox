using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Util.Resources;
using System.Collections.ObjectModel;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "EnumerationFlowBlock_DisplayName", Description = "EnumerationFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class EnumerationFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "EnumerationFlowBlock_Parameters", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxCardList(
            ItemDisplayName = "EnumerationFlowBlock_Parameter",
            DisplayPropertyNames = [nameof(FieldElement.Name), nameof(FieldElement.FieldType)])]
        [FlowBloxFieldSelection(DefaultRequiredValue = false)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(FieldElement.FlowBlockName), nameof(FieldElement.Name) })]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(DisplayMember = nameof(FieldElement.FullyQualifiedName), Method = nameof(GetPossibleFieldElements))]
        public ObservableCollection<FieldElement> Parameters { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.format_list_bulleted, 16, SKColors.Teal);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.format_list_bulleted, 32, SKColors.Teal);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Parameters));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var values = Parameters.Select(p => p.StringValue)
                    .ExceptNullOrEmpty()
                    .ToArray();

                GenerateResult(runtime, values);
            });
        }
    }
}

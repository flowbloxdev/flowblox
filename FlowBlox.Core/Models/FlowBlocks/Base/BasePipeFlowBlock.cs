using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.FlowBlocks.Base
{
    public abstract class BasePipeFlowBlock : BaseSingleResultFlowBlock
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

        public virtual List<FieldElement> GetPossibleInputFields() => FlowBloxFieldsResolver.GetFieldsOfAssociatedFlowBlocks(this);

        public override void OnAfterOpen()
        {
            if (InputField != null || ReferencedFlowBlocks.Count != 1)
                return;

            var sourceFields = ReferencedFlowBlocks[0] is BaseResultFlowBlock sourceFlowBlock
                ? sourceFlowBlock.Fields
                : null;

            if (sourceFields?.Count != 1 || sourceFields[0] == null)
                return;

            InputField = sourceFields[0];
            OnPropertyChanged(nameof(InputField));
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
    }
}

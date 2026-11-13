using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Generators
{
    public abstract class FlowBloxPipeGenerationStrategyBase : FlowBloxGenerationStrategyBase
    {
        private BasePipeFlowBlock _subscribedSource;

        protected FlowBloxPipeGenerationStrategyBase()
        {
        }

        protected FlowBloxPipeGenerationStrategyBase(BaseFlowBlock flowBlock) : base(flowBlock)
        {
            if (flowBlock is not BasePipeFlowBlock)
                throw new ArgumentException(nameof(flowBlock), $"The FlowBlock must derive from \"{typeof(BasePipeFlowBlock).Name}\".");
        }

        [Display(Name = "Global_InputField", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [Required]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(
            DisplayMember = nameof(FieldElement.FullyQualifiedName),
            Method = nameof(FlowBloxComponent.GetPossibleFieldElements))]
        public FieldElement InputField { get; set; }

        protected override void OnAfterSourceChanged()
        {
            if (_subscribedSource != null)
                _subscribedSource.PropertyChanged -= SourcePropertyChanged;

            _subscribedSource = Source as BasePipeFlowBlock;
            if (_subscribedSource == null)
                return;

            InputField = _subscribedSource.InputField;
            _subscribedSource.PropertyChanged += SourcePropertyChanged;
        }

        private void SourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BasePipeFlowBlock.InputField))
                InputField = _subscribedSource?.InputField;
        }
    }
}

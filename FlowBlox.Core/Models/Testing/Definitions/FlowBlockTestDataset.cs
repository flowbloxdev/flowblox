using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Testing
{
    public class FlowBlockTestDataset : FlowBloxReactiveObject
    {
        private BaseFlowBlock _flowBlock;
        private bool _execute;
        private bool _uIAssociatedFlowBlockRequiredForExecution;
        private bool _uIIsTargetFlowBlock;
        private bool _uIIsTargetNeighbour;
        private bool _uIIsVisibleInCurrentContext = true;
        private bool _uIHasFlowBlockValidationErrors;
        private List<FlowBloxFieldTestConfiguration> _flowBloxTestConfigurations;

        [JsonIgnore]
        public FlowBloxTestDefinition ParentTestDefinition { get; set; }

        [Display(Name = "FlowBlockTestDataset_FlowBlock", Description = "FlowBlockTestDataset_FlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
        public BaseFlowBlock FlowBlock
        {
            get => _flowBlock;
            set
            {
                if (_flowBlock != value)
                {
                    _flowBlock = value;
                    OnPropertyChanged();
                }
            }
        }

        [Display(Name = "FlowBlockTestDataset_Execute", Description = "FlowBlockTestDataset_Execute_Description", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool Execute
        {
            get => _execute;
            set
            {
                if (_execute != value)
                {
                    _execute = value;
                    ParentTestDefinition?.RecalculateRequiredFlagsAcrossDefinition();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UIAssociatedFlowBlockRequirementSatisfied));
                }
            }
        }

        [JsonIgnore]
        [Display(Name = "FlowBlockTestDataset_UIRequiredForExecution", Description = "FlowBlockTestDataset_UIRequiredForExecution_Description", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UIAssociatedFlowBlockRequiredForExecution
        {
            get => _uIAssociatedFlowBlockRequiredForExecution;
            set
            {
                if (_uIAssociatedFlowBlockRequiredForExecution != value)
                {
                    _uIAssociatedFlowBlockRequiredForExecution = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UIAssociatedFlowBlockRequirementSatisfied));
                }
            }
        }

        [JsonIgnore]
        public bool UIAssociatedFlowBlockRequirementSatisfied =>
            !UIAssociatedFlowBlockRequiredForExecution || Execute;

        [JsonIgnore]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UIIsTargetFlowBlock
        {
            get => _uIIsTargetFlowBlock;
            set
            {
                if (_uIIsTargetFlowBlock != value)
                {
                    _uIIsTargetFlowBlock = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UIIsTargetNeighbour
        {
            get => _uIIsTargetNeighbour;
            set
            {
                if (_uIIsTargetNeighbour != value)
                {
                    _uIIsTargetNeighbour = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UIIsVisibleInCurrentContext
        {
            get => _uIIsVisibleInCurrentContext;
            set
            {
                if (_uIIsVisibleInCurrentContext != value)
                {
                    _uIIsVisibleInCurrentContext = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UIHasFlowBlockValidationErrors
        {
            get => _uIHasFlowBlockValidationErrors;
            set
            {
                if (_uIHasFlowBlockValidationErrors != value)
                {
                    _uIHasFlowBlockValidationErrors = value;
                    OnPropertyChanged();
                }
            }
        }

        [Display(Name = "FlowBlockTestDataset_FieldTestConfigurations", Description = "FlowBlockTestDataset_FieldTestConfigurations_Description", ResourceType = typeof(FlowBloxTexts))]
        public List<FlowBloxFieldTestConfiguration> FlowBloxTestConfigurations
        {
            get => _flowBloxTestConfigurations;
            set
            {
                if (_flowBloxTestConfigurations != value)
                {
                    _flowBloxTestConfigurations = value;
                    OnPropertyChanged();
                }
            }
        }

        public FlowBlockTestDataset()
        {
            FlowBloxTestConfigurations = new List<FlowBloxFieldTestConfiguration>();
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(ParentTestDefinition?.Name))
                return ParentTestDefinition.Name;

            return !string.IsNullOrWhiteSpace(FlowBlock?.Name)
                ? FlowBlock.Name
                : base.ToString();
        }
    }
}

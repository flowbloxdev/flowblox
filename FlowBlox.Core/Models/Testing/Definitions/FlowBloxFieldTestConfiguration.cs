using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Testing
{
    [Display(Name = "FlowBloxTestConfiguration", ResourceType = typeof(FlowBloxTexts))]
    public class FlowBloxFieldTestConfiguration : FlowBloxReactiveObject
    {
        private bool _uIRequiredForExecution;
        private FieldElement _fieldElement;
        private FlowBloxTestConfigurationSelectionMode? _selectionMode;
        private ObservableCollection<ExpectationCondition> _expectationConditions;
        private string _userInput;
        private int? _index;

        public FlowBloxFieldTestConfiguration()
        {
            this.ExpectationConditions = new ObservableCollection<ExpectationCondition>();
        }

        public override string ToString() => FieldElement?.ToString() ?? string.Empty;

        [JsonIgnore]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UIRequiredForExecution
        {
            get => _uIRequiredForExecution;
            set
            {
                if (_uIRequiredForExecution != value)
                {
                    _uIRequiredForExecution = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UIRequirementSatisfied));
                }
            }
        }

        [JsonIgnore]
        public bool UIRequirementSatisfied => !UIRequiredForExecution || SelectionMode switch
        {
            FlowBloxTestConfigurationSelectionMode.UserInput => !string.IsNullOrWhiteSpace(UserInput),
            FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue => !string.IsNullOrWhiteSpace(UserInput),
            FlowBloxTestConfigurationSelectionMode.First => true,
            FlowBloxTestConfigurationSelectionMode.Index => Index >= 0,
            FlowBloxTestConfigurationSelectionMode.Last => true,
            _ => false
        };

        public FieldElement FieldElement
        {
            get => _fieldElement;
            set
            {
                if (_fieldElement != value)
                {
                    _fieldElement = value;

                    OnFieldElementChanged();

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PossibleSelectionModes));
                }
            }
        }

        private void OnFieldElementChanged()
        {
            if (_selectionMode == null)
                _selectionMode = PossibleSelectionModes.First();
        }

        [Display(Name = "FlowBloxTestConfiguration_SelectionMode", Description = "FlowBloxTestConfiguration_SelectionMode_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public FlowBloxTestConfigurationSelectionMode? SelectionMode
        {
            get => _selectionMode;
            set
            {
                if (_selectionMode != value)
                {
                    _selectionMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsIndexVisible));
                    OnPropertyChanged(nameof(UIRequirementSatisfied));
                }
            }
        }

        public IEnumerable<FlowBloxTestConfigurationSelectionMode> PossibleSelectionModes
        {
            get
            {
                if (this.FieldElement.UserFieldType == UserFieldTypes.Input)
                {
                    return
                    [
                        FlowBloxTestConfigurationSelectionMode.Keep,
                        FlowBloxTestConfigurationSelectionMode.UserInput
                    ];
                }
                else if (this.FieldElement.UserFieldType == UserFieldTypes.Memory)
                {
                    return
                    [
                        FlowBloxTestConfigurationSelectionMode.Keep,
                        FlowBloxTestConfigurationSelectionMode.UserInput
                    ];
                }
                else
                {
                   return
                   [
                       FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue,
                       FlowBloxTestConfigurationSelectionMode.UserInput,
                       FlowBloxTestConfigurationSelectionMode.First,
                       FlowBloxTestConfigurationSelectionMode.Index,
                       FlowBloxTestConfigurationSelectionMode.Last
                   ];
                }
            }
        }

        [ActivationCondition(
            MemberName = nameof(SelectionMode),
            Values = new object[]
        {
            FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue,
            FlowBloxTestConfigurationSelectionMode.UserInput
        })]
        [Display(Name = "FlowBloxTestConfiguration_UserInput", Description = "FlowBloxTestConfiguration_UserInput_Description", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxTextBox(MultiLine = true)]
        public string UserInput
        {
            get => _userInput;
            set
            {
                if (_userInput != value)
                {
                    _userInput = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UIRequirementSatisfied));
                }
            }
        }

        [Display(Name = "FlowBloxTestConfiguration_ExpectationConditions", Description = "FlowBloxTestConfiguration_ExpectationConditions_Description", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxDataGrid(
            ColumnMemberNames = new[]
            {
                nameof(ExpectationCondition.ExpectationConditionTarget),
                nameof(ExpectationCondition.Index),
                nameof(ExpectationCondition.Operator),
                nameof(ExpectationCondition.Value)
            })]
        public ObservableCollection<ExpectationCondition> ExpectationConditions
        {
            get => _expectationConditions;
            set
            {
                if (_expectationConditions != value)
                {
                    _expectationConditions = value;
                    SubscribeToExpectationConditions(_expectationConditions);
                    OnPropertyChanged();
                }
            }
        }

        private void SubscribeToExpectationConditions(ObservableCollection<ExpectationCondition> collection)
        {
            if (collection == null)
                return;

            collection.CollectionChanged += ExpectationConditions_CollectionChanged;

            foreach (var item in collection)
                item.PropertyChanged += ExpectationCondition_ItemPropertyChanged;
        }

        private void ExpectationConditions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (ExpectationCondition ec in e.NewItems)
                    ec.PropertyChanged += ExpectationCondition_ItemPropertyChanged;

            if (e.OldItems != null)
                foreach (ExpectationCondition ec in e.OldItems)
                    ec.PropertyChanged -= ExpectationCondition_ItemPropertyChanged;

            OnPropertyChanged(nameof(ExpectationConditions));
        }

        private void ExpectationCondition_ItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ExpectationConditions));
        }

        [FlowBloxUI(UISelectionButtons.None)]
        public int? Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UIRequirementSatisfied));
                }
            }
        }

        public bool IsIndexVisible => SelectionMode == FlowBloxTestConfigurationSelectionMode.Index;
    }
}

using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.AIAssistant.Constants;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.AIAssistant.Models
{
    [Display(Name = "AssistantConfiguration_DisplayName", Description = "AssistantConfiguration_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUIGroup("AssistantConfiguration_Groups_General", 0)]
    [FlowBloxUIGroup("AssistantConfiguration_Groups_Extended", 1)]
    [FlowBloxUIGroup("AssistantConfiguration_Groups_Permissions", 2)]
    public class AssistantConfiguration : FlowBloxReactiveObject
    {
        private string _selectedProviderName;
        private ObservableCollection<AIProviderBase> _providers;

        public AssistantConfiguration()
        {
            Providers = new ObservableCollection<AIProviderBase>();
        }

        [Display(Name = "AssistantConfiguration_SelectedProviderName", Description = "AssistantConfiguration_SelectedProviderName_Tooltip", GroupName = "AssistantConfiguration_Groups_General", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [CustomValidation(typeof(AssistantConfiguration), nameof(ValidateSelectedProviderName))]
        [FlowBloxComboBox]
        [FlowBloxItemsSource(Method = nameof(GetProviderNames), Dependency = nameof(Providers))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string SelectedProviderName
        {
            get => _selectedProviderName;
            set
            {
                if (_selectedProviderName == value)
                    return;
                _selectedProviderName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedProvider));
            }
        }

        [JsonIgnore]
        public AIProviderBase SelectedProvider
        {
            get
            {
                return _providers.FirstOrDefault(provider => 
                    !string.IsNullOrWhiteSpace(SelectedProviderName) && provider.Name == SelectedProviderName);
            }
        }

        [Display(Name = "AssistantConfiguration_Providers", Description = "AssistantConfiguration_Providers_Tooltip", GroupName = "AssistantConfiguration_Groups_General", ResourceType = typeof(FlowBloxTexts), Order = -1)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(AIProviderBase.Name), nameof(AIProviderBase.DefaultModel) })]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<AIProviderBase> Providers
        {
            get => _providers;
            set
            {
                if (_providers != null)
                    _providers.CollectionChanged -= ProvidersChanged;
                _providers = value ?? new ObservableCollection<AIProviderBase>();
                _providers.CollectionChanged += ProvidersChanged;
                UpdateProviderSelection();
            }
        }

        public IList<string> GetProviderNames() => Providers
            .Select(provider => provider.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        public static ValidationResult ValidateSelectedProviderName(object value, ValidationContext context)
        {
            var configuration = (AssistantConfiguration)context.ObjectInstance;
            if (configuration.GetProviderNames().Contains(value as string))
                return ValidationResult.Success;

            return new ValidationResult(
                FlowBloxResourceUtil.GetLocalizedString("AssistantConfiguration_SelectedProviderName_Invalid", typeof(FlowBloxTexts)),
                new[] { context.MemberName });
        }

        private void ProvidersChanged(object sender, NotifyCollectionChangedEventArgs args) => UpdateProviderSelection();

        private void UpdateProviderSelection()
        {
            if (string.IsNullOrWhiteSpace(SelectedProviderName))
                SelectedProviderName = GetProviderNames().FirstOrDefault();

            OnPropertyChanged(nameof(Providers));
            OnPropertyChanged(nameof(SelectedProvider));
        }

        [Display(Name = "AssistantConfiguration_Temperature", Description = "AssistantConfiguration_Temperature_Tooltip", GroupName = "AssistantConfiguration_Groups_General", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public double? Temperature { get; set; }

        [Display(Name = "AssistantConfiguration_EnableCommunicationProtocol", Description = "AssistantConfiguration_EnableCommunicationProtocol_Tooltip", GroupName = "AssistantConfiguration_Groups_General", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool EnableCommunicationProtocol { get; set; }

        [Display(Name = "AssistantConfiguration_EnableAutomaticAdjustment", Description = "AssistantConfiguration_EnableAutomaticAdjustment_Tooltip", GroupName = "AssistantConfiguration_Groups_General", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool EnableAutomaticAdjustment { get; set; } = true;

        [Display(Name = "AssistantConfiguration_AttachProjectJsonAutomatically", Description = "AssistantConfiguration_AttachProjectJsonAutomatically_Tooltip", GroupName = "AssistantConfiguration_Groups_General", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AttachProjectJsonAutomatically { get; set; } = false;

        [Display(Name = "AssistantConfiguration_OptionsAccessLevel", Description = "AssistantConfiguration_OptionsAccessLevel_Tooltip", GroupName = "AssistantConfiguration_Groups_Permissions", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public AssistantOptionsAccessLevel OptionsAccessLevel { get; set; } = AssistantOptionsAccessLevel.None;

        [Display(Name = "AssistantConfiguration_MaxTokens", Description = "AssistantConfiguration_MaxTokens_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int? MaxTokens { get; set; }

        [Display(Name = "AssistantConfiguration_MaxToolRounds", Description = "AssistantConfiguration_MaxToolRounds_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int MaxToolRounds { get; set; } = 100;

        [Display(Name = "AssistantConfiguration_MaxLatestMessages", Description = "AssistantConfiguration_MaxLatestMessages_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [Range(AssistantConfigurationLimits.MinLatestMessages, AssistantConfigurationLimits.MaxLatestMessages)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int MaxLatestMessages { get; set; } = 16;

        [Display(Name = "AssistantConfiguration_MinLatestMessages", Description = "AssistantConfiguration_MinLatestMessages_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [Range(AssistantConfigurationLimits.MinLatestMessages, AssistantConfigurationLimits.MaxLatestMessages)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int MinLatestMessages { get; set; } = 2;

        [Display(Name = "AssistantConfiguration_SummaryCompactionRate", Description = "AssistantConfiguration_SummaryCompactionRate_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [Range(AssistantConfigurationLimits.MinSummaryCompactionRate, AssistantConfigurationLimits.MaxSummaryCompactionRate)]
        [FlowBloxUI(UISelectionButtons.None)]
        public double SummaryCompactionRate { get; set; } = 1d;

        [Display(Name = "AssistantConfiguration_MaxContextTokens", Description = "AssistantConfiguration_MaxContextTokens_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int MaxContextTokens { get; set; } = 32000;

        [Display(Name = "AssistantConfiguration_ReservedResponseTokens", Description = "AssistantConfiguration_ReservedResponseTokens_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 7)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int ReservedResponseTokens { get; set; } = 4096;

        [Display(Name = "AssistantConfiguration_ApproximateCharactersPerToken", Description = "AssistantConfiguration_ApproximateCharactersPerToken_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 8)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int ApproximateCharactersPerToken { get; set; } = 5;

        [Display(Name = "AssistantConfiguration_MaxFieldValuesTokensPerResponse", Description = "AssistantConfiguration_MaxFieldValuesTokensPerResponse_Tooltip", GroupName = "AssistantConfiguration_Groups_Extended", ResourceType = typeof(FlowBloxTexts), Order = 9)]
        [Range(1, 20000)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int MaxFieldValuesTokensPerResponse { get; set; } = 4000;
    }
}

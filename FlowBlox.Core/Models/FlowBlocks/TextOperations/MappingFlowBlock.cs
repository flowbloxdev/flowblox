using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "MappingFlowBlock_MappingEntry_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public sealed class MappingFlowBlockEntry : FlowBloxReactiveObject, IValidatableObject
    {
        [Display(Name = "MappingFlowBlockEntry_Key", Description = "MappingFlowBlockEntry_Key_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public string Key { get; set; } = string.Empty;

        [Display(Name = "MappingFlowBlockEntry_RegularExpression", Description = "MappingFlowBlockEntry_RegularExpression_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxTextBox(IsCodingMode = true, SyntaxHighlighting = "FlowBlox.UICore.Resources.Highlighting.Regex.xshd")]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.Regex))]
        public string RegularExpression { get; set; } = string.Empty;

        [Display(Name = "MappingFlowBlockEntry_Value", Description = "MappingFlowBlockEntry_Value_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public string Value { get; set; } = string.Empty;

        public override string ToString() =>
            !string.IsNullOrWhiteSpace(Key) ? Key : RegularExpression;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var key = this.Resolve(owner => owner.Key);
            var regularExpression = this.Resolve(owner => owner.RegularExpression);
            var hasKey = !string.IsNullOrWhiteSpace(key);
            var hasRegularExpression = !string.IsNullOrWhiteSpace(regularExpression);

            if (hasKey == hasRegularExpression)
            {
                yield return new ValidationResult(
                    FlowBloxResourceUtil.GetLocalizedString("MappingFlowBlockEntry_Validation_KeyOrRegexRequired"),
                    [nameof(Key), nameof(RegularExpression)]);
            }

            if (hasRegularExpression)
            {
                var isValidRegularExpression = true;

                try
                {
                    _ = new Regex(regularExpression);
                }
                catch (ArgumentException)
                {
                    isValidRegularExpression = false;
                }

                if (!isValidRegularExpression)
                {
                    yield return new ValidationResult(
                        FlowBloxResourceUtil.GetLocalizedString("MappingFlowBlockEntry_Validation_InvalidRegex"),
                        [nameof(RegularExpression)]);
                }
            }
        }
    }

    [FlowBloxUIGroup("MappingFlowBlock_Groups_Mapping", 0)]
    [Display(Name = "MappingFlowBlock_DisplayName", Description = "MappingFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public sealed class MappingFlowBlock : BasePipeFlowBlock
    {
        public MappingFlowBlock()
        {
            MappingEntries = new ObservableCollection<MappingFlowBlockEntry>();
        }

        [Display(Name = "MappingFlowBlock_MappingEntries", Description = "MappingFlowBlock_MappingEntries_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "MappingFlowBlock_Groups_Mapping", Order = 0)]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxDataGrid(
            IsMovable = true,
            ColumnMemberNames =
            [
                nameof(MappingFlowBlockEntry.Key),
                nameof(MappingFlowBlockEntry.RegularExpression),
                nameof(MappingFlowBlockEntry.Value)
            ])]
        public ObservableCollection<MappingFlowBlockEntry> MappingEntries { get; set; }

        [Display(Name = "MappingFlowBlock_DefaultValue", Description = "MappingFlowBlock_DefaultValue_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string DefaultValue { get; set; } = string.Empty;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.compare, 16, SKColors.MediumSeaGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.compare, 32, SKColors.MediumSeaGreen);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(MappingEntries));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var inputValue = InputField?.StringValue ?? string.Empty;
                var mappingEntry = FindMatchingEntry(inputValue);

                if (mappingEntry != null)
                {
                    GenerateResult(runtime, mappingEntry.Resolve(owner => owner.Value));
                    return;
                }

                var defaultValue = this.Resolve(owner => owner.DefaultValue);
                if (string.IsNullOrEmpty(defaultValue))
                    CreateNotification(runtime, MappingFlowBlockNotifications.NoMappingFound);

                GenerateResult(runtime, defaultValue);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(MappingFlowBlockNotifications));
                return notificationTypes;
            }
        }

        private MappingFlowBlockEntry FindMatchingEntry(string inputValue)
        {
            foreach (var mappingEntry in MappingEntries ?? Enumerable.Empty<MappingFlowBlockEntry>())
            {
                if (mappingEntry == null)
                    continue;

                var key = mappingEntry.Resolve(owner => owner.Key);
                var regularExpression = mappingEntry.Resolve(owner => owner.RegularExpression);

                if (!string.IsNullOrWhiteSpace(key) &&
                    string.Equals(inputValue, key, StringComparison.Ordinal))
                {
                    return mappingEntry;
                }

                if (!string.IsNullOrWhiteSpace(regularExpression) &&
                    TryIsRegexMatch(inputValue, regularExpression))
                {
                    return mappingEntry;
                }
            }

            return null;
        }

        private static bool TryIsRegexMatch(string inputValue, string regularExpression)
        {
            try
            {
                return Regex.IsMatch(inputValue, regularExpression);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public enum MappingFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "MappingFlowBlockNotifications_NoMappingFound", ResourceType = typeof(FlowBloxTexts))]
            NoMappingFound
        }
    }
}

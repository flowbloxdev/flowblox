using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "SplitFlowBlock_DisplayName", Description = "SplitFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class SplitFlowBlock : BasePipeFlowBlock
    {
        [Display(Name = "SplitFlowBlock_Separators", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ListView, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        [FlowBloxListView(LVColumnMemberNames = new[] { nameof(SplitSeparatorDefinition.Separator), nameof(SplitSeparatorDefinition.SpecialSeparator) })]
        [MinLength(1)]
        public ObservableCollection<SplitSeparatorDefinition> Separators { get; set; } = new();

        [Display(Name = "SplitFlowBlock_RemoveEmptyEntries", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool RemoveEmptyEntries { get; set; } = true;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.call_split, 16, SKColors.SeaGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.call_split, 32, SKColors.SeaGreen);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(RemoveEmptyEntries));
            return properties;
        }

        public override void OnAfterCreate()
        {
            Separators.Add(new SplitSeparatorDefinition
            {
                Separator = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.SplitFlowBlockDefaultSeparator].Value
            });

            base.OnAfterCreate();
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var separators = Separators
                    .Select(x => x.ResolveSeparator())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToArray();

                if (separators.Length == 0)
                    throw new InvalidOperationException(FlowBloxResourceUtil.GetLocalizedString("SplitFlowBlock_Validation_NoSeparatorDefined"));

                var options = RemoveEmptyEntries
                    ? StringSplitOptions.RemoveEmptyEntries
                    : StringSplitOptions.None;

                var values = (InputField?.StringValue ?? string.Empty).Split(separators, options);
                GenerateResult(runtime, values);
            });
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement("SplitFlowBlock.DefaultSeparator", ",", "This is the default separator of the Split FlowBlock.", OptionElement.OptionType.Text));
            base.OptionsInit(defaults);
        }
    }
}

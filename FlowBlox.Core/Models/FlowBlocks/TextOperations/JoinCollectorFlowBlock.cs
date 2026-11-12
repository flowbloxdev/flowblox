using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "JoinCollectorFlowBlock_DisplayName", Description = "JoinCollectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class JoinCollectorFlowBlock : BaseSingleResultCollectorFlowBlock
    {
        [Display(Name = "JoinFlowBlock_Separator", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [CustomValidation(typeof(JoinCollectorFlowBlock), nameof(ValidateSeparators))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Separator { get; set; }

        [Display(Name = "JoinFlowBlock_SpecialSeparator", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        [CustomValidation(typeof(JoinCollectorFlowBlock), nameof(ValidateSeparators))]
        public SpecialSeparator? SpecialSeparator { get; set; }

        public static ValidationResult ValidateSeparators(object value, ValidationContext validationContext)
        {
            var joinFlowBlock = (JoinCollectorFlowBlock)validationContext.ObjectInstance;
            var separator = joinFlowBlock.Resolve(owner => owner.Separator);
            var specialSeparator = joinFlowBlock.Resolve(block => block.SpecialSeparator);

            if (string.IsNullOrEmpty(separator) && !specialSeparator.HasValue)
                return new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("JoinFlowBlock_Validation_NoSeparatorDefined"), [validationContext.MemberName]);
            if (!string.IsNullOrEmpty(separator) && specialSeparator.HasValue)
                return new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("JoinFlowBlock_Validation_ManySeparatorsDefined"), [validationContext.MemberName]);
            return ValidationResult.Success;
        }

        [Display(Name = "JoinFlowBlock_JoinedParameters", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.ListView, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            SelectionFilterMethod = nameof(GetPossibleJoinedParameters))]
        [FlowBloxFieldSelection(DefaultRequiredValue = false)]
        [FlowBloxListView(LVColumnMemberNames = new[] { nameof(FieldElement.FlowBlockName), nameof(FieldElement.Name) })]
        public ObservableCollection<FieldElement> JoinedParameters { get; set; }

        public List<FieldElement> GetPossibleJoinedParameters() => FlowBloxFieldsResolver.GetFieldsOfAssociatedFlowBlocks(this);

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.set_merge, 16, SKColors.DarkOliveGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.set_merge, 32, SKColors.DarkOliveGreen);

        public JoinCollectorFlowBlock() : base()
        {
            JoinedParameters = new ObservableCollection<FieldElement>();
        }

        public override void OnAfterCreate()
        {
            Separator = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.JoinFlowBlockDefaultSeparator].Value.ToString();
            base.OnAfterCreate();
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Separator));
            properties.Add(nameof(SpecialSeparator));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                string separator = this.Resolve(owner => owner.Separator);
                var specialSeparator = this.Resolve(block => block.SpecialSeparator);
                if (specialSeparator == Enums.SpecialSeparator.Tab)
                    separator = "\t";
                else if (specialSeparator == Enums.SpecialSeparator.NewLine)
                    separator = Environment.NewLine;

                if (string.IsNullOrEmpty(separator))
                    throw new InvalidOperationException("No separator was defined.");

                var values = JoinedParameters
                    .Select(x => x.StringValue)
                    .ExceptNullOrEmpty();

                StageValues(values);

                if (CanGenerateResult())
                    GenerateResult(runtime, string.Join(separator, StagedValues));
            });
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement("JoinFlowBlock.DefaultSeparator", ",", "This is the default separator of the Join FlowBlock.", OptionElement.OptionType.Text));
            base.OptionsInit(defaults);
        }
    }
}

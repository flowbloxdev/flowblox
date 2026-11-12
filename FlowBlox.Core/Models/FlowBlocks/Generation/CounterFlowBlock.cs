using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Runtime;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Generation
{
    [Serializable]
    [Display(Name = "CounterFlowBlock_DisplayName", Description = "CounterFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class CounterFlowBlock : BaseSingleResultFlowBlock
    {
        public enum CounterTypes
        {
            Integer,
            Alphanumeric
        }

        [Display(Name = "CounterFlowBlock_CounterType", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public CounterTypes CounterType { get; set; }

        [Display(Name = "CounterFlowBlock_Format", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Default, ToolboxCategory = nameof(FlowBloxToolboxCategory.CounterFormat), UiOptions = UIOptions.EnableFieldSelection)]
        public string Format { get; set; }

        [Display(Name = "CounterFlowBlock_StartValue", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        [CustomValidation(typeof(CounterFlowBlock), nameof(ValidateStartValue))]
        public new string StartValue { get; set; }

        [Display(Name = "CounterFlowBlock_FinalValue", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        [CustomValidation(typeof(CounterFlowBlock), nameof(ValidateEndValue))]
        public string FinalValue { get; set; }

        public static ValidationResult ValidateStartValue(string startValue, ValidationContext validationContext)
        {
            var instance = (CounterFlowBlock)validationContext.ObjectInstance;

            startValue = FlowBloxFieldHelper.ReplaceFieldsInString(startValue);
            var counterType = instance.Resolve(counter => counter.CounterType);
            if (counterType == CounterTypes.Integer && !string.IsNullOrEmpty(startValue))
            {
                if (!int.TryParse(startValue, out int startValueInt))
                    return new ValidationResult("The starting value must be an integer.", [validationContext.MemberName]);

                if (startValueInt < 0)
                    return new ValidationResult("The starting value must be equal or greater than zero.", [validationContext.MemberName]);
            }

            return ValidationResult.Success;
        }

        public static ValidationResult ValidateEndValue(string endValue, ValidationContext validationContext)
        {
            var instance = (CounterFlowBlock)validationContext.ObjectInstance;

            endValue = FlowBloxFieldHelper.ReplaceFieldsInString(endValue);
            var counterType = instance.Resolve(counter => counter.CounterType);
            if (counterType == CounterTypes.Integer)
            {
                if (string.IsNullOrEmpty(endValue))
                {
                    if (string.IsNullOrEmpty(instance.Resolve(owner => owner.Format)))
                        return new ValidationResult("If the format has not been assigned, an end value must be set for the counter type \"Integer\".", [validationContext.MemberName]);
                }
                else
                {
                    if (!int.TryParse(endValue, out _))
                        return new ValidationResult("The final value must be an integer.", [validationContext.MemberName]);
                }
            }

            return ValidationResult.Success;
        }

        [Display(Name = "CounterFlowBlock_AlphaRange", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string AlphaRange { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.counter, 16, SKColors.DarkCyan);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.counter, 32, SKColors.DarkCyan);

        public CounterFlowBlock() : base() { }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Generation;

        public override void OnAfterCreate()
        {
            AlphaRange = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.CounterAlphanumericRange].Value;
            base.OnAfterCreate();
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(CounterType));
            properties.Add(nameof(StartValue));
            properties.Add(nameof(FinalValue));
            properties.Add(nameof(Format));
            if (CounterType == CounterTypes.Alphanumeric)
                properties.Add(nameof(AlphaRange));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (CounterType == CounterTypes.Integer)
                {
                    ExecuteIntegerCounter(runtime);
                }
                else
                {
                    ExecuteAlphanumericCounter(runtime);
                }
            });
        }

        private void ExecuteIntegerCounter(BaseRuntime runtime)
        {
            var startValue = this.Resolve(owner => owner.StartValue);
            var finalValue = this.Resolve(owner => owner.FinalValue);
            var format = this.Resolve(owner => owner.Format);
            int start = string.IsNullOrEmpty(startValue) ? 0 : int.Parse(startValue);

            int endValue;
            if (!string.IsNullOrEmpty(finalValue))
                endValue = int.Parse(finalValue);
            else if (!string.IsNullOrEmpty(format))
            {
                var maximumDigits = string.Concat(Enumerable.Range(0, format.Length).Select(x => "9"));
                endValue = int.Parse(maximumDigits);
            }
            else
            {
                throw new InvalidOperationException("The final value or format must be set.");
            }

            if (endValue < 0 || endValue < start)
                throw new InvalidOperationException("Invalid value for CounterElement.EndValue: The ending value must be positive and greater than CounterElement.StartValue.");

            if (start < 0)
                throw new InvalidOperationException("Invalid value for CounterElement.StartValue: The starting value must be positive.");

            runtime.Report($"Use Type=Integer StartValue={start} EndValue={endValue}");

            var contents = Enumerable.Range(start, endValue - start + 1).Select(x => x.ToString(format));

            GenerateResult(runtime, contents);
        }

        private void ExecuteAlphanumericCounter(BaseRuntime runtime)
        {
            var startValue = this.Resolve(owner => owner.StartValue);
            var finalValue = this.Resolve(owner => owner.FinalValue);
            var format = this.Resolve(owner => owner.Format);
            var alphaRange = this.Resolve(owner => owner.AlphaRange);
            if (string.IsNullOrEmpty(alphaRange))
                alphaRange = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.CounterAlphanumericRange].Value;

            List<char> characters = alphaRange.ToCharArray().ToList();

            int counterRange = characters.Count;
            int length = format.Length;
            int endIndex = (int)Math.Pow(counterRange, length);

            var counterValues = new List<string>();

            bool startValueFound = string.IsNullOrEmpty(startValue);
            bool endValueFound = string.IsNullOrEmpty(finalValue);

            for (int counter = 0; counter < endIndex; counter++)
            {
                int counterTemp = counter;
                string counterValue = string.Empty;

                for (int index = 0; index < length; index++)
                {
                    int valueIndex = counterTemp % counterRange;
                    counterTemp /= counterRange;

                    char character = characters[valueIndex];
                    char formatChar = format[index];

                    if (char.IsUpper(formatChar))
                        character = char.ToUpper(character);
                    else
                        character = char.ToLower(character);

                    counterValue += character;
                }

                if (!startValueFound && counterValue == startValue)
                    startValueFound = true;

                if (startValueFound)
                    counterValues.Add(counterValue);

                if (!endValueFound && counterValue == finalValue)
                {
                    endValueFound = true;
                    break;
                }
            }

            if (!startValueFound && !string.IsNullOrEmpty(startValue))
                throw new InvalidOperationException("The starting value was not found in the generated range.");

            if (!endValueFound && !string.IsNullOrEmpty(finalValue))
                throw new InvalidOperationException("The final value was not found in the generated range.");

            GenerateResult(runtime, counterValues);
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement("Counter.Alphanumeric.Range", "aäbcdefghijklmnoöpqrsßtuüvwxyz", "Specify the characters for the alphanumeric counter here.", OptionElement.OptionType.Text));
            base.OptionsInit(defaults);
        }
    }
}

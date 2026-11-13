using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.SequenceDetection;
using FlowBlox.SequenceDetection.Constants;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Selection
{
    [Display(Name = "SequenceDetectionFlowBlock_DisplayName", Description = "SequenceDetectionFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class SequenceDetectionFlowBlock : BasePipeFlowBlock
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.repeat, 16, SKColors.MediumPurple);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.repeat, 32, SKColors.MediumPurple);

        public SequenceDetectionFlowBlock() : base()
        {

        }

        public override void OnAfterCreate()
        {
            MaxSequenceGenerationRuntimeSeconds = SequenceDetectionConstants.DefaultSequenceDetectionScannerTimeout;
            CreateDefaultGenerationStrategy();
            base.OnAfterCreate();
        }

        private void CreateDefaultGenerationStrategy()
        {
            GenerationStrategies.Add(new SequenceDetectionGenerationStrategy(this));
        }

        [Display(Name = "SequenceDetectionFlowBlock_MaxSequenceGenerationRuntimeSeconds", ResourceType = typeof(FlowBloxTexts))]
        [Range(
            1,
            int.MaxValue,
            ErrorMessageResourceName = "SequenceDetectionFlowBlock_Validation_MaxSequenceGenerationRuntimeSeconds", 
            ErrorMessageResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public int MaxSequenceGenerationRuntimeSeconds { get; set; }

        private string _sequenceDetectionPattern;

        [Display(Name = "SequenceDetectionFlowBlock_SequenceDetectionPattern", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true, SyntaxHighlighting = "JSON")]
        [FlowBloxUI(UISelectionButtons.None)]
        public string SequenceDetectionPattern
        {
            get => _sequenceDetectionPattern;
            set
            {
                if (_sequenceDetectionPattern != value)
                {
                    _sequenceDetectionPattern = value;
                    OnPropertyChanged();
                }
            }
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Selection;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(SequenceDetectionNotifications));
                return notificationTypes;
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (string.IsNullOrEmpty(SequenceDetectionPattern))
                {
                    CreateNotification(runtime, SequenceDetectionNotifications.NoSequencePatternSpecified);
                    GenerateResult(runtime);
                    return;
                }

                var sequenceDetectionPattern = JsonConvert.DeserializeObject<SequenceDetectionPattern>(SequenceDetectionPattern);

                var matchValues = new List<string>();
                SequenceSearch.Instance.SearchFor(InputField.StringValue, sequenceDetectionPattern, ref matchValues);

                if (!matchValues.Any())
                {
                    runtime.Report("The pattern returned no matches.", FlowBloxLogLevel.Warning);
                    CreateNotification(runtime, SequenceDetectionNotifications.ReturnedNoMatches);
                }

                GenerateResult(runtime, matchValues);
            });
        }

        public enum SequenceDetectionNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Pattern returned no matches")]
            ReturnedNoMatches,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "No sequence pattern specified")]
            NoSequencePatternSpecified
        }
    }
}

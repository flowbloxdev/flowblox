using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Selection.StartEndPatternSelector;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Selection
{
    [Display(Name = "StartEndPatternSelectorFlowBlock_DisplayName", Description = "StartEndPatternSelectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("StartEndPatternSelectorFlowBlock_SpecialExplanation_HierarchicalParsing", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("StartEndPatternSelectorFlowBlock_SpecialExplanation_PatternEntryBehavior", Icon = SpecialExplanationIcon.Information)]
    public class StartEndPatternSelectorFlowBlock : BasePipeFlowBlock
    {
        [Display(Name = "StartEndPatternSelectorFlowBlock_StartEndPatterns", Description = "StartEndPatternSelectorFlowBlock_StartEndPatterns_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        [FlowBloxDataGrid(IsMovable = true)]
        public ObservableCollection<StartEndPattern> StartEndPatterns { get; set; }

        private static readonly SKColor SelectorIconColor = new SKColor(100, 181, 212);

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.selection_search, 16, SelectorIconColor);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.selection_search, 32, SelectorIconColor);

        public StartEndPatternSelectorFlowBlock() : base()
        {
            StartEndPatterns = new ObservableCollection<StartEndPattern>();
        }

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Selection;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (StartEndPatterns == null || StartEndPatterns.Count == 0)
                {
                    CreateNotification(runtime, StartEndPatternSelectorNotifications.NoStartEndPatternsSpecified);
                    GenerateResult(runtime);
                    return;
                }

                string content = InputField.StringValue;

                List<string> results = new List<string>()
                {
                    content
                };

                foreach (var startEndPattern in StartEndPatterns)
                {
                    var endPattern = string.IsNullOrWhiteSpace(startEndPattern.EndPattern)
                        ? null
                        : startEndPattern.EndPattern;

                    results = PatternSelector.SelectPatternFromContext(results, startEndPattern.StartPattern, endPattern, true);
                    runtime.Report($"Found {results.Count} results for pattern combination \"{startEndPattern.StartPattern}\" and \"{startEndPattern.EndPattern}\"");

                    if (startEndPattern.Index != null)
                    {
                        if (results.Count > startEndPattern.Index)
                        {
                            results = new List<string>()
                        {
                            results.ElementAt(startEndPattern.Index.Value)
                        };
                            runtime.Report($"Selected index {startEndPattern.Index.Value} of {results.Count} results.");
                        }
                        else
                        {
                            results = new List<string>();
                            runtime.Report($"Index {startEndPattern.Index.Value} is out of range at {results.Count} results.");
                        }
                    }

                }

                var lastPattern = StartEndPatterns.LastOrDefault();
                results = ApplyReturnOptions(results, lastPattern);

                if (results.Count == 0)
                    CreateNotification(runtime, StartEndPatternSelectorNotifications.ReturnedNoMatches);

                GenerateResult(runtime, results);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(StartEndPatternSelectorNotifications));
                return notificationTypes;
            }
        }
        
        public enum StartEndPatternSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "No start/end patterns specified.")]
            NoStartEndPatternsSpecified,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "The pattern selector returned no matches.")]
            ReturnedNoMatches
        }

        private static List<string> ApplyReturnOptions(IEnumerable<string> values, StartEndPattern startEndPattern)
        {
            if (startEndPattern?.ReturnOptions == null)
                return values.ToList();

            return startEndPattern.ReturnOptions.Value switch
            {
                StartEndPatternReturnOptions.StartPattern => values
                    .Select(val => string.Concat(startEndPattern.StartPattern, val))
                    .ToList(),
                StartEndPatternReturnOptions.EndPattern => values
                    .Select(val => string.Concat(val, startEndPattern.EndPattern))
                    .ToList(),
                StartEndPatternReturnOptions.StartAndEndPattern => values
                    .Select(val => string.Concat(startEndPattern.StartPattern, val, startEndPattern.EndPattern))
                    .ToList(),
                _ => values.ToList()
            };
        }
    }
}


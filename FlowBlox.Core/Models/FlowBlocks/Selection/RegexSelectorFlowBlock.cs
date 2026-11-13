using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.FlowBlocks.Selection
{
    [Display(Name = "RegexSelectorFlowBlock_DisplayName", Description = "RegexSelectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class RegexSelectorFlowBlock : BasePipeFlowBlock
    {
        [Display(Name = "RegexSelectorFlowBlock_RegularExpression", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.Regex))]
        [FlowBloxTextBox(IsCodingMode = true, SyntaxHighlighting = "FlowBlox.UICore.Resources.Highlighting.Regex.xshd")]
        public string RegularExpression { get; set; }

        [Display(Name = "RegexSelectorFlowBlock_Group", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string Group { get; set; }

        [Display(Name = "RegexSelectorFlowBlock_CaseSensitive", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public bool CaseSensitive { get; set; }

        [Display(Name = "RegexSelectorFlowBlock_Multiline", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public bool Multiline { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.regex, 16, SKColors.OrangeRed);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.regex, 32, SKColors.OrangeRed);


        public RegexSelectorFlowBlock() : base()
        {
            Multiline = true;
        }

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Selection;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(RegularExpression));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                string content = InputField.StringValue;

                if (!this.ResolveOrCreateNotification(
                    x => x.RegularExpression,
                    out var regularExpression,
                    runtime,
                    RegexSelectorNotifications.RegularExpressionIsEmpty))
                    return;

                Regex regex = new Regex(regularExpression);
                MatchCollection matchCollection = regex.Matches(content);

                if (matchCollection.Count == 0)
                {
                    runtime.Report("The regular expression returned no matches.", FlowBloxLogLevel.Warning);
                    CreateNotification(runtime, RegexSelectorNotifications.ReturnedNoMatches);
                }
                else
                {
                    runtime.Report("The regular expression returned " + matchCollection.Count.ToString() + " matches.");
                }

                var group = this.Resolve(owner => owner.Group);
                List<string> matchValues = new List<string>();
                foreach (Match match in matchCollection)
                {
                    if (!string.IsNullOrEmpty(group))
                    {
                        matchValues.Add(match.Groups.GetValueOrDefault(group)?.Value);
                    }
                    else
                    {
                        matchValues.Add(match.Value);
                    }
                }
                GenerateResult(runtime, matchValues);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(RegexSelectorNotifications));
                return notificationTypes;
            }
        }

        public enum RegexSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Regular expression is empty")]
            RegularExpressionIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Regular Expression returned no matches")]
            ReturnedNoMatches
        }
    }
}

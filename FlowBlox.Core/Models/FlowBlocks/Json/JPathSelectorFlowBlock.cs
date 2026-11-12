using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Json
{
    [FlowBloxSpecialExplanation("JsonFlowBlocks_SpecialExplanation_JPathSyntax", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("JPathSelectorFlowBlock_SpecialExplanation_PathExamples", Icon = SpecialExplanationIcon.Information)]
    [Display(Name = "JPathSelectorFlowBlock_DisplayName", Description = "JPathSelectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class JPathSelectorFlowBlock : BaseSingleResultFlowBlock
    {
        private string _rowPath;
        private string _fallbackValue;

        [Display(Name = "JPathSelectorFlowBlock_JsonContent", Description = "JPathSelectorFlowBlock_JsonContent_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true, SyntaxHighlighting = "JSON")]
        [Required]
        public string JsonContent { get; set; }

        [Display(Name = "JPathSelectorFlowBlock_Path", Description = "JPathSelectorFlowBlock_Path_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Default, ToolboxCategory = nameof(FlowBloxToolboxCategory.JPath), UiOptions = UIOptions.EnableFieldSelection)]
        [Required]
        public string Path { get; set; }

        [Display(Name = "JPathSelectorFlowBlock_RowPath", Description = "JPathSelectorFlowBlock_RowPath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, ToolboxCategory = nameof(FlowBloxToolboxCategory.JPath), UiOptions = UIOptions.EnableFieldSelection)]
        [CustomValidation(typeof(JPathRowPathValidator), nameof(JPathRowPathValidator.ValidateRowPath))]
        [DependsOnProperty(MemberName = nameof(FallbackValue))]
        public string RowPath
        {
            get => _rowPath;
            set
            {
                if (_rowPath == value)
                    return;

                _rowPath = value;
                OnPropertyChanged();
            }
        }

        [Display(Name = "JPathSelectorFlowBlock_FallbackValue", Description = "JPathSelectorFlowBlock_FallbackValue_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string FallbackValue
        {
            get => _fallbackValue;
            set
            {
                if (_fallbackValue == value)
                    return;

                _fallbackValue = value;
                OnPropertyChanged();
            }
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.selection_ellipse_arrow_inside, 16, SKColors.Goldenrod);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.selection_ellipse_arrow_inside, 32, SKColors.Goldenrod);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Json;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(JPathSelectorNotifications));
                return notificationTypes;
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Path));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var jsonText = this.Resolve(owner => owner.JsonContent);
                var path = this.Resolve(owner => owner.Path);
                if (string.IsNullOrWhiteSpace(jsonText))
                {
                    CreateNotification(runtime, JPathSelectorNotifications.JsonContentIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    CreateNotification(runtime, JPathSelectorNotifications.PathIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                var rootToken = JToken.Parse(jsonText);
                var rowPath = this.Resolve(owner => owner.RowPath).Trim();
                if (!string.IsNullOrWhiteSpace(rowPath))
                {
                    var fallbackValue = this.Resolve(owner => owner.FallbackValue);
                    if (!JPathRowValueResolver.TryResolve(rootToken, path, rowPath, fallbackValue, out var rowValues))
                    {
                        CreateNotification(runtime, JPathSelectorNotifications.JsonTokenCouldNotBeResolved);
                        GenerateResult(runtime);
                        return;
                    }

                    GenerateResult(runtime, rowValues);
                    return;
                }

                var resultTokens = rootToken.SelectTokens(path, errorWhenNoMatch: false).ToList();
                if (resultTokens.Count == 0)
                {
                    CreateNotification(runtime, JPathSelectorNotifications.JsonTokenCouldNotBeResolved);
                    GenerateResult(runtime);
                    return;
                }

                var results = new List<string>();

                foreach (var resultToken in resultTokens)
                {
                    if (resultToken is JArray array)
                    {
                        foreach (var item in array)
                            results.Add(JPathTokenSerializer.Serialize(item));
                    }
                    else
                    {
                        results.Add(JPathTokenSerializer.Serialize(resultToken));
                    }
                }

                if (results.Count == 0)
                    CreateNotification(runtime, JPathSelectorNotifications.ReturnedNoMatches);

                GenerateResult(runtime, results);
            });
        }

        public enum JPathSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON content is empty")]
            JsonContentIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON path is empty")]
            PathIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON token could not be resolved by path")]
            JsonTokenCouldNotBeResolved,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "JSON path returned no matches")]
            ReturnedNoMatches
        }
    }
}

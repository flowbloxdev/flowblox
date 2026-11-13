using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Toolbox;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote
{
    [FlowBloxUIGroup("AIPromptFlowBlock_Groups_Request", 0)]
    [FlowBloxSpecialExplanation("AIPromptFlowBlock_SpecialExplanation_OutputFormat", Icon = SpecialExplanationIcon.Information)]
    [Display(Name = "AIPromptFlowBlock_DisplayName", Description = "AIPromptFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class AIPromptFlowBlock : BaseSingleResultFlowBlock
    {
        public AIPromptFlowBlock()
        {
            ApplyDefaultSystemInstruction();
        }

        [Required]
        [Display(Name = "AIPromptFlowBlock_Provider", Description = "AIPromptFlowBlock_Provider_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleProviders), DisplayMember = nameof(ManagedObject.Name))]
        public AIProviderBase Provider { get; set; }

        [Required]
        [Display(Name = "AIPromptFlowBlock_PromptTemplate", Description = "AIPromptFlowBlock_PromptTemplate_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPromptFlowBlock_Groups_Request", Order = 1)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.AIPromptTemplates))]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true)]
        public string PromptTemplate { get; set; }

        [Display(Name = "AIPromptFlowBlock_SystemInstruction", Description = "AIPromptFlowBlock_SystemInstruction_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPromptFlowBlock_Groups_Request", Order = 2)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.AIPromptTemplates))]
        [FlowBloxTextBox(MultiLine = true)]
        public string SystemInstruction { get; set; }

        [Display(Name = "AIPromptFlowBlock_ModelOverride", Description = "AIPromptFlowBlock_ModelOverride_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPromptFlowBlock_Groups_Request", Order = 3)]
        public string ModelOverride { get; set; }

        [Display(Name = "AIPromptFlowBlock_Temperature", Description = "AIPromptFlowBlock_Temperature_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPromptFlowBlock_Groups_Request", Order = 4)]
        public double? Temperature { get; set; }

        [Display(Name = "AIPromptFlowBlock_MaxTokens", Description = "AIPromptFlowBlock_MaxTokens_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPromptFlowBlock_Groups_Request", Order = 5)]
        public int? MaxTokens { get; set; }

        [Display(Name = "AIPromptFlowBlock_TimeoutSecondsOverride", Description = "AIPromptFlowBlock_TimeoutSecondsOverride_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPromptFlowBlock_Groups_Request", Order = 6)]
        public int? TimeoutSecondsOverride { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.robot, 16, SKColors.DeepSkyBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.robot, 32, SKColors.DeepSkyBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.AI;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override List<string> GetDisplayableProperties()
        {
            var props = base.GetDisplayableProperties();
            props.Add(nameof(Provider));
            return props;
        }

        public List<AIProviderBase> GetPossibleProviders()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetManagedObjects<AIProviderBase>().ToList();
        }

        private void ApplyDefaultSystemInstruction()
        {
            if (!string.IsNullOrWhiteSpace(SystemInstruction))
                return;

            SystemInstruction = FlowBloxToolboxResourceProvider.GetToolboxElementContent(
                ToolboxConstants.AIPromptTemplatesCategory,
                ToolboxConstants.SystemJsonOutputPromptTemplateName);
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (!this.ResolveOrCreateNotification(
                    owner => owner.PromptTemplate,
                    out var promptResolved,
                    runtime,
                    AIPromptNotifications.PromptMissing))
                    return;

                var req = new AIRequest
                {
                    Prompt = promptResolved,
                    SystemInstruction = this.Resolve(owner => owner.SystemInstruction),
                    Model = string.IsNullOrWhiteSpace(this.ModelOverride) ? null : this.ModelOverride,
                    Temperature = this.Resolve(x => x.Temperature),
                    MaxTokens = this.Resolve(x => x.MaxTokens),
                    TimeoutSecondsOverride = this.Resolve(x => x.TimeoutSecondsOverride),
                };

                req.Meta["FlowBlock"] = this.Name;
                req.Meta["FlowBlockType"] = GetType().Name;

                var ct = runtime.GetCancellationToken();
                var resp = Provider.ExecuteAsync(runtime, req, ct).GetAwaiter().GetResult();

                if (!resp.Success)
                {
                    var logMessage =
                        $"AI prompt execution failed.{Environment.NewLine}" +
                        $"Provider: {Provider?.Name ?? "n/a"} ({Provider?.ProviderType ?? "n/a"}){Environment.NewLine}" +
                        $"Error: {resp.Error ?? "n/a"}{Environment.NewLine}" +
                        $"Prompt:{Environment.NewLine}{promptResolved}";

                    runtime.Report(logMessage);

                    CreateNotification(runtime, AIPromptNotifications.AIPromptExecutionFailed);
                    GenerateResult(runtime);
                    return;
                }

                var output = resp.Text;
                GenerateResult(runtime, output);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(AIPromptNotifications));
                return notificationTypes;
            }
        }

        public enum AIPromptNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Prompt is empty")]
            PromptMissing,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "AI prompt execution failed")]
            AIPromptExecutionFailed
        }
    }
}

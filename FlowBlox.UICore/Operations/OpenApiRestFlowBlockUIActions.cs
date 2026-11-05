using FlowBlox.Core;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using ActionResources = FlowBlox.UICore.Resources.ClassResources.OpenApiRestFlowBlockUIActions;

namespace FlowBlox.UICore.Operations
{
    public class OpenApiRestFlowBlockUIActions : ComponentUIActions<OpenApiRestFlowBlock>
    {
        public OpenApiRestFlowBlockUIActions(OpenApiRestFlowBlock component) : base(component)
        {
        }

        public SKImage ReloadDefinitionIcon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.refresh, 16, SKColors.SteelBlue);

        public bool CanReloadDefinition() => !string.IsNullOrWhiteSpace(Component.OpenApiSource);

        [Display(Name = "OpenApiRestFlowBlockUIActions_ReloadDefinition", ResourceType = typeof(FlowBloxTexts))]
        public void ReloadDefinition()
        {
            var messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
            var logger = FlowBloxLogManager.Instance.GetLogger();
            var title = FlowBloxResourceUtil.GetLocalizedString(nameof(ActionResources.ReloadTitle), typeof(ActionResources));
            try
            {
                Component.ReloadDefinition(forceRefresh: true, preserveSelection: true);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to reload the OpenAPI definition for flow block '{Component.Name}'.", ex);
                messageBoxService.ShowMessageBox(
                    FlowBloxResourceUtil.GetLocalizedString(nameof(ActionResources.ReloadFailed), typeof(ActionResources)),
                    title,
                    FlowBloxMessageBoxTypes.Error);
                return;
            }

            logger.Info($"Reloaded the OpenAPI definition for flow block '{Component.Name}': {Component.EndpointSuggestions.Count} endpoints.");
            var hasWarnings = !string.IsNullOrWhiteSpace(Component.DefinitionWarnings);
            if (hasWarnings)
                logger.Warn($"OpenAPI validation warnings for flow block '{Component.Name}': {Component.DefinitionWarnings}");

            messageBoxService.ShowMessageBox(
                FlowBloxResourceUtil.GetLocalizedString(
                    hasWarnings ? nameof(ActionResources.ReloadWithWarnings) : nameof(ActionResources.ReloadSucceeded),
                    typeof(ActionResources)),
                title,
                hasWarnings ? FlowBloxMessageBoxTypes.Warning : FlowBloxMessageBoxTypes.Information);
        }
    }
}

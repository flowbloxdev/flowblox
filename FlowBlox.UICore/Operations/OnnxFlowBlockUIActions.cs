using System.ComponentModel.DataAnnotations;
using System.Windows;
using FlowBlox.Core;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.LocalAI;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Views;
using FlowBlox.UICore.ViewModels.PropertyView;
using SkiaSharp;

namespace FlowBlox.UICore.Operations;

public sealed class OnnxFlowBlockUIActions : ComponentUIActions<BaseFlowBlock>
{
    public OnnxFlowBlockUIActions(BaseFlowBlock component) : base(component)
    {
    }

    public SKImage ConfigureLocalAIIcon16 =>
        FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.brain, 16, SKColors.SteelBlue);

    public bool IsConfigureLocalAIVisible() => LocalAIScriptCatalog.ForBlock(Component).Any();

    public bool CanConfigureLocalAI()
    {
        var project = FlowBloxProjectManager.Instance.ActiveProject;
        var runtime = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
        return project != null
            && runtime?.IsRuntimeActive != true
            && runtime?.IsExternalProjectEditActive != true
            && LocalAISetup.CanConfigureBlock(project, Component)
            && IsConfigureLocalAIVisible();
    }

    [Display(
        Name = "LocalAISetup_Action",
        Description = "LocalAISetup_Action_Tooltip",
        ResourceType = typeof(FlowBloxTexts))]
    public void ConfigureLocalAI()
    {
        if (!CanConfigureLocalAI())
            return;

        var project = FlowBloxProjectManager.Instance.ActiveProject;
        var block = FlowBloxRegistryProvider.GetRegistry().Reload(Component);
        var owner = Application.Current?.Windows.OfType<PropertyWindow>().FirstOrDefault(window =>
            window.DataContext is PropertyWindowViewModel model && ReferenceEquals(model.PropertyViewModel.WorkingCopy, block));
        var dialog = new LocalAISetupWindow(project, block);
        if (owner != null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }
        else
            FlowBloxServiceLocator.Instance.GetService<IDialogService>().ShowWPFDialog(dialog);
    }
}

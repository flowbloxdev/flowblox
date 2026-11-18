using System.ComponentModel.DataAnnotations;
using FlowBlox.Core;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components.Authorization;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using SkiaSharp;
using Texts = FlowBlox.UICore.Resources.ClassResources.OAuthUserConnectionUIActions;

namespace FlowBlox.UICore.Operations;

public sealed class OAuthUserConnectionUIActions : ComponentUIActions<OAuthUserConnection>
{
    public OAuthUserConnectionUIActions(OAuthUserConnection component) : base(component)
    {
    }

    public SKImage ConnectIcon16 => Component.Icon16;
    public SKImage DisconnectIcon16 => Component.Icon16;

    public bool CanConnect() => FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>()?.IsRuntimeActive != true;
    public bool CanDisconnect() => CanConnect() && Component.IsConnected;

    [Display(Name = "OAuthUserConnectionUIActions_Connect", ResourceType = typeof(FlowBloxTexts))]
    public async void Connect()
    {
        try
        {
            var service = FlowBloxServiceLocator.Instance.GetService<FlowBlox.Core.Interfaces.IOAuthUserAuthorizationService>();
            var settings = Component.GetSettings();
            settings.ClientSecret = Component.GetClientSecret();
            if (service == null || !await service.AuthorizeAsync(settings, CancellationToken.None))
                throw new FlowBlox.Core.Authentication.OAuth.OAuthInteractionRequiredException();
            NotifyChanged();
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("OAuth user authorization failed.", exception);
            ShowFailure(nameof(Texts.ConnectFailed));
        }
    }

    [Display(Name = "OAuthUserConnectionUIActions_Disconnect", ResourceType = typeof(FlowBloxTexts))]
    public async void Disconnect()
    {
        try
        {
            await Component.DisconnectAsync();
            NotifyChanged();
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to remove local OAuth credentials.", exception);
            ShowFailure(nameof(Texts.DisconnectFailed));
        }
    }

    private void NotifyChanged()
    {
        FlowBloxComponentHelper.RaisePropertyChanged(Component, nameof(Component.IsConnected));
        Component.NotifyComponentChanged();
    }

    private static void ShowFailure(string key) =>
        FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>()?.ShowMessageBox(
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(Texts)),
            FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.Title), typeof(Texts)),
            FlowBloxMessageBoxTypes.Error);
}

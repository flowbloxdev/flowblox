using System;
using System.IO;
using System.Threading.Tasks;
using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Projects;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private bool _isReloadingFromProjectSpace;

    internal bool CanReloadFromProjectSpace =>
        !_isReloadingFromProjectSpace && !_isProjectLoading && !IsProjectEditingReadOnly
        && !IsActiveProjectFromDistributedData()
        && File.Exists(FlowBloxProjectManager.Instance.ActiveProjectPath)
        && !string.IsNullOrWhiteSpace(FlowBloxProjectManager.Instance.ActiveProject?.ProjectSpaceGuid)
        && !string.IsNullOrWhiteSpace(FlowBloxProjectManager.Instance.ActiveProject?.ProjectSpaceEndpointUri);

    private async void itmReloadFromProjectSpace_Click(object sender, EventArgs e) =>
        await ReloadFromProjectSpaceAsync();

    internal async Task ReloadFromProjectSpaceAsync()
    {
        if (!CanReloadFromProjectSpace)
            return;
        var manager = FlowBloxProjectManager.Instance;
        var project = manager.ActiveProject;
        var file = manager.ActiveProjectPath;
        var title = FlowBloxResourceUtil.GetLocalizedString("AppWindow_ReloadProjectSpace_Title", typeof(FlowBloxMainUITexts));
        var message = string.Format(
            FlowBloxResourceUtil.GetLocalizedString("AppWindow_ReloadProjectSpace_Confirm", typeof(FlowBloxMainUITexts)),
            project.ProjectName);
        var confirmation = await DesktopDialogs.Messages.ShowAsync(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirmation != MessageBoxResult.Yes || !ReferenceEquals(manager.ActiveProject, project) || !CanReloadFromProjectSpace)
            return;

        _isReloadingFromProjectSpace = true;
        ShowProjectLoadingOverlay();
        UpdateUI();
        try
        {
            var endpoint = project.ProjectSpaceEndpointUri;
            var accounts = FlowBloxAccountManager.Instance;
            if (!accounts.IsLoggedInFor(endpoint))
                await new FlowBloxAutoLoginExecutor().TryAutoLoginAsync();
            var tools = new ProjectSpaceTools(new FlowBloxWebApiService(endpoint), accounts.GetUserToken(endpoint));
            await tools.ReloadAsync(file, overwrite: true);
            if (!ReferenceEquals(manager.ActiveProject, project))
                return;
            CloseProject();
            _recentProjectPath = file;
            await OpenProjectFromRecentProjectPathAsync();
        }
        catch (ProjectSpaceLoginRequiredException)
        {
            await ShowProjectSpaceLoginHintAsync();
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to reload the local project from Project Space.", exception);
            await DesktopDialogs.Messages.ShowAsync(
                string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_ReloadProjectSpace_Failed", typeof(FlowBloxMainUITexts)), exception.Message),
                title, MessageBoxButtons.Ok, MessageBoxIcon.Error);
        }
        finally
        {
            _isReloadingFromProjectSpace = false;
            HideProjectLoadingOverlay();
            UpdateUI();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Util.Resources;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private const string OwnProjectsNotificationId = "launcher.projectspace.load.failed";
    private int _ownProjectsRefreshVersion;
    private ProjectShortcutSection _ownProjectsSection;

    internal async Task RefreshOwnProjectShortcutsAsync()
    {
        var version = ++_ownProjectsRefreshVersion;
        ShortcutSections.Remove(_ownProjectsSection);
        _ownProjectsSection = null;
        _appNotificationService.Dismiss(OwnProjectsNotificationId);

        var endpoint = ProjectShortcut.CurrentApiEndpoint;
        var accounts = FlowBloxAccountManager.Instance;
        if (string.IsNullOrWhiteSpace(endpoint) || !accounts.IsLoggedInFor(endpoint))
            return;

        var user = accounts.GetActiveUser(endpoint);
        var token = accounts.GetUserToken(endpoint);
        if (string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            var service = new FlowBloxWebApiService(endpoint);
            var response = await service.GetProjectsAsync(userToken: token, mine: true)
                .WaitAsync(TimeSpan.FromSeconds(15));
            if (!IsOwnProjectsRequestCurrent(version, endpoint, user, token))
                return;

            if (!response.Success)
            {
                PublishOwnProjectsError();
                return;
            }

            var projects = response.ResultObject?
                .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(project => new ProjectShortcut
                {
                    Name = project.Name,
                    ProjectSpaceGuid = project.Guid,
                    ApiEndpoint = endpoint
                }) ?? Enumerable.Empty<ProjectShortcut>();
            var sections = new List<ProjectShortcutSection>();
            AddShortcutSection(sections,
                FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.OwnProjects), typeof(Texts)), projects);
            if (sections.Count == 0)
                return;

            _ownProjectsSection = sections[0];
            var executionSection = ShortcutSections.FirstOrDefault(section => section.Items.Any(item => item.Execution != null));
            var index = executionSection == null ? ShortcutSections.Count : ShortcutSections.IndexOf(executionSection);
            ShortcutSections.Insert(index, _ownProjectsSection);
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to load launcher projects from Project Space.", exception);
            if (IsOwnProjectsRequestCurrent(version, endpoint, user, token))
                PublishOwnProjectsError();
        }
    }

    private bool IsOwnProjectsRequestCurrent(int version, string endpoint, FbUserData user, string token)
    {
        var accounts = FlowBloxAccountManager.Instance;
        return !_shortcutsClosed
            && version == _ownProjectsRefreshVersion
            && string.Equals(endpoint, ProjectShortcut.CurrentApiEndpoint, StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(user, accounts.GetActiveUser(endpoint))
            && token == accounts.GetUserToken(endpoint);
    }

    private void PublishOwnProjectsError()
    {
        _appNotificationService.Publish(new AppNotification
        {
            Id = OwnProjectsNotificationId,
            NotificationType = NotificationType.Error,
            Message = FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.OwnProjectsLoadFailed), typeof(Texts)),
            ExpiresUtc = DateTimeOffset.UtcNow.Add(DefaultNotificationLifetime)
        });
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckMissingProjectSpaceGuidAsync(Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl);
        var previousEndpoint = option.Value;
        var accounts = FlowBloxAccountManager.Instance;
        try
        {
            option.Value = endpoint;
            accounts.SetSession(endpoint, new FbUserData { UserName = "save-check" }, "save-check-token");
            var project = new FlowBloxProject
            {
                ProjectName = "Local project",
                ProjectDescription = "Local description",
                ProjectSpaceGuid = Guid.NewGuid().ToString(),
                ProjectSpaceEndpointUri = endpoint
            };
            var serve = RespondWithMissingProjectAsync(listener);
            var dialog = new CreateOrUpdatePSProjectWindow(project);
            var model = (CreateOrUpdatePSProjectViewModel)dialog.DataContext;
            Exception failure = null;
            dialog.ContentRendered += async (_, _) =>
            {
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (string.IsNullOrWhiteSpace(model.MissingProjectHint) && DateTime.UtcNow < deadline)
                        await Task.Delay(25);

                    check(model.MissingProjectHint?.Contains(project.ProjectSpaceGuid, StringComparison.Ordinal) == true,
                        "A missing Project Space GUID shows a specific recovery hint with its GUID");
                    dialog.UpdateLayout();
                    var hint = (TextBlock)dialog.FindName("MissingProjectHintText");
                    check(hint.Text == model.MissingProjectHint && !model.CanSave && string.IsNullOrEmpty(model.ErrorText),
                        "The save dialog displays the recovery hint and blocks updating the missing project without a generic error popup");
                    check(model.ProjectName == project.ProjectName && model.ProjectDescription == project.ProjectDescription,
                        "Missing remote projects retain the local name and description");
                    model.SelectedTabIndex = 0;
                    check(string.IsNullOrEmpty(model.MissingProjectHint) && string.IsNullOrEmpty(model.ProjectGuid) && model.CanSave,
                        "Switching to Create new project clears the missing-GUID hint and allows creation with a new server GUID");
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    dialog.Close();
                }
            };
            WpfDialogHost.ShowDialog(dialog, _window);
            await serve.WaitAsync(TimeSpan.FromSeconds(5));
            if (failure != null)
                throw failure;
        }
        finally
        {
            accounts.SetSession(endpoint, null, null);
            option.Value = previousEndpoint;
        }
    }

    private static async Task RespondWithMissingProjectAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var body = Encoding.UTF8.GetBytes("{\"error\":\"Project not found\",\"errorCode\":\"PROJECT_NOT_FOUND\"}");
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 404 Not Found\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(body);
    }
}

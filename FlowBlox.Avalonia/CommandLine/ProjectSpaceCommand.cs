using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using FlowBlox.AvaloniaDesktop.Interop.Windows;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Projects;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FlowBlox.AvaloniaDesktop.CommandLine;

internal static class ProjectSpaceCommand
{
    public static bool IsRequested(string[] args) => args.Any(argument =>
        argument == "--list-ps-projects" || argument.StartsWith("--download-ps-project", StringComparison.Ordinal)
        || argument.StartsWith("--upload-ps-project", StringComparison.Ordinal)
        || argument.StartsWith("--reload-ps-project", StringComparison.Ordinal) || argument == "--ps-help");

    public static int Run(string[] args)
    {
        CommandConsole.Attach();
        using var parser = new Parser(settings => settings.HelpWriter = null);
        var parsed = parser.ParseArguments<ProjectSpaceCommandOptions>(args);
        if (parsed is not Parsed<ProjectSpaceCommandOptions> success)
        {
            Console.Error.WriteLine(HelpText.AutoBuild(parsed));
            return parsed.Errors.All(error => error is HelpRequestedError || error is VersionRequestedError) ? 0 : 2;
        }
        if (success.Value.Help)
        {
            Console.Out.WriteLine(HelpText.AutoBuild(parser.ParseArguments<ProjectSpaceCommandOptions>(new[] { "--help" })));
            return 0;
        }

        try
        {
            ExecuteAsync(success.Value).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Project Space command failed.", exception);
            Console.Error.WriteLine(JsonConvert.SerializeObject(new { Error = exception.Message }));
            return exception is UnauthorizedAccessException ? 3 : exception is ArgumentException ? 2 : 1;
        }
    }

    private static async Task ExecuteAsync(ProjectSpaceCommandOptions options)
    {
        if ((options.List ? 1 : 0) + (options.Download != null ? 1 : 0)
            + (options.Upload != null ? 1 : 0) + (options.Reload != null ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one Project Space command. Use --ps-help for usage.");
        if (options.Query != null && !options.List || options.Directory != null && options.Download == null
            || options.Mine && !options.List || options.Mine && options.Private
            || options.Overwrite && options.Download == null && options.Reload == null || options.New && options.Upload == null
            || options.Description != null && options.Upload == null || options.Visibility != null && options.Upload == null)
            throw new ArgumentException("An option does not apply to the selected command. Use --ps-help for usage.");
        if (options.Reload != null && !options.Overwrite)
            throw new ArgumentException("Reload discards local project changes. Pass --overwrite to confirm replacement.");

        FbProjectVisibility? visibility = null;
        if (options.Visibility != null)
        {
            visibility = options.Visibility.ToLowerInvariant() switch
            {
                "private" => FbProjectVisibility.Private,
                "public" => FbProjectVisibility.Public,
                _ => throw new ArgumentException("--visibility must be private or public.")
            };
        }

        var configuration = FlowBloxOptions.GetOptionInstance();
        var endpoint = configuration.GetOption(OptionsConstants.ApiProjectServiceBaseUrl).Value;
        var token = string.Empty;
        if (options.Private || options.Mine || options.Upload != null)
        {
            await new FlowBloxAutoLoginExecutor().TryAutoLoginAsync().ConfigureAwait(false);
            token = FlowBloxAccountManager.Instance.GetUserToken(endpoint);
            if (string.IsNullOrWhiteSpace(token) || !FlowBloxAccountManager.Instance.IsLoggedInFor(endpoint))
                throw new UnauthorizedAccessException("Sign in to FlowBlox with Remember credentials enabled for the configured Project Space endpoint, then retry. Public listing and downloads do not require sign-in.");
        }

        var tools = new ProjectSpaceTools(new FlowBloxWebApiService(endpoint), token);
        object result;
        if (options.List)
        {
            result = (await tools.ListAsync(options.Private, options.Query ?? string.Empty, options.Mine).ConfigureAwait(false))
                .Select(project => new { ProjectSpaceGuid = project.Guid, project.Name, project.Description, project.Visibility })
                .ToList();
        }
        else if (options.Download != null)
        {
            var directory = options.Directory ?? configuration.GetOption(OptionsConstants.PathsProjectDir).Value;
            result = await tools.DownloadAsync(options.Download, Path.GetFullPath(directory), options.Overwrite).ConfigureAwait(false);
        }
        else if (options.Reload != null)
        {
            result = await tools.ReloadAsync(options.Reload, options.Overwrite).ConfigureAwait(false);
        }
        else
        {
            result = await tools.UploadAsync(options.Upload, options.New, options.Description, visibility).ConfigureAwait(false);
        }
        Console.Out.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented, new StringEnumConverter()));
    }
}

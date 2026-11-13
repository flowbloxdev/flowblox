using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Runner;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Downloads;
using FlowBlox.Core.Util.Execution;
using FlowBlox.Test.Runtime;

namespace FlowBloxTest.FlowBlocks.Media;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExecutionPrerequisiteTests : FlowBloxTestsBase
{
    [TestMethod]
    public void ExtensionTool_MissingExecutable_IdentifiesItsOwnQuickStartWithoutCoreRegistration()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "extension-tool.exe");
        var exception = Assert.Throws<ExecutionPrerequisiteException>(() =>
            ExecutionPrerequisites.RequireExecutable<ExtensionToolSettings>("extension-tool", path));

        Assert.AreEqual(typeof(ExtensionToolSettings), exception.QuickStartTargetType);
        Assert.IsTrue(exception.Message.Contains("Extension tool setup", StringComparison.Ordinal));
        Assert.IsTrue(exception.Message.Contains(path, StringComparison.Ordinal));
        Assert.AreEqual("extension-tool unavailable", exception.NotificationMessage);
    }

    [TestMethod]
    public void MissingDownloader_EmitsScopedErrorAndFailureResultWithDetailedSetupInstructions()
    {
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        var start = CreateFlowBlock<StartFlowBlock>();
        var block = CreateFlowBlock<MediaDownloadFlowBlock>(start);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "yt-dlp.exe");
        block.Provider = new YtDlpMediaDownloadProvider { ExecutablePath = path };
        block.Url = "https://example.test/video";
        block.OutputDirectory = Path.GetTempPath();
        foreach (var destination in new[] { MediaDownloadDestinations.ErrorMessage, MediaDownloadDestinations.FailureReason })
        {
            var field = Registry.CreateField(block);
            field.Name = destination.ToString();
            block.ResultFields.Add(new ResultFieldByEnumValue<MediaDownloadDestinations>
            {
                EnumValue = destination,
                ResultField = field
            });
        }
        string? notification = null;
        block.OnError += (_, message) => notification = message;

        block.Execute(new FlowBloxUnitTestRuntime(project), null!);

        Assert.AreEqual("yt-dlp unavailable", notification);
        Assert.AreEqual("False", GetResult(block, MediaDownloadDestinations.Success));
        Assert.AreEqual("RequiredComponentMissing", GetResult(block, MediaDownloadDestinations.FailureReason));
        Assert.IsTrue(GetResult(block, MediaDownloadDestinations.ErrorMessage).Contains(path, StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, GetResult(block, MediaDownloadDestinations.FilePath));
    }

    [TestMethod]
    public async Task MissingFFmpeg_IsReportedBeforeStartingTheDownload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxPrerequisites-" + Guid.NewGuid().ToString("N"));
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var provider = new FFmpegMediaDownloadProvider
        {
            OutputName = "result",
            FFmpeg = new FFmpegTool { BinaryDirectory = Path.Combine(directory, "missing") }
        };
        try
        {
            var exception = await Assert.ThrowsAsync<ExecutionPrerequisiteException>(() => provider.DownloadAsync(
                new MediaDownloadRequest
                {
                    Url = "https://example.test/video.mp4",
                    OutputDirectory = directory,
                    Format = MediaDownloadFormat.Video
                },
                new TransferProgressReporter(_ => { }),
                CancellationToken.None));

            Assert.AreEqual("FFmpeg", exception.ComponentName);
            Assert.AreEqual(typeof(FFmpegTool), exception.QuickStartTargetType);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExplicitDenoPath_IsCheckedWhileAutomaticRuntimeSelectionRemainsWithYtDlp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxPrerequisites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in new[] { "yt-dlp", "ffmpeg", "ffprobe" })
                File.WriteAllText(Path.Combine(directory, name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)), string.Empty);

            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var provider = new YtDlpMediaDownloadProvider
            {
                ExecutablePath = Path.Combine(directory, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp"),
                FFmpeg = new FFmpegTool { BinaryDirectory = directory },
                DenoPath = Path.Combine(directory, "missing-deno.exe")
            };
            var exception = Assert.Throws<ExecutionPrerequisiteException>(provider.ValidateExecutionPrerequisites);
            Assert.AreEqual("Deno", exception.ComponentName);

            provider.DenoPath = string.Empty;
            provider.ValidateExecutionPrerequisites();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExtractorRuntimeFailure_ProvidesSetupGuidanceWithoutDependingOnTheSourceUrl()
    {
        var exception = YtDlpMediaDownloadProvider.CreateDownloadFailure(
            "Discovery failed: ",
            new[] { "No supported JavaScript runtime could be found." });

        Assert.IsInstanceOfType<ExecutionPrerequisiteException>(exception);
        Assert.AreEqual(typeof(YtDlpMediaDownloadProvider), ((ExecutionPrerequisiteException)exception).QuickStartTargetType);
        Assert.IsTrue(exception.Message.Contains("JavaScript runtime", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Runner_MissingComponent_ReturnsTheActualSetupFailureInsteadOfTheGenericAbortReason()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxPrerequisiteRunner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var originalProject = FlowBloxProjectManager.Instance.ActiveProject;
        try
        {
            var project = new FlowBloxProject { ProjectName = "Prerequisite runner test" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var start = CreateFlowBlock<StartFlowBlock>();
            var block = CreateFlowBlock<MediaDownloadFlowBlock>(start);
            var provider = CreateManagedObject<YtDlpMediaDownloadProvider>();
            provider.ExecutablePath = Path.Combine(directory, "missing-yt-dlp.exe");
            block.Provider = provider;
            block.Url = "https://example.test/video";
            block.OutputDirectory = directory;
            var file = Path.Combine(directory, "prerequisites.fbprj");
            project.Save(file);

            var response = FlowBloxProjectRunner.Run(new RunnerRequest { ProjectFile = file });

            Assert.IsFalse(response.Success);
            Assert.IsTrue(response.ErrorMessage.Contains(provider.ExecutablePath, StringComparison.Ordinal), response.Exception ?? response.ErrorMessage);
            Assert.IsTrue(response.ErrorMessage.Contains("Quick Starts", StringComparison.Ordinal), response.ErrorMessage);
            Assert.IsTrue(response.AbortException.Contains(nameof(ExecutionPrerequisiteException), StringComparison.Ordinal));
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = originalProject;
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CachedMediaFlowBlock_ReusesOutputWithoutRequiringTheProvidersBinaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxCachedMedia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "episode.mp3");
            File.WriteAllText(path, "existing media");
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var start = CreateFlowBlock<StartFlowBlock>();
            var block = CreateFlowBlock<MediaDownloadFlowBlock>(start);
            block.Provider = new FFmpegMediaDownloadProvider
            {
                OutputName = "episode",
                FFmpeg = new FFmpegTool { BinaryDirectory = Path.Combine(directory, "not-installed") }
            };
            block.Url = "https://example.test/episode.mp3";
            block.OutputDirectory = directory;
            block.Format = MediaDownloadFormat.Mp3;
            var errors = new List<string>();
            block.OnError += (_, message) => errors.Add(message);

            Assert.IsTrue(block.Execute(new FlowBloxUnitTestRuntime(project), null!));

            Assert.IsEmpty(errors);
            Assert.AreEqual(bool.TrueString, GetResult(block, MediaDownloadDestinations.Success));
            Assert.AreEqual(path, GetResult(block, MediaDownloadDestinations.FilePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GetResult(MediaDownloadFlowBlock block, MediaDownloadDestinations destination)
    {
        var field = block.ResultFields.Single(item => item.EnumValue == destination).ResultField;
        return block.GridElementResult.Results.Last().FieldValueMappings
            .Single(mapping => ReferenceEquals(mapping.Field, field)).Value;
    }
}
